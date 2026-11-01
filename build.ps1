#!/usr/bin/env pwsh
#Requires -Version 7.0

<#
.SYNOPSIS
    Runs all of the linting, building and testing that CI does, so a change can be verified locally before it is pushed.

.DESCRIPTION
    Works with PowerShell 7 on Windows, macOS and Linux. Each step mirrors a job of .github/workflows/ci.yml, and runs
    from the repository root whatever the current directory is. Every step runs even if an earlier one fails (unless
    -FailFast is used), and a summary at the end shows what passed. The exit code is 0 only if everything did.

    Steps (in the order they run):

      markdown    markdownlint
      javascript  ESLint and Stylelint (the JavaScript and the CSS)
      workflows   actionlint on the GitHub Actions workflows (needs Docker)
      config      the Docker Compose files, the Grafana dashboard and the Weaver registry (needs Docker)
      go          gofmt, golangci-lint, go build, go test               (grots-delivery)
      python      Ruff and pytest                                       (grots-checkout)
      node        npm test                                              (grots-menu)
      java        PMD and Maven verify                                  (grots-orders)
      dotnet      builds the whole solution, with warnings as errors (as CI does)
      e2e         the Playwright end-to-end tests; they start the whole stack, so they need Docker and take a few minutes

    What each step needs installed is in the README (Prerequisites). A step whose tool is missing fails with a message
    saying so; leave it out with -Skip.

.PARAMETER Only
    Runs only these steps.

.PARAMETER Skip
    Runs everything except these steps. For a quick check that does not start the stack: -Skip e2e

.PARAMETER Configuration
    The .NET build configuration (Release, as CI uses, by default).

.PARAMETER FailFast
    Stops at the first step that fails.

.PARAMETER List
    Lists the steps and exits.

.EXAMPLE
    ./build.ps1
    Everything.

.EXAMPLE
    ./build.ps1 -Skip e2e
    Everything except the end-to-end tests, which are the slow part.

.EXAMPLE
    ./build.ps1 -Only go, python
    Just the Go and the Python checks.
