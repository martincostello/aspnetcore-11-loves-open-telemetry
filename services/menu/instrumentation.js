// OpenTelemetry bootstrap. Loaded before the application with `node --import ./instrumentation.js`.
//
// All configuration comes from the standard OTEL_* environment variables (OTEL_EXPORTER_OTLP_ENDPOINT etc.).
// The two defaults below only make sure the service is named correctly and emits the stable HTTP semantic conventions.
process.env.OTEL_SERVICE_NAME ??= 'grots-menu';
process.env.OTEL_SEMCONV_STABILITY_OPT_IN ??= 'http';

// Express is a CommonJS package that is imported from an ES module, so the ESM loader hook is needed for it to be patched.
const { register } = await import('node:module');
register('@opentelemetry/instrumentation/hook.mjs', import.meta.url);

const { NodeSDK } = await import('@opentelemetry/sdk-node');
const { getNodeAutoInstrumentations } = await import('@opentelemetry/auto-instrumentations-node');
const { BaggageSpanProcessor } = await import('@opentelemetry/baggage-span-processor');
const { OTLPTraceExporter } = await import('@opentelemetry/exporter-trace-otlp-proto');
const { BatchSpanProcessor, ConsoleSpanExporter, SimpleSpanProcessor } = await import('@opentelemetry/sdk-trace');

// Copy the sample's own baggage (grot.*) onto every span so that it can be seen in the trace. Providing span processors
// replaces the SDK's default ones, which the SDK chooses from OTEL_TRACES_EXPORTER, so that choice is made again here, the same way:
// a comma-separated list, otlp if it is empty, and none (on its own) for no export at all. The exporters are otlp (configured from
// OTEL_EXPORTER_OTLP_*, and always http/protobuf, the only protocol this service includes) and console; anything else is ignored.
const spanProcessors = [new BaggageSpanProcessor((key) => key.startsWith('grot.'))];

let exporters = [...new Set((process.env.OTEL_TRACES_EXPORTER ?? '').split(',').map((name) => name.trim().toLowerCase()))].filter(
  (name) => name && name !== 'null',
);

if (exporters.length === 0 || (exporters.length > 1 && exporters.includes('none'))) {
  exporters = ['otlp'];
}

for (const name of exporters) {
  if (name === 'otlp') {
    spanProcessors.push(new BatchSpanProcessor({ exporter: new OTLPTraceExporter() }));
  } else if (name === 'console') {
    spanProcessors.push(new SimpleSpanProcessor({ exporter: new ConsoleSpanExporter() }));
  }
}

const sdk = new NodeSDK({
  spanProcessors,
  instrumentations: [
    getNodeAutoInstrumentations({
      // Express is instrumented by instrumentation-express, which also reports http.route on the
      // server span and metrics. The Express 5 router instrumentation would only add duplicate spans.
      '@opentelemetry/instrumentation-router': { enabled: false },
      // Health checks (made every few seconds by the orchestrator) are not traced.
      '@opentelemetry/instrumentation-http': { ignoreIncomingRequestHook: (request) => request.url === '/healthz' },
      '@opentelemetry/instrumentation-express': { ignoreLayers: ['/healthz'] },
    }),
  ],
});

sdk.start();

for (const signal of ['SIGTERM', 'SIGINT']) {
  process.once(signal, async () => {
    try {
      await sdk.shutdown();
    } finally {
      process.exit(0);
    }
  });
}
