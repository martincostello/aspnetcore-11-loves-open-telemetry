#!/usr/bin/env pwsh
#Requires -Version 7.0

<#
.SYNOPSIS
    Makes the same requests to Grot's Pizzeria every time, so that there is telemetry to look at (and to check).

.DESCRIPTION
    Each round runs these scenarios through the BFF, one trace each, and says what came back and whether that was what was expected:

      browse            the menu, the settings and who is signed in (nobody)
      order             signs in, orders with a card and with cash, then looks at the orders and tracks a delivery
      declined          a card ending in 0000: the payment is declined (402, a client error)
      gateway-failure   a card ending in 0503: the payment gateway is down (502 from the BFF, a 5xx, which is a server error)
      invalid           an order that cannot be placed (400)
      not-yours         asking for someone else's order (404, the same as one that does not exist)
      not-found         a route that does not exist (404) and one that needs signing in (401)

    Every request carries a W3C traceparent header that this script chose, and each scenario prints the trace id, so a trace
    can be found in Grafana (Tempo: the trace id) or the Aspire dashboard without searching.

    The statuses are only certain when the checkout service does not fail at random: start the stack with CHECKOUT_FAILURE_RATE=0
    (see the README). Otherwise a few orders will fail with a 502, which is shown as unexpected, but is not an error here unless
    -Strict is used.

.PARAMETER BaseUrl
    Where the BFF is. http://localhost:8080 by default, which is the Docker Compose stack.

.PARAMETER Rounds
    How many times to run the scenarios. 5 by default; a few minutes of traffic for the dashboard is around 30.

.PARAMETER PauseMilliseconds
    How long to wait between scenarios, so that the traffic is spread out. 200 by default.

.PARAMETER NoFailures
    Leaves out the scenarios that end in errors (the declined payment is an answer and stays).

.PARAMETER Strict
    Exits with 1 if any status was not the expected one.

.PARAMETER Username
    The account to sign in with: the demo account by default.

.PARAMETER Password
    Its password.

.EXAMPLE
    ./scripts/generate-traffic.ps1
    Five rounds against the Compose stack.

.EXAMPLE
    ./scripts/generate-traffic.ps1 -Rounds 30 -PauseMilliseconds 1000
    About two minutes of traffic, for looking at the dashboard.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:8080',
    [ValidateRange(1, 10000)]
    [int] $Rounds = 5,
    [ValidateRange(0, 60000)]
    [int] $PauseMilliseconds = 200,
    [switch] $NoFailures,
    [switch] $Strict,
    [string] $Username = 'grot',
    [string] $Password = 'Grot!Pizza1'
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
$results = [System.Collections.Generic.List[object]]::new()

function New-TraceParent {
    $trace = [Guid]::NewGuid().ToString('N')
    $span = [Guid]::NewGuid().ToString('N').Substring(0, 16)
    return @{ TraceId = $trace; Header = "00-$trace-$span-01" }
}

# One request, in the trace of the scenario; says what came back, and records whether that was expected.
function Invoke-Step {
    param(
        [string] $Scenario,
        [string] $Name,
        [string] $Method,
        [string] $Path,
        $Body = $null,
        [Microsoft.PowerShell.Commands.WebRequestSession] $Session,
        [string] $TraceParent,
        [int] $Expect
    )

    $arguments = @{
        Uri                = "$BaseUrl$Path"
        Method             = $Method
        Headers            = @{ traceparent = $TraceParent }
        WebSession         = $Session
        SkipHttpErrorCheck = $true
    }

    if ($null -ne $Body) {
        $arguments.Body = $Body | ConvertTo-Json -Depth 5 -Compress
        $arguments.ContentType = 'application/json'
    }

    $response = Invoke-WebRequest @arguments
    $status = [int] $response.StatusCode

    $results.Add([pscustomobject] @{ Scenario = $Scenario; Step = $Name; Status = $status; Expected = $Expect; Ok = $status -eq $Expect })

    return [pscustomobject] @{ Status = $status; Body = if ($response.Content) { try { $response.Content | ConvertFrom-Json } catch { $null } } }
}

function New-Session { [Microsoft.PowerShell.Commands.WebRequestSession]::new() }

function Show-Scenario([string] $Name, [string] $TraceId, [int] $From) {
    $steps = $results.GetRange($From, $results.Count - $From) | ForEach-Object { "$($_.Step) $($_.Status)$(if (-not $_.Ok) { '?' })" }
    Write-Host ('  {0,-16} {1}  {2}' -f $Name, $TraceId, ($steps -join ', '))
}

function Get-Items { @(@{ pizzaId = 'margherita'; quantity = 2 }, @{ pizzaId = 'garlic-bread'; quantity = 1 }) }

function New-Order([string] $Method, [string] $CardLast4) {
    $payment = @{ method = $Method }
    if ($CardLast4) { $payment.cardLast4 = $CardLast4 }
    return @{ address = '1 Loki Lane, Tempo Town'; items = Get-Items; payment = $payment }
}

Write-Host "Grot's Pizzeria at $BaseUrl, $Rounds round(s). Each line is a scenario, its trace id and the status of each step (? is not what was expected)."

