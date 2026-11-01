// Command delivery is the "grots-delivery" service of Grot's Pizzeria: it tracks the delivery of pizzas.
package main

import (
	"context"
	"errors"
	"log/slog"
	"net"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"syscall"
	"time"

	"go.opentelemetry.io/contrib/bridges/otelslog"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/metric"
)

func main() {
	// `delivery healthcheck` is used by the container's HEALTHCHECK (see health.go).
	if len(os.Args) > 1 && os.Args[1] == "healthcheck" {
		os.Exit(healthcheck())
	}

	if err := run(); err != nil {
		slog.Error("Service failed", "exception.message", err.Error())
		os.Exit(1)
	}
}

func run() error {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	// Fail fast, before anything else starts, if the shared signing key is not configured.
	auth, err := NewAuthenticatorFromEnv()
	if err != nil {
		return err
	}

	shutdownTelemetry, err := setupTelemetry(ctx)
	if err != nil {
		return err
	}

	// Log to stdout and to OpenTelemetry, so that logs carry the trace context of the request they were written in.
	slog.SetDefault(slog.New(slog.NewMultiHandler(
		slog.NewTextHandler(os.Stdout, nil),
		otelslog.NewHandler(serviceName),
	)))

	store := NewStore(stepDuration())
	if err := registerMetrics(store); err != nil {
		return err
	}

	// otelhttp creates the server span and records the http.server.* metrics. Because it wraps the whole
	// ServeMux it picks up the matched route pattern (e.g. "GET /deliveries/{orderId}") for http.route.
	handler := otelhttp.NewHandler(newMux(store, auth), "delivery",
		otelhttp.WithFilter(func(r *http.Request) bool { return r.URL.Path != "/healthz" }),
	)

	server := &http.Server{
		Addr:              net.JoinHostPort("", env("PORT", "8084")),
		Handler:           handler,
		ReadHeaderTimeout: 5 * time.Second,
	}

	serverErr := make(chan error, 1)
	go func() {
		slog.Info("Listening on " + server.Addr)
		serverErr <- server.ListenAndServe()
	}()

	select {
	case err = <-serverErr:
		// The server failed to start (e.g. the port is in use).
	case <-ctx.Done():
		slog.Info("Shutting down")
		shutdownCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		err = server.Shutdown(shutdownCtx)
	}

	if errors.Is(err, http.ErrServerClosed) {
		err = nil
	}

	// Flush any pending telemetry last.
	flushCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	return errors.Join(err, shutdownTelemetry(flushCtx))
}

// registerMetrics registers the custom business metrics of the service.
func registerMetrics(store *Store) error {
	meter := otel.Meter(serviceName)

	// The number of active deliveries is derived from the store whenever metrics are collected,
	// so it can never drift from the real state however the deliveries progress.
	_, err := meter.Int64ObservableUpDownCounter("grot.deliveries.active",
		metric.WithDescription("The number of deliveries that have not been delivered yet."),
		metric.WithUnit("{delivery}"),
		metric.WithInt64Callback(func(_ context.Context, o metric.Int64Observer) error {
			o.Observe(store.Active())
			return nil
		}),
	)
	return err
}

// stepDuration is how long a delivery spends in each step, configured by DELIVERY_STEP_SECONDS.
func stepDuration() time.Duration {
	seconds, err := strconv.ParseFloat(env("DELIVERY_STEP_SECONDS", "15"), 64)
	if err != nil || seconds < 0 {
		slog.Warn("Ignoring invalid DELIVERY_STEP_SECONDS")
		seconds = 15
	}
	return time.Duration(seconds * float64(time.Second))
}

func env(key, fallback string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return fallback
}
