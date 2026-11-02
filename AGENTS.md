# AGENTS.md

Guidance for AI coding agents (and people) working in this repository. The [README](README.md) is the full documentation
for users of the sample; this file is what you need to change it safely.

## What this is

Grot's Pizzeria is a sample application for demonstrating ASP.NET Core 11's support for OpenTelemetry. A Blazor WebAssembly UI
talks to an ASP.NET Core Minimal API (the BFF), which calls four microservices in different languages. Everything emits logs,
metrics and traces with the OpenTelemetry SDKs, and the end-to-end flow across all of them is the point of the sample. It is
a demo, so keep each part simple, modern and idiomatic for its language.

| Part | Language | Where | Notes |
| --- | --- | --- | --- |
| UI | C# (Blazor WebAssembly) | `src/GrotsPizzeria.Client` | Telemetry in `Telemetry.cs` |
| BFF | C# (ASP.NET Core 11 Minimal API) | `src/GrotsPizzeria.Server` | Identity (SQLite), JWT minting, serves the UI |
| Shared contracts | C# | `src/GrotsPizzeria.Shared` | Used by the UI and the BFF |
| Aspire AppHost | C# | `src/GrotsPizzeria.AppHost` | Runs everything for local debugging |
| `grots-menu` | Node.js (Express) | `services/menu` | Menu, prices, special offers |
| `grots-orders` | Java (Spring Boot, OpenTelemetry Java agent) | `services/orders` | Orchestrates an order |
| `grots-checkout` | Python (FastAPI) | `services/checkout` | Payments |
| `grots-delivery` | Go | `services/delivery` | Delivery tracking |
| End-to-end tests | C# (Playwright, xUnit) | `tests/GrotsPizzeria.EndToEndTests` | |

Also: `docker-compose.yml` (the stack plus the `grafana/otel-lgtm` backend), `deploy/grafana` (the provisioned dashboard),
`weaver/registry` (the sample's own semantic conventions) and `docs/service-contract.md` (the contract between services).

## Verifying a change

Run everything CI runs with the PowerShell 7 script in the repository root (it works on Windows, macOS and Linux):

```sh
pwsh ./build.ps1                  # every step; Docker must be running
pwsh ./build.ps1 -Skip e2e        # everything except the slow end-to-end tests
pwsh ./build.ps1 -Only go,python  # just some steps
pwsh ./build.ps1 -List            # the steps: markdown, javascript, workflows, config, docs, go, python, node, java, dotnet, e2e
```

The script's help and the README (Prerequisites) list what each step needs. The toolchain versions are pinned by `global.json`,
`package.json`, `go.mod`, `pom.xml`, `requirements*.txt` and the Dockerfiles.

Before you finish, run the steps for the code you changed, plus `markdown` if you touched a Markdown file. A change to a
service, the BFF, the UI or the Compose file also needs `e2e`. CI treats warnings as errors for .NET (it sets `CI=true`).

The linters are the usual ones for each language, with their default or recommended rules. Fix what they report rather than
disabling rules:

| Code | Linter |
| --- | --- |
| JavaScript | ESLint (`npm run lint`) |
| CSS | Stylelint with its standard config (`npm run lint:css`) |
| Markdown | markdownlint-cli2 (`npm run lint:md`) |
| Go | gofmt and golangci-lint v2 |
| Python | Ruff (`ruff check`) |
| Java | PMD (quickstart rules) through Maven |
| GitHub Actions | actionlint, and zizmor in CI |
| C# | The .NET analyzers (`AnalysisLevel` is `latest-recommended`) |

## Running the sample

```sh
docker compose up --build --wait -d    # the web app is at http://localhost:8080, Grafana at http://localhost:3000
```

The demo account is `grot` / `Grot!Pizza1`. Do not tear the stack down (`docker compose down`) after a task unless asked to:
people often keep it running to try the change by hand.

The end-to-end tests need a stack without the sample's deliberate randomness: start it with `CHECKOUT_FAILURE_RATE=0` and
`SPECIAL_OFFERS=off` (the README explains why), or let the tests start the stack themselves through the AppHost. Afterwards,
start the stack again with its default settings.

## Conventions

### Telemetry

- Every application emits logs, metrics and traces with its language's **OpenTelemetry SDK** (the Java service uses the
  OpenTelemetry Java agent), including HTTP server and client instrumentation.