$health = Invoke-WebRequest -Uri "$BaseUrl/healthz" -SkipHttpErrorCheck
if ($health.StatusCode -ne 200) {
    throw "$BaseUrl is not running: GET /healthz returned $($health.StatusCode)."
}

for ($round = 1; $round -le $Rounds; $round++) {
    Write-Host "Round $round of $Rounds"

    # browse
    $t = New-TraceParent; $from = $results.Count; $anonymous = New-Session
    Invoke-Step browse 'menu' GET '/api/menu' -Session $anonymous -TraceParent $t.Header -Expect 200 | Out-Null
    Invoke-Step browse 'config' GET '/api/config' -Session $anonymous -TraceParent $t.Header -Expect 200 | Out-Null
    Invoke-Step browse 'me' GET '/api/auth/me' -Session $anonymous -TraceParent $t.Header -Expect 200 | Out-Null
    Show-Scenario browse $t.TraceId $from
    Start-Sleep -Milliseconds $PauseMilliseconds

    # order
    $t = New-TraceParent; $from = $results.Count; $session = New-Session
    Invoke-Step order 'sign in' POST '/api/auth/login' @{ username = $Username; password = $Password } -Session $session -TraceParent $t.Header -Expect 200 | Out-Null
    $card = Invoke-Step order 'card order' POST '/api/orders' (New-Order card 4242) -Session $session -TraceParent $t.Header -Expect 201
    Invoke-Step order 'cash order' POST '/api/orders' (New-Order cash $null) -Session $session -TraceParent $t.Header -Expect 201 | Out-Null

    if ($card.Body.id) {
        Invoke-Step order 'order' GET "/api/orders/$($card.Body.id)" -Session $session -TraceParent $t.Header -Expect 200 | Out-Null
        Invoke-Step order 'tracking' GET "/api/orders/$($card.Body.id)/tracking" -Session $session -TraceParent $t.Header -Expect 200 | Out-Null
    }

    Invoke-Step order 'orders' GET '/api/orders' -Session $session -TraceParent $t.Header -Expect 200 | Out-Null
    Show-Scenario order $t.TraceId $from
    $order = $card.Body.id
    Start-Sleep -Milliseconds $PauseMilliseconds

    # declined
    $t = New-TraceParent; $from = $results.Count
    Invoke-Step declined 'declined card' POST '/api/orders' (New-Order card 0000) -Session $session -TraceParent $t.Header -Expect 402 | Out-Null
    Show-Scenario declined $t.TraceId $from
    Start-Sleep -Milliseconds $PauseMilliseconds

    if (-not $NoFailures) {
        # gateway-failure
        $t = New-TraceParent; $from = $results.Count
        Invoke-Step gateway-failure 'gateway down' POST '/api/orders' (New-Order card 0503) -Session $session -TraceParent $t.Header -Expect 502 | Out-Null
        Show-Scenario gateway-failure $t.TraceId $from
        Start-Sleep -Milliseconds $PauseMilliseconds
    }

    # invalid
    $t = New-TraceParent; $from = $results.Count
    $nothing = New-Order cash $null; $nothing.items = @(@{ pizzaId = 'margherita'; quantity = 0 })
    Invoke-Step invalid 'no pizzas' POST '/api/orders' $nothing -Session $session -TraceParent $t.Header -Expect 400 | Out-Null
    Show-Scenario invalid $t.TraceId $from
    Start-Sleep -Milliseconds $PauseMilliseconds

    # not-yours
    $t = New-TraceParent; $from = $results.Count; $stranger = New-Session
    $name = "traffic-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
    Invoke-Step not-yours 'register' POST '/api/auth/register' @{ username = $name; displayName = $name; password = 'Pizza!Passw0rd' } -Session $stranger -TraceParent $t.Header -Expect 201 | Out-Null
    if ($order) {
        Invoke-Step not-yours 'their order' GET "/api/orders/$order" -Session $stranger -TraceParent $t.Header -Expect 404 | Out-Null
    }
    Show-Scenario not-yours $t.TraceId $from
    Start-Sleep -Milliseconds $PauseMilliseconds

    # not-found
    $t = New-TraceParent; $from = $results.Count
    Invoke-Step not-found 'no such route' GET '/api/nothing-here' -Session $anonymous -TraceParent $t.Header -Expect 404 | Out-Null
    Invoke-Step not-found 'signed out' GET '/api/orders' -Session $anonymous -TraceParent $t.Header -Expect 401 | Out-Null
    Show-Scenario not-found $t.TraceId $from
    Start-Sleep -Milliseconds $PauseMilliseconds
}

$unexpected = @($results | Where-Object { -not $_.Ok })
Write-Host ''
Write-Host "$($results.Count) requests, $($unexpected.Count) with a status that was not the expected one."

if ($unexpected.Count -gt 0) {
    $unexpected | Group-Object Scenario, Step, Status, Expected | ForEach-Object {
        $first = $_.Group[0]
        Write-Host ("  {0} / {1}: {2} (expected {3}) x{4}" -f $first.Scenario, $first.Step, $first.Status, $first.Expected, $_.Count)
    }

    Write-Host 'Random payment failures (CHECKOUT_FAILURE_RATE) are the likely cause: start the stack with CHECKOUT_FAILURE_RATE=0 to turn them off.'

    if ($Strict) {
        exit 1
    }
}
