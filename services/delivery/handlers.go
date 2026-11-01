package main

import (
	"encoding/json"
	"errors"
	"log/slog"
	"net/http"
	"strings"

	"go.opentelemetry.io/otel/attribute"
	semconv "go.opentelemetry.io/otel/semconv/v1.43.0"
	"go.opentelemetry.io/otel/trace"
)

const maxBodyBytes = 1 << 20

type createRequest struct {
	OrderID   string `json:"orderId"`
	Address   string `json:"address"`
	ItemCount int    `json:"itemCount"`
}

type problem struct {
	Title  string `json:"title"`
	Status int    `json:"status"`
	Detail string `json:"detail"`
}

// withRouteAttribute sets http.route on the server span once the ServeMux has matched the request.
//
// otelhttp starts the span before routing happens so it only learns the route for metrics and the span name;
// ServeMux records the matched pattern on the request, so it is read back after the request has been served.
func withRouteAttribute(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		next.ServeHTTP(w, r)
		if _, route, found := strings.Cut(r.Pattern, " "); found && route != "" {
			trace.SpanFromContext(r.Context()).SetAttributes(semconv.HTTPRoute(route))
		}
	})
}

// newMux registers the HTTP API. Route patterns (including the method) are what otelhttp uses for http.route.
func newMux(store *Store, auth *Authenticator) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
	})
	mux.HandleFunc("POST /deliveries", auth.Require(createDelivery(store)))
	mux.HandleFunc("GET /deliveries/{orderId}", auth.Require(getDelivery(store)))
	return withRouteAttribute(mux)
}

func createDelivery(store *Store) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		ctx := r.Context()

		var req createRequest
		r.Body = http.MaxBytesReader(w, r.Body, maxBodyBytes)
		if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
			writeProblem(w, http.StatusBadRequest, "Invalid request", "The request body must be valid JSON.")
			return
		}

		req.OrderID = strings.TrimSpace(req.OrderID)
		req.Address = strings.TrimSpace(req.Address)
		switch {
		case req.OrderID == "" || len(req.OrderID) > 64:
			writeProblem(w, http.StatusBadRequest, "Invalid request", "orderId is required and must be at most 64 characters.")
			return
		case req.Address == "" || len(req.Address) > 256:
			writeProblem(w, http.StatusBadRequest, "Invalid request", "address is required and must be at most 256 characters.")
			return
		case req.ItemCount < 1:
			writeProblem(w, http.StatusBadRequest, "Invalid request", "itemCount must be at least 1.")
			return
		}

		setOrderID(r, req.OrderID)

		tracking, created := store.Create(req.OrderID, req.Address, req.ItemCount)
		if !created {
			slog.InfoContext(ctx, "Delivery already exists", "grot.order.id", req.OrderID)
			writeJSON(w, http.StatusOK, tracking)
			return
		}

		slog.InfoContext(ctx, "Delivery created", "grot.order.id", req.OrderID, "grot.delivery.driver", tracking.Driver, "grot.delivery.item_count", req.ItemCount)
		writeJSON(w, http.StatusCreated, tracking)
	}
}

func getDelivery(store *Store) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		orderID := r.PathValue("orderId")
		setOrderID(r, orderID)

		tracking, ok := store.Get(orderID)
		if !ok {
			slog.WarnContext(r.Context(), "Delivery not found", "grot.order.id", orderID)
			writeProblem(w, http.StatusNotFound, "Delivery not found", "No delivery exists for order "+orderID+".")
			return
		}

		writeJSON(w, http.StatusOK, tracking)
	}
}

// setOrderID tags the server span created by otelhttp with the order the request is about.
func setOrderID(r *http.Request, orderID string) {
	trace.SpanFromContext(r.Context()).SetAttributes(attribute.String("grot.order.id", orderID))
}

func writeProblem(w http.ResponseWriter, status int, title, detail string) {
	writeJSON(w, status, problem{Title: title, Status: status, Detail: detail})
}

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	if err := json.NewEncoder(w).Encode(body); err != nil && !errors.Is(err, http.ErrHandlerTimeout) {
		slog.Error("Failed to write response", "exception.message", err.Error())
	}
}
