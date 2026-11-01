# grots-menu (Node.js)

The catalogue of pizzas, sides and desserts and price-quote service for Grot's Pizzeria. It is a small [Express 5](https://expressjs.com/)
app (ES modules, Node.js 26+) with no database: the menu lives in `src/menu.js`.

| Endpoint | Description |
| --- | --- |
| `GET /healthz` | Anonymous health check |
| `GET /menu` | The full menu, with the items currently on special offer (15% off, a new pizza, side and dessert every 5 minutes) |
| `GET /menu/{id}` | A single pizza (`404` if unknown) |
| `POST /menu/price` | Prices a list of `{ pizzaId, quantity }` |

See [`docs/service-contract.md`](../../docs/service-contract.md) for the full API. Every endpoint except
`/healthz` requires an HS256 JWT bearer token (validated with [`jose`](https://github.com/panva/jose): signature,
`exp`, `iss`, `aud`) and returns `401` otherwise.

## Run it

```sh
npm ci
JWT_SIGNING_KEY="a-development-key-of-at-least-32-bytes" npm start
```

PowerShell: `$env:JWT_SIGNING_KEY = "a-development-key-of-at-least-32-bytes"; npm start`

`npm run dev` restarts on file changes, `npm test` runs the (dependency-free) `node:test` tests.

### Environment variables

| Variable | Default | Notes |
| --- | --- | --- |
| `PORT` | `8081` | |
| `JWT_SIGNING_KEY` | _(required)_ | At least 32 bytes. The service exits at startup without it. Never baked into the image. |
| `JWT_ISSUER` | `grots-web` | |
| `JWT_AUDIENCE` | `grots-pizzeria` | |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | _(SDK default)_ | e.g. `http://localhost:4318`. Standard `OTEL_*` variables apply. |
| `OTEL_SERVICE_NAME` | `grots-menu` | |
| `SPECIAL_OFFERS` | `on` | `off` switches the special offers off, so prices never change (used by the end-to-end tests). |

## Telemetry

`instrumentation.js` is loaded with `node --import` before the app. It starts the OpenTelemetry `NodeSDK` with
`@opentelemetry/auto-instrumentations-node`, giving HTTP server/client and Express instrumentation plus
Node.js runtime metrics; traces, metrics and logs are exported over OTLP using only the standard `OTEL_*`
environment variables. It defaults `OTEL_SERVICE_NAME` to `grots-menu` and `OTEL_SEMCONV_STABILITY_OPT_IN` to `http`
(stable HTTP semantic conventions).

* `src/logger.js` writes to stdout and emits OpenTelemetry log records (with trace context) via the logs API.
* `grot.menu.price_quotes` is a custom counter incremented for every price quote.
* Express is imported from an ES module, so `instrumentation.js` registers the OpenTelemetry ESM loader hook;
  without it `http.route` would be missing.
* Authentication is applied per route (not globally) so that rejected requests still report the right `http.route`.

## Docker

```sh
docker build -t grots-menu .
docker run --rm -p 8081:8081 -e JWT_SIGNING_KEY="a-development-key-of-at-least-32-bytes" grots-menu
```
