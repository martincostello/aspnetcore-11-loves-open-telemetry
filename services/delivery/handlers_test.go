package main

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func create(t *testing.T, address string) int {
	t.Helper()

	body := `{"orderId":"ord_1","address":"` + address + `","itemCount":2}`
	req := httptest.NewRequest(http.MethodPost, "/deliveries", strings.NewReader(body))
	rec := httptest.NewRecorder()

	createDelivery(NewStore(time.Minute)).ServeHTTP(rec, req)

	return rec.Code
}

func TestAddressLengthIsInCharactersNotBytes(t *testing.T) {
	cases := map[string]struct {
		address string
		want    int
	}{
		"ascii":                      {"1 Loki Lane, Tempo Town", http.StatusCreated},
		"the longest the BFF allows": {strings.Repeat("東", 200), http.StatusCreated},
		"the most this accepts":      {strings.Repeat("東", maxAddressLength), http.StatusCreated},
		"one character too many":     {strings.Repeat("東", maxAddressLength+1), http.StatusBadRequest},
		"blank":                      {"   ", http.StatusBadRequest},
	}

	for name, tc := range cases {
		t.Run(name, func(t *testing.T) {
			if got := create(t, tc.address); got != tc.want {
				t.Errorf("status = %d, want %d", got, tc.want)
			}
		})
	}
}
