# grots-checkout (Python)

Payments for Grot's Pizzeria. Called by `grots-orders` to charge for an order.

* `POST /checkout` – "authorizes" a payment (simulated 50–250ms gateway call), adds tax (8% by default) and returns a receipt.
  `402` when the card ends in `0000`; `503` for a configurable fraction of requests; `400` for invalid input.
* `GET /healthz` – anonymous; every other endpoint requires a valid JWT (see below).

See [`docs/service-contract.md`](../../docs/service-contract.md) for the full API.

Built with FastAPI, Pydantic and Uvicorn.

## Telemetry

Zero-code instrumentation with the OpenTelemetry Python distro, using only packages published by OpenTelemetry:

| Package | Provides |
| --- | --- |
| `opentelemetry-distro` | `opentelemetry-instrument` and SDK defaults |
| `opentelemetry-exporter-otlp` | OTLP exporters (gRPC and HTTP/protobuf) |
| `opentelemetry-instrumentation-fastapi` | HTTP server spans and `http.server.*` metrics |

Standard logging is exported as OTLP logs (with trace context) by `OTEL_PYTHON_LOGGING_AUTO_INSTRUMENTATION_ENABLED=true`.
The service adds the `grot.checkout.payments` counter and the `grot.order.id` span attribute.

Only standard `OTEL_*` environment variables configure it. At a minimum set the endpoint and protocol:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
```

## Security

Every endpoint except `GET /healthz` requires `Authorization: Bearer <jwt>` and returns `401`
(`{"title":"Unauthorized",...}`) otherwise. Tokens are minted by `grots-web`, signed with HS256 and validated with
[PyJWT](https://pyjwt.readthedocs.io/): signature, `exp`, `iss` and `aud`, with 30 seconds of clock skew.
See the "Service-to-service security" section of the contract. The service refuses to start unless
`JWT_SIGNING_KEY` is set to at least 32 bytes.

> [!IMPORTANT]
> No key is baked into the image; supply it at run time.

## Configuration

| Variable | Default | Description |
| --- | --- | --- |
| `PORT` | `8083` | Port to listen on |
| `CHECKOUT_FAILURE_RATE` | `0.05` | Fraction of requests that fail with `503` (`0` = never) |
| `TAX_RATE` | `0.08` | The tax added to every payment, as a fraction (`0.08` is 8%), from `0` up to `1`. Each receipt says what it was (`taxRate`). |
| `JWT_SIGNING_KEY` | _(required)_ | Shared HS256 secret, at least 32 bytes |
| `JWT_ISSUER` | `grots-web` | Expected `iss` claim |
| `JWT_AUDIENCE` | `grots-pizzeria` | Expected `aud` claim |
| `OTEL_SERVICE_NAME` | _(set in the Dockerfile)_ `grots-checkout` | Service name |
| `OTEL_SEMCONV_STABILITY_OPT_IN` | _(Dockerfile)_ `http` | Emit the stable HTTP semantic conventions |

## Run locally

Requires Python 3.15 or later.

```sh
# macOS / Linux
python3 -m venv .venv && source .venv/bin/activate
pip install -r requirements-dev.txt

export JWT_SIGNING_KEY='replace-with-at-least-32-bytes-of-secret'
export OTEL_SERVICE_NAME=grots-checkout OTEL_SEMCONV_STABILITY_OPT_IN=http \
       OTEL_PYTHON_LOGGING_AUTO_INSTRUMENTATION_ENABLED=true OTEL_PYTHON_FASTAPI_EXCLUDED_URLS=healthz \
       OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
opentelemetry-instrument uvicorn app.main:app --port 8083
```

```powershell
# Windows (PowerShell)
python -m venv .venv; .\.venv\Scripts\Activate.ps1
pip install -r requirements-dev.txt

$env:JWT_SIGNING_KEY = "replace-with-at-least-32-bytes-of-secret"
$env:OTEL_SERVICE_NAME = "grots-checkout"; $env:OTEL_SEMCONV_STABILITY_OPT_IN = "http"
$env:OTEL_PYTHON_LOGGING_AUTO_INSTRUMENTATION_ENABLED = "true"; $env:OTEL_PYTHON_FASTAPI_EXCLUDED_URLS = "healthz"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "http/protobuf"; $env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318"
opentelemetry-instrument uvicorn app.main:app --port 8083
```

Try it:

```sh
curl -X POST localhost:8083/checkout -H 'content-type: application/json' -H "Authorization: Bearer $TOKEN" \
  -d '{"orderId":"ord_1","amount":19.0,"paymentMethod":"card","cardLast4":"4242"}'
```

To see telemetry on the console instead of exporting it, set `OTEL_TRACES_EXPORTER=console`,
`OTEL_METRICS_EXPORTER=console` and `OTEL_LOGS_EXPORTER=console`.

## Test

```sh
pytest
```

## Docker

```sh
docker build -t grots-checkout services/checkout
docker run --rm -p 8083:8083 -e JWT_SIGNING_KEY="$JWT_SIGNING_KEY" grots-checkout
```
