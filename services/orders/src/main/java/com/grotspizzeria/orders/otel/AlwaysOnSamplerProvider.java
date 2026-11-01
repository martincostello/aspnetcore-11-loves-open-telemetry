package com.grotspizzeria.orders.otel;

import io.opentelemetry.sdk.autoconfigure.spi.ConfigProperties;
import io.opentelemetry.sdk.autoconfigure.spi.traces.ConfigurableSamplerProvider;
import io.opentelemetry.sdk.trace.samplers.Sampler;

/**
 * Records every trace (for debugging) except health checks. Select it with OTEL_TRACES_SAMPLER=grots_always_on.
 */
public final class AlwaysOnSamplerProvider implements ConfigurableSamplerProvider {

    @Override
    public Sampler createSampler(ConfigProperties config) {
        return new HealthCheckSampler(Sampler.alwaysOn());
    }

    @Override
    public String getName() {
        return "grots_always_on";
    }
}
