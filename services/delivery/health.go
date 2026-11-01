package main

import (
	"fmt"
	"net/http"
	"os"
	"time"
)

// healthcheck probes the running service's /healthz and returns the process exit code. It exists because the
// container image is distroless: there is no curl or shell for a Docker HEALTHCHECK to use, only this binary
// (run it as `/delivery healthcheck`).
func healthcheck() int {
	port := env("PORT", "8084")

	client := http.Client{Timeout: 3 * time.Second}

	resp, err := client.Get("http://localhost:" + port + "/healthz")
	if err != nil {
		fmt.Fprintln(os.Stderr, "unhealthy:", err)
		return 1
	}
	defer func() { _ = resp.Body.Close() }()

	if resp.StatusCode != http.StatusOK {
		fmt.Fprintln(os.Stderr, "unhealthy:", resp.Status)
		return 1
	}

	return 0
}
