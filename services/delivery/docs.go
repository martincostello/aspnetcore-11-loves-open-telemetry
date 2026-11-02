package main

import (
	"embed"
	"io/fs"
	"log/slog"
	"net/http"
	"strings"
)

// static holds the API documentation page (Swagger UI, from a CDN) and its scripts and styles: see static/index.html.
//
//go:embed static
var static embed.FS

// registerDocs serves the API documentation at the root of the service, so that it is not a 404 in a browser, along with the
// OpenAPI document it shows. None of them need a token: the page is where someone finds out how to get one.
func registerDocs(mux *http.ServeMux) {
	files, err := fs.Sub(static, "static")
	if err != nil {
		panic(err) // the directory is embedded at build time, so it is always there
	}

	mux.HandleFunc("GET /{$}", func(w http.ResponseWriter, r *http.Request) {
		http.ServeFileFS(w, r, files, "index.html")
	})

	assets := http.FileServerFS(files)
	mux.HandleFunc("GET /swagger-ui/", func(w http.ResponseWriter, r *http.Request) {
		if strings.HasSuffix(r.URL.Path, "/") { // not a listing of the folder
			http.NotFound(w, r)
			return
		}
		assets.ServeHTTP(w, r)
	})

	mux.HandleFunc("GET /openapi.json", func(w http.ResponseWriter, _ *http.Request) {
		document, err := openAPIDocument()
		if err != nil {
			slog.Error("Failed to build the OpenAPI document", "exception.message", err.Error())
			writeProblem(w, http.StatusInternalServerError, "Internal Server Error", "The OpenAPI document could not be built.")
			return
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write(document)
	})
}
