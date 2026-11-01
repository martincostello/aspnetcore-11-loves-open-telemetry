package main

import (
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"strings"
	"time"

	"github.com/golang-jwt/jwt/v5"
)

const minSigningKeyBytes = 32

// Authenticator validates the JWT bearer tokens that grots-web (and grots-orders on its behalf) sends with every call.
type Authenticator struct {
	key    []byte
	parser *jwt.Parser
}

// NewAuthenticatorFromEnv reads JWT_SIGNING_KEY, JWT_ISSUER and JWT_AUDIENCE. It fails if the key is missing or too short.
func NewAuthenticatorFromEnv() (*Authenticator, error) {
	key := os.Getenv("JWT_SIGNING_KEY")
	if len(key) < minSigningKeyBytes {
		return nil, fmt.Errorf("JWT_SIGNING_KEY must be set to at least %d bytes", minSigningKeyBytes)
	}
	return NewAuthenticator([]byte(key), env("JWT_ISSUER", "grots-web"), env("JWT_AUDIENCE", "grots-pizzeria")), nil
}

func NewAuthenticator(key []byte, issuer, audience string) *Authenticator {
	return &Authenticator{
		key: key,
		parser: jwt.NewParser(
			jwt.WithValidMethods([]string{jwt.SigningMethodHS256.Alg()}),
			jwt.WithIssuer(issuer),
			jwt.WithAudience(audience),
			jwt.WithExpirationRequired(),
			jwt.WithLeeway(30*time.Second),
		),
	}
}

// Require rejects requests without a valid bearer token with a 401. Applied per route, inside the
// otelhttp handler, so that rejected requests are still traced and counted in the HTTP metrics.
func (a *Authenticator) Require(next http.HandlerFunc) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		token, ok := strings.CutPrefix(r.Header.Get("Authorization"), "Bearer ")
		if !ok || strings.TrimSpace(token) == "" {
			a.reject(w, r, errors.New("missing bearer token"))
			return
		}
		if _, err := a.parser.Parse(strings.TrimSpace(token), func(*jwt.Token) (any, error) { return a.key, nil }); err != nil {
			a.reject(w, r, err)
			return
		}
		next(w, r)
	}
}

func (a *Authenticator) reject(w http.ResponseWriter, r *http.Request, err error) {
	slog.WarnContext(r.Context(), "Rejected unauthenticated request", "grot.auth.failure", err.Error())
	w.Header().Set("WWW-Authenticate", `Bearer`)
	writeProblem(w, http.StatusUnauthorized, "Unauthorized", "A valid bearer token is required.")
}
