package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

// docsMux is the real routes, with authentication switched on: none of the documentation may need a token.
func docsMux() http.Handler {
	auth := NewAuthenticator([]byte("unit-test-signing-key-0123456789-abcdef"), "grots-web", "grots-pizzeria")
	return newMux(NewStore(time.Minute), auth)
}

func get(t *testing.T, path string) *httptest.ResponseRecorder {
	t.Helper()

	recorder := httptest.NewRecorder()
	docsMux().ServeHTTP(recorder, httptest.NewRequest(http.MethodGet, path, nil))

	return recorder
}

func TestDocumentationPageIsTheRootAndNeedsNoToken(t *testing.T) {
	for _, path := range []string{"/", "/swagger-ui/theme.css", "/swagger-ui/init.js", "/swagger-ui/theme.js", "/swagger-ui/grot.svg"} {
		if got := get(t, path).Code; got != http.StatusOK {
			t.Errorf("GET %s = %d, want 200", path, got)
		}
	}

	page := get(t, "/")
	if contentType := page.Header().Get("Content-Type"); !strings.HasPrefix(contentType, "text/html") {
		t.Errorf("Content-Type = %q, want HTML", contentType)
	}
	for _, want := range []string{"<title>grots-delivery API", `data-spec-url="/openapi.json"`} {
		if !strings.Contains(page.Body.String(), want) {
			t.Errorf("the page does not contain %q", want)
		}
	}
}

func TestDocumentationAssetsAreNotListed(t *testing.T) {
	if got := get(t, "/swagger-ui/").Code; got != http.StatusNotFound {
		t.Errorf("GET /swagger-ui/ = %d, want 404", got)
	}
	if got := get(t, "/swagger-ui/missing.css").Code; got != http.StatusNotFound {
		t.Errorf("GET /swagger-ui/missing.css = %d, want 404", got)
	}
}

func TestOpenAPIDocumentNeedsNoToken(t *testing.T) {
	response := get(t, "/openapi.json")
	if response.Code != http.StatusOK {
		t.Fatalf("GET /openapi.json = %d, want 200", response.Code)
	}

	var document struct {
		OpenAPI string `json:"openapi"`
		Info    struct {
			Title string `json:"title"`
		} `json:"info"`
		Paths      map[string]map[string]struct{ Security *[]any } `json:"paths"`
		Components struct {
			Schemas         map[string]json.RawMessage `json:"schemas"`
			SecuritySchemes map[string]struct {
				Scheme string `json:"scheme"`
			} `json:"securitySchemes"`
		} `json:"components"`
	}
	if err := json.Unmarshal(response.Body.Bytes(), &document); err != nil {
		t.Fatalf("the OpenAPI document is not JSON: %v", err)
	}

	if !strings.HasPrefix(document.OpenAPI, "3.") || document.Info.Title != "grots-delivery" {
		t.Errorf("openapi = %q, title = %q", document.OpenAPI, document.Info.Title)
	}
	for _, path := range []string{"/healthz", "/deliveries", "/deliveries/{orderId}"} {
		if _, ok := document.Paths[path]; !ok {
			t.Errorf("the document has no %s", path)
		}
	}
	for _, schema := range []string{"CreateDeliveryRequest", "Tracking", "TrackingStep", "Problem"} {
		if _, ok := document.Components.Schemas[schema]; !ok {
			t.Errorf("the document has no schema %s", schema)
		}
	}
	if document.Components.SecuritySchemes["bearerAuth"].Scheme != "bearer" {
		t.Error("the document does not describe the bearer token")
	}
	if security := document.Paths["/healthz"]["get"].Security; security == nil || len(*security) != 0 {
		t.Error("the health check should say that it needs no token")
	}
}

func TestOperationsStillNeedAToken(t *testing.T) {
	for _, path := range []string{"/deliveries/ord_1"} {
		if got := get(t, path).Code; got != http.StatusUnauthorized {
			t.Errorf("GET %s = %d, want 401", path, got)
		}
	}
}