#>
[CmdletBinding()]
param(
    [ArgumentCompleter({ 'markdown', 'javascript', 'workflows', 'config', 'go', 'python', 'node', 'java', 'dotnet', 'e2e' })]
    [string[]] $Only,

    [ArgumentCompleter({ 'markdown', 'javascript', 'workflows', 'config', 'go', 'python', 'node', 'java', 'dotnet', 'e2e' })]
    [string[]] $Skip,

    [string] $Configuration = 'Release',

    [switch] $FailFast,

    [switch] $List
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$workflow = Get-Content -Raw -LiteralPath (Join-Path $root '.github/workflows/ci.yml')

$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Skip = @($Skip | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

# The shared key the services need to start (the tests start some of them)
$env:JWT_SIGNING_KEY ??= 'local-build-only-signing-key-0123456789-0123456789'

function Assert-Tool {
    param([string] $Name, [string] $Hint)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' was not found. $Hint"
    }
}

function Assert-Docker {
    Assert-Tool 'docker' 'Install Docker (https://docs.docker.com/get-docker/).'
    & docker info *> $null

    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is installed but not running. Start it and try again.'
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)] [string] $Command,
        [string[]] $Arguments = @(),
        [string] $WorkingDirectory = $root
    )

    Write-Host "> $Command $($Arguments -join ' ')" -ForegroundColor DarkGray
    Push-Location -LiteralPath $WorkingDirectory

    try {
        & $Command @Arguments

        if ($LASTEXITCODE -ne 0) {
            throw "'$Command $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$script:toolingInstalled = $false

function Install-Tooling {
    if (-not $script:toolingInstalled) {
        Assert-Tool 'npm' 'Install Node.js (https://nodejs.org).'
        Invoke-Native npm @('ci', '--no-audit', '--no-fund')
        $script:toolingInstalled = $true
    }
}

function Get-WorkflowImage {
    param([string] $Pattern)

    $match = [regex]::Match($workflow, $Pattern)

    if (-not $match.Success) {
        throw "Could not find an image matching '$Pattern' in .github/workflows/ci.yml."
    }

    return $match.Value
}

$steps = [ordered]@{

    markdown   = {
        Install-Tooling
        Invoke-Native npm @('run', 'lint:md')
    }

    javascript = {
        Install-Tooling
        Invoke-Native npm @('run', 'lint', '--', '--max-warnings', '0')
        Invoke-Native npm @('run', 'lint:css', '--', '--max-warnings', '0')
    }

    workflows  = {
        Assert-Docker
        $image = Get-WorkflowImage 'rhysd/actionlint@sha256:[0-9a-f]{64}'
        Invoke-Native docker @('run', '--rm', '--volume', "${root}:/repo", '--workdir', '/repo', $image)
    }

    config     = {
        Assert-Docker
        Invoke-Native docker @('compose', '-f', 'docker-compose.yml', 'config', '--quiet')
        Invoke-Native docker @('compose', '-f', 'docker-compose.yml', '-f', 'docker-compose.weaver.yml', 'config', '--quiet')

        foreach ($file in Get-ChildItem -Path (Join-Path $root 'deploy/grafana/dashboards') -Filter '*.json') {
            $dashboard = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json

            if (-not $dashboard.uid -or -not $dashboard.panels) {
                throw "$($file.Name) is not a dashboard (it needs a uid and panels)."
            }

            Write-Host "$($file.Name): $($dashboard.panels.Count) panels"
        }

        $weaver = Get-WorkflowImage 'otel/weaver:v[0-9.]+'
        Invoke-Native docker @('run', '--rm', '--volume', "$(Join-Path $root 'weaver/registry'):/registry:ro", $weaver, 'registry', 'check', '--registry', '/registry')
    }

    go         = {
        Assert-Tool 'go' 'Install Go (https://go.dev/dl/).'
        Assert-Tool 'golangci-lint' 'Install golangci-lint v2 (https://golangci-lint.run/welcome/install/); it must be built with a Go at least as new as the one this uses.'
        $service = Join-Path $root 'services/delivery'

        Write-Host '> gofmt -l .' -ForegroundColor DarkGray
        Push-Location -LiteralPath $service

        try {
            $unformatted = @(& gofmt -l .)
        }
        finally {
            Pop-Location
        }

        if ($unformatted.Count -gt 0) {
            throw "These files are not formatted (run gofmt -w): $($unformatted -join ', ')"
        }

        Invoke-Native golangci-lint @('run') -WorkingDirectory $service
        Invoke-Native go @('build', './...') -WorkingDirectory $service
        Invoke-Native go @('test', './...') -WorkingDirectory $service
    }

    python     = {
        $python = @('python3', 'python') | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1

        if (-not $python) {
            throw "Python was not found. Install Python 3 (https://www.python.org/downloads/)."
        }

        $service = Join-Path $root 'services/checkout'
        $venv = Join-Path $service '.venv'

        # A virtual environment of its own, so nothing is installed into the machine's Python
        if (-not (Test-Path -LiteralPath $venv)) {
            Invoke-Native $python @('-m', 'venv', $venv)
        }

        $venvPython = if ($IsWindows) { Join-Path $venv 'Scripts/python.exe' } else { Join-Path $venv 'bin/python' }

        Invoke-Native $venvPython @('-m', 'pip', 'install', '--quiet', '--disable-pip-version-check', '-r', 'requirements-dev.txt') -WorkingDirectory $service
        Invoke-Native $venvPython @('-m', 'ruff', 'check', '.') -WorkingDirectory $service
        Invoke-Native $venvPython @('-m', 'pytest') -WorkingDirectory $service
    }

    node       = {
        Assert-Tool 'npm' 'Install Node.js (https://nodejs.org).'
        $service = Join-Path $root 'services/menu'

        Invoke-Native npm @('ci', '--no-audit', '--no-fund') -WorkingDirectory $service
        Invoke-Native npm @('test') -WorkingDirectory $service
    }

    java       = {
        Assert-Tool 'java' 'Install a JDK (https://adoptium.net); Maven is provided by the wrapper.'

        $version = (& java -version 2>&1 | Select-Object -First 1).ToString()
        $major = [regex]::Match($version, '"(\d+)').Groups[1].Value

        if (-not $major -or [int] $major -lt 25) {
            throw "The orders service needs JDK 25 or later, but found: $version"
        }

        $service = Join-Path $root 'services/orders'
        $arguments = @('--batch-mode', '--no-transfer-progress', 'verify', 'pmd:check')

        if ($IsWindows) {
            Invoke-Native (Join-Path $service 'mvnw.cmd') $arguments -WorkingDirectory $service
        }
        else {
            Invoke-Native sh (@('./mvnw') + $arguments) -WorkingDirectory $service
        }
    }

    dotnet     = {
        Assert-Tool 'dotnet' 'Install the .NET SDK in global.json (https://dotnet.microsoft.com/download).'

        $previous = $env:CI
        $env:CI = 'true'

        try {
            Invoke-Native dotnet @('build', 'GrotsPizzeria.slnx', '--configuration', $Configuration)
        }
        finally {
            $env:CI = $previous
        }
    }

    e2e        = {
        Assert-Tool 'dotnet' 'Install the .NET SDK in global.json (https://dotnet.microsoft.com/download).'
        Assert-Docker

        $project = 'tests/GrotsPizzeria.EndToEndTests'
        Invoke-Native dotnet @('build', $project, '--configuration', $Configuration)

        $playwright = Get-ChildItem -Path (Join-Path $root "$project/bin/$Configuration") -Recurse -Filter 'playwright.ps1' | Select-Object -First 1
        Invoke-Native (Get-Process -Id $PID).Path @('-NoProfile', '-File', $playwright.FullName, 'install', 'chromium')

        Invoke-Native dotnet @('test', '--project', $project, '--configuration', $Configuration, '--no-build')
    }
}

foreach ($name in @($Only) + @($Skip)) {
    if ($name -notin $steps.Keys) {
        throw "Unknown step '$name'. The steps are: $($steps.Keys -join ', ')."
    }
}

if ($List) {
    $steps.Keys | ForEach-Object { Write-Host $_ }
    return
}

$selected = @($steps.Keys | Where-Object { (-not $Only -or $_ -in $Only) -and $_ -notin $Skip })

if ($selected.Count -eq 0) {
    Write-Warning 'Nothing to run.'
    return
}

$results = foreach ($name in $selected) {
    Write-Host ''
    Write-Host "=== $name " -ForegroundColor Cyan -NoNewline
    Write-Host ('=' * [Math]::Max(3, 70 - $name.Length)) -ForegroundColor Cyan

    $timer = [Diagnostics.Stopwatch]::StartNew()

    try {
        & $steps[$name] | Out-Host
        $outcome = 'passed'
        $detail = ''
    }
    catch {
        $outcome = 'FAILED'
        $detail = $_.Exception.Message
        Write-Host $detail -ForegroundColor Red
    }

    $timer.Stop()

    [pscustomobject] @{ Step = $name; Result = $outcome; Duration = '{0:mm\:ss}' -f $timer.Elapsed; Detail = $detail }

    if ($outcome -eq 'FAILED' -and $FailFast) {
        break
    }
}

Write-Host ''
Write-Host '=== Summary ===' -ForegroundColor Cyan

foreach ($result in $results) {
    $colour = if ($result.Result -eq 'passed') { 'Green' } else { 'Red' }
    Write-Host ('{0,-12} {1,-8} {2}' -f $result.Step, $result.Result, $result.Duration) -ForegroundColor $colour
}

$failed = @($results | Where-Object Result -eq 'FAILED')
$skipped = @($steps.Keys | Where-Object { $_ -notin $selected })

if ($skipped.Count -gt 0) {
    Write-Host "Not run: $($skipped -join ', ')" -ForegroundColor DarkGray
}

if ($failed.Count -gt 0) {
    Write-Host ''
    Write-Host "$($failed.Count) of $($results.Count) steps failed: $(($failed.Step) -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host "All $($results.Count) steps passed." -ForegroundColor Green
