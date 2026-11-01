package com.grotspizzeria.orders.otel;

import io.opentelemetry.sdk.autoconfigure.spi.ConfigProperties;
import io.opentelemetry.sdk.autoconfigure.spi.traces.ConfigurableSamplerProvider;
import io.opentelemetry.sdk.trace.samplers.Sampler;

/**
 * The SDK's default sampler (parentbased_always_on) that never records health checks.
 * Select it with OTEL_TRACES_SAMPLER=grots_parentbased_always_on.
 */
public final class ParentBasedAlwaysOnSamplerProvider implements ConfigurableSamplerProvider {

    @Override
    public Sampler createSampler(ConfigProperties config) {
        return Sampler.parentBased(new HealthCheckSampler(Sampler.alwaysOn()));
    }

    @Override
    public String getName() {
        return "grots_parentbased_always_on";
    }
}
