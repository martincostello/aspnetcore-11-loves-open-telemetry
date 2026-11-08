package com.grotspizzeria.orders.otel;

import io.opentelemetry.sdk.autoconfigure.spi.AutoConfigurationCustomizer;
import io.opentelemetry.sdk.autoconfigure.spi.AutoConfigurationCustomizerProvider;

/**
 * Leaves health checks out of the HTTP server metrics, which the agent has no setting for: see {@link HealthCheckSampler},
 * which does the same for the spans.
 */
public final class HealthCheckMetricsCustomizer implements AutoConfigurationCustomizerProvider {

    @Override
    public void customize(AutoConfigurationCustomizer customizer) {
        customizer.addMetricExporterCustomizer((exporter, config) -> new HealthCheckMetricExporter(exporter));
    }
}
