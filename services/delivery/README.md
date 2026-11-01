# grots-delivery (Go)

Delivery tracking for Grot's Pizzeria. Orders call `POST /deliveries` once they are paid for, and the UI polls
`GET /deliveries/{orderId}` to follow the pizza from the oven to the door. See the
[service contract](../../docs/service-contract.md) for the API.

Deliveries are held in memory. Progress is derived from the time elapsed since the delivery was created
(`preparing` → `baking` → `quality-control` → `out-for-delivery` → `delivered`), so there is no background work.

## Code

| File | Purpose |
| --- | --- |
| `main.go` | Wiring: logging, HTTP server, graceful shutdown, custom metric |
| `telemetry.go` | OpenTelemetry SDK setup for traces, metrics and logs |
| `auth.go` | JWT bearer token validation middleware (`golang-jwt/jwt/v5`) |
| `handlers.go` | HTTP handlers, `ServeMux` routes (Go 1.22+ patterns) |
| `store.go` | Mutex-guarded in-memory store and the delivery timeline |

## OpenTelemetry

* The SDK is configured only through the standard `OTEL_*` environment variables
  (`go.opentelemetry.io/contrib/exporters/autoexport`). To send telemetry somewhere set, for example:
  `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318` (the default protocol is `http/protobuf`).
* `otelhttp` provides the HTTP server span and `http.server.*` metrics, `runtime` provides the Go runtime metrics
  and `otelslog` bridges `log/slog` to OpenTelemetry logs (they are also written to stdout).
* `grot.deliveries.active` (observable up/down counter) is the custom metric, and `grot.order.id` is set on server spans.
* To see telemetry without a backend: `OTEL_TRACES_EXPORTER=console OTEL_METRICS_EXPORTER=console OTEL_LOGS_EXPORTER=console`.

## Security

Every endpoint except `GET /healthz` requires a JWT bearer token (HS256, validated for signature, `exp`, `iss` and
`aud` with 30s leeway); anything else gets a `401`. The middleware runs inside the `otelhttp` handler, so rejected
requests are still traced. The service **refuses to start** unless `JWT_SIGNING_KEY` (at least 32 bytes) is set.
See the [service contract](../../docs/service-contract.md#service-to-service-security-jwt).

## Run locally

Requires Go 1.27 or later. The same commands work on Windows, macOS and Linux.

```sh
cd services/delivery
export JWT_SIGNING_KEY="a-development-only-key-of-32-bytes-or-more"
go run .
```

```powershell
# PowerShell
$env:JWT_SIGNING_KEY = "a-development-only-key-of-32-bytes-or-more"
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318"
go run .
```

```sh
# Requests need a bearer token signed with the same key (grots-web mints them in the full sample)
curl -H "Authorization: Bearer $TOKEN" -X POST localhost:8084/deliveries -d '{"orderId":"ord_1","address":"1 Loki Lane","itemCount":3}'
curl -H "Authorization: Bearer $TOKEN" localhost:8084/deliveries/ord_1
```

## Configuration

| Variable | Default | Description |
| --- | --- | --- |
| `PORT` | `8084` | Port to listen on (all interfaces) |
| `DELIVERY_STEP_SECONDS` | `15` | Seconds a delivery spends in each step |
| `JWT_SIGNING_KEY` | _(required)_ | Shared HS256 signing key, at least 32 bytes |
| `JWT_ISSUER` | `grots-web` | Expected `iss` claim |
| `JWT_AUDIENCE` | `grots-pizzeria` | Expected `aud` claim |
| `OTEL_SERVICE_NAME` | `grots-delivery` | Service name (defaulted in code and in the image) |
| `OTEL_*` | | Any standard OpenTelemetry SDK environment variable |

## Docker

```sh
docker build -t grots-delivery services/delivery
docker run --rm -p 8084:8084 -e JWT_SIGNING_KEY="$JWT_SIGNING_KEY" grots-delivery
```
