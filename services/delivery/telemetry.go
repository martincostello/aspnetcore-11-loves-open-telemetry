package main

import (
	"context"
	"errors"
	"os"
	"strings"

	"go.opentelemetry.io/contrib/exporters/autoexport"
	"go.opentelemetry.io/contrib/instrumentation/runtime"
	"go.opentelemetry.io/contrib/processors/baggagecopy"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/baggage"
	"go.opentelemetry.io/otel/propagation"
	sdklog "go.opentelemetry.io/otel/sdk/log"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	semconv "go.opentelemetry.io/otel/semconv/v1.43.0"
)

const serviceName = "grots-delivery"

// setupTelemetry configures the OpenTelemetry SDK for traces, metrics and logs.
//
// The exporters (and their endpoints, protocols, headers and so on) are selected by
// the standard OTEL_* environment variables via the autoexport package. For example, setting
// OTEL_EXPORTER_OTLP_ENDPOINT is all that is needed to send everything to an OTLP receiver.
//
// The returned function flushes and shuts down all of the providers.
func setupTelemetry(ctx context.Context) (shutdown func(context.Context) error, err error) {
	var shutdowns []func(context.Context) error
	shutdown = func(ctx context.Context) error {
		var errs error
		for i := len(shutdowns) - 1; i >= 0; i-- {
			errs = errors.Join(errs, shutdowns[i](ctx))
		}
		return errs
	}
	fail := func(e error) (func(context.Context) error, error) {
		return nil, errors.Join(e, shutdown(ctx))
	}

	// Honour OTEL_SERVICE_NAME and OTEL_RESOURCE_ATTRIBUTES, but fall back to a sensible service name.
	attrs := []resource.Option{resource.WithFromEnv()}
	if os.Getenv("OTEL_SERVICE_NAME") == "" {
		attrs = append(attrs, resource.WithAttributes(semconv.ServiceName(serviceName)))
	}
	res, err := resource.New(ctx, append(attrs,
		resource.WithTelemetrySDK(),
		resource.WithHost(),
		resource.WithProcess(),
		resource.WithContainer(),
	)...)
	if err != nil {
		return fail(err)
	}

	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(
		propagation.TraceContext{},
		propagation.Baggage{},
	))

	// Traces
	spanExporter, err := autoexport.NewSpanExporter(ctx)
	if err != nil {
		return fail(err)
	}
	tp := sdktrace.NewTracerProvider(
		// Copy the sample's own baggage (grot.*) onto every span so that it can be seen in the trace
		sdktrace.WithSpanProcessor(baggagecopy.NewSpanProcessor(func(m baggage.Member) bool {
			return strings.HasPrefix(m.Key(), "grot.")
		})),
		sdktrace.WithBatcher(spanExporter),
		sdktrace.WithResource(res),
	)
	shutdowns = append(shutdowns, tp.Shutdown)
	otel.SetTracerProvider(tp)

	// Metrics
	metricReader, err := autoexport.NewMetricReader(ctx)
	if err != nil {
		return fail(err)
	}
	mp := sdkmetric.NewMeterProvider(
		sdkmetric.WithReader(metricReader),
		sdkmetric.WithResource(res),
	)
	shutdowns = append(shutdowns, mp.Shutdown)
	otel.SetMeterProvider(mp)

	// Logs
	logExporter, err := autoexport.NewLogExporter(ctx)
	if err != nil {
		return fail(err)
	}
	lp := sdklog.NewLoggerProvider(
		sdklog.WithProcessor(sdklog.NewBatchProcessor(logExporter)),
		sdklog.WithResource(res),
	)
	shutdowns = append(shutdowns, lp.Shutdown)
	otel.SetLoggerProvider(lp)

	// Go runtime metrics (go.* semantic conventions).
	if err := runtime.Start(); err != nil {
		return fail(err)
	}

	return shutdown, nil
}
