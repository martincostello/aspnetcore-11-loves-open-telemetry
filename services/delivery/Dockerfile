# Build context: services/delivery
FROM golang:1.27-alpine AS build
WORKDIR /src

COPY go.mod go.sum ./
RUN go mod download

COPY *.go ./
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /out/delivery .

FROM gcr.io/distroless/static-debian12:nonroot
COPY --from=build /out/delivery /delivery

ENV PORT=8084 \
    OTEL_SERVICE_NAME=grots-delivery

EXPOSE 8084
USER nonroot:nonroot
ENTRYPOINT ["/delivery"]
