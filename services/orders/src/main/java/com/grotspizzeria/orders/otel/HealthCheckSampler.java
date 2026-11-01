package com.grotspizzeria.orders.otel;

import io.opentelemetry.api.common.Attributes;
import io.opentelemetry.api.common.AttributeKey;
import io.opentelemetry.api.trace.SpanKind;
import io.opentelemetry.context.Context;
import io.opentelemetry.sdk.trace.data.LinkData;
import io.opentelemetry.sdk.trace.samplers.Sampler;
import io.opentelemetry.sdk.trace.samplers.SamplingResult;
import java.util.List;

/**
 * Drops the server span of health check requests (GET /healthz), which the orchestrator makes every few seconds, and defers
 * to another sampler for everything else. The Java agent has no setting for excluding URLs, so this runs as an agent
 * extension (see the providers next to it).
 */
final class HealthCheckSampler implements Sampler {

    private static final AttributeKey<String> URL_PATH = AttributeKey.stringKey("url.path");
    private static final String HEALTH_CHECK_PATH = "/healthz";

    private final Sampler delegate;

    HealthCheckSampler(Sampler delegate) {
        this.delegate = delegate;
    }

    @Override
    public SamplingResult shouldSample(Context parentContext, String traceId, String name, SpanKind spanKind,
            Attributes attributes, List<LinkData> parentLinks) {
        if (spanKind == SpanKind.SERVER && HEALTH_CHECK_PATH.equals(attributes.get(URL_PATH))) {
            return SamplingResult.drop();
        }
        return delegate.shouldSample(parentContext, traceId, name, spanKind, attributes, parentLinks);
    }

    @Override
    public String getDescription() {
        return "HealthCheckSampler{" + delegate.getDescription() + "}";
    }
}
