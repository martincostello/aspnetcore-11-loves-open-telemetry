package com.grotspizzeria.orders.otel;

import io.opentelemetry.api.common.AttributeKey;
import io.opentelemetry.sdk.common.CompletableResultCode;
import io.opentelemetry.sdk.common.export.MemoryMode;
import io.opentelemetry.sdk.metrics.Aggregation;
import io.opentelemetry.sdk.metrics.InstrumentType;
import io.opentelemetry.sdk.metrics.data.AggregationTemporality;
import io.opentelemetry.sdk.metrics.data.HistogramData;
import io.opentelemetry.sdk.metrics.data.HistogramPointData;
import io.opentelemetry.sdk.metrics.data.MetricData;
import io.opentelemetry.sdk.metrics.data.MetricDataType;
import io.opentelemetry.sdk.metrics.export.MetricExporter;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Proxy;
import java.util.Collection;
import java.util.List;

/**
 * Passes the metrics on to the exporter it wraps, except the points of the HTTP server request duration for health checks
 * (GET /healthz), which the orchestrator makes every few seconds and which would otherwise be most of what a quiet application
 * reports. The agent records the metrics of a request whatever its sampler decided, and has no setting for leaving a URL out,
 * so they are taken out here, just before they are sent.
 */
final class HealthCheckMetricExporter implements MetricExporter {

    private static final String DURATION_METRIC = "http.server.request.duration";
    private static final AttributeKey<String> HTTP_ROUTE = AttributeKey.stringKey("http.route");
    private static final String HEALTH_CHECK_ROUTE = "/healthz";

    private final MetricExporter delegate;

    HealthCheckMetricExporter(MetricExporter delegate) {
        this.delegate = delegate;
    }

    @Override
    public CompletableResultCode export(Collection<MetricData> metrics) {
        List<MetricData> kept = metrics.stream()
                .map(HealthCheckMetricExporter::withoutHealthChecks)
                .filter(metric -> !metric.isEmpty())
                .toList();

        return kept.isEmpty() ? CompletableResultCode.ofSuccess() : delegate.export(kept);
    }

    private static MetricData withoutHealthChecks(MetricData metric) {
        if (!DURATION_METRIC.equals(metric.getName()) || metric.getType() != MetricDataType.HISTOGRAM) {
            return metric;
        }

        HistogramData histogram = metric.getHistogramData();
        List<HistogramPointData> points = histogram.getPoints().stream()
                .filter(point -> !HEALTH_CHECK_ROUTE.equals(point.getAttributes().get(HTTP_ROUTE)))
                .toList();

        if (points.size() == histogram.getPoints().size()) {
            return metric;
        }

        HistogramData filtered = new HistogramData() {
            @Override
            public AggregationTemporality getAggregationTemporality() {
                return histogram.getAggregationTemporality();
            }

            @Override
            public Collection<HistogramPointData> getPoints() {
                return points;
            }
        };

        // The same metric, with the points that are left. (A proxy, because MetricData has more to it than is worth copying.)
        return (MetricData) Proxy.newProxyInstance(
                MetricData.class.getClassLoader(),
                new Class<?>[] {MetricData.class},
                (proxy, method, args) -> {
                    switch (method.getName()) {
                        case "getData", "getHistogramData":
                            return filtered;
                        case "isEmpty":
                            return points.isEmpty();
                        default:
                            try {
                                return method.invoke(metric, args);
                            } catch (InvocationTargetException e) {
                                throw e.getCause();
                            }
                    }
                });
    }

    @Override
    public CompletableResultCode flush() {
        return delegate.flush();
    }

    @Override
    public CompletableResultCode shutdown() {
        return delegate.shutdown();
    }

    @Override
    public AggregationTemporality getAggregationTemporality(InstrumentType instrumentType) {
        return delegate.getAggregationTemporality(instrumentType);
    }

    @Override
    public Aggregation getDefaultAggregation(InstrumentType instrumentType) {
        return delegate.getDefaultAggregation(instrumentType);
    }

    @Override
    public MemoryMode getMemoryMode() {
        return delegate.getMemoryMode();
    }
}