- Use only packages published by the OpenTelemetry project, and keep extra dependencies to a minimum. Do not add vendor SDKs or
  third-party instrumentation.
- Configure the SDKs with the standard `OTEL_*` environment variables, and make the defaults work. In code, set only what an
  environment variable cannot (for example the browser, which has none).
- Export OTLP over `http/protobuf`, and send it to the LGTM container by default.
- Health checks are not traced. Context is propagated with W3C Trace Context and Baggage.
- The sample's own attributes and metrics are defined in `weaver/registry`. When you add or change telemetry, update the
  registry and the dashboard, and check the result with the Weaver live-check described in the README.
- The OTLP endpoint in the Compose stack is unauthenticated, which is only acceptable for a local sample. Do not add real
  credentials anywhere.

### Services and security

- The services speak JSON over HTTP (`camelCase`), as described in `docs/service-contract.md`. Update that document when a
  contract changes.
- Calls between the BFF and the services are secured with short-lived JWTs (HS256, a shared secret in `JWT_SIGNING_KEY`).
- Users must only ever see their own orders (the BFF answers 404 for anyone else's), and an order must never be accepted for
  zero or a negative amount. Validate input at each boundary, not only in the UI.
- The ASP.NET Core apps use ASP.NET Core Identity for authentication.
- Each application describes its API with OpenAPI (`/openapi.json`), and shows it with a Swagger UI page that needs no sign-in
  (at `/`, or `/swagger` for the BFF). When you add or change an endpoint, describe it in the document (summary, responses,
  schemas) in the way of its language: see the README. The page and its theme are identical in every application, each having
  its own copy: change one and copy it to the others (`build.ps1 -Only docs` checks, and CI runs it).
- Never commit real secrets. The demo values in the repository (the demo account, the JWT key in `docker-compose.yml`) are
  synthetic. Check staged changes for credentials before committing.

### Code

- Never assume a currency. The menu service says what its prices are in (`CURRENCY`, which is `GBP` unless configured), every item and
  quote carries it as `currency`, and the other services and the UI use that: the UI formats amounts with the `currency` it was given.
- Do not hard-code the tax rate or the special offer discount outside the service that owns it (`TAX_RATE` in checkout,
  `OFFER_DISCOUNT` in menu). Clients and tests show or derive them from what the services return (`taxRate`, `offer.discount`).
- In code, comments and names, say "fake" or "mock" for stand-ins, not "pretend".
- In C#, put each type in its own file, named after the type (`Foo.cs`, and ``Bar`1.cs`` for `Bar<T>`), including the small
  models a Razor page uses. Do not suppress analyzer rules to avoid this.
- Comments explain why, not what, and match the existing style of the file you are editing.
- Follow `.editorconfig` (2-space indent for most files, 4 for C#, Java and Python, tabs for Go) and `.gitattributes`: **LF line
  endings** everywhere (including `*.ps1`), except `*.cmd` and `*.bat`. Do not commit CRLF files.
- The UI, including its CSS, is hand-written and styled to resemble Grafana, with light and dark themes. Keep it accessible: the
  tests run axe-core over every page in both themes.

### Tests

- The end-to-end tests cover core user journeys and the security and validation rules above. Do not assert on layout, styling or
  other detail that will change; those tests become brittle.
- Find elements by role, label or text where you can, and otherwise by `data-testid` (`GetByTestId`). Never use CSS classes or
  element selectors. Add a `data-testid` to the markup when a test needs one.
- Each service has its own unit tests (`go test`, `pytest`, `npm test`, `mvn verify`). Add tests with the change.

### Repository and CI

- GitHub Actions are pinned to a full commit SHA with the version in a comment (`uses: owner/repo@<sha> # v1.2.3`), at the latest
  release. Give workflow jobs a `name`, and keep the permissions minimal.
- Dependabot runs weekly on Mondays, and it also updates the Docker images and Compose file.
- When you add or change a build or test step, update `ci.yml` and `build.ps1` together, so they stay equivalent.
- A pull request only runs the CI jobs for the parts of the repository it changes: the `changes` job in `ci.yml` decides which,
  from a filter per job. When you add a job, or move the files a job depends on, update its filter (a push to `main` runs everything).
- Do not commit, push or open pull requests unless asked to.
