package main

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"strconv"
	"sync"

	"github.com/getkin/kin-openapi/openapi3"
	"github.com/getkin/kin-openapi/openapi3gen"
)

// openAPIDocument is the OpenAPI document of this service, as JSON. The schemas are generated from the Go types the service
// uses on the wire (so they cannot drift from them); the operations are described here, next to the routes in newMux.
var openAPIDocument = sync.OnceValues(buildOpenAPI)

func buildOpenAPI() ([]byte, error) {
	schemas := openapi3.Schemas{}

	for _, value := range []any{&createRequest{}, &Tracking{}, &problem{}} {
		// A generator remembers the types it has seen, and so would leave the nested ones (the steps) out of the components
		// of every document after the first, so each value gets its own.
		gen := openapi3gen.NewGenerator(
			openapi3gen.UseAllExportedFields(),
			openapi3gen.CreateComponentSchemas(openapi3gen.ExportComponentSchemasOptions{ExportComponentSchemas: true, ExportTopLevelSchema: true}),
		)
		if _, err := gen.NewSchemaRefForValue(value, schemas); err != nil {
			return nil, fmt.Errorf("generating the schema of %T: %w", value, err)
		}
	}

	name(schemas)
	describe(schemas)

	health := openapi3.NewObjectSchema().WithProperty("status", openapi3.NewStringSchema().WithDefault("ok"))
	schemas["Health"] = health.NewRef()

	ref := func(name string) *openapi3.SchemaRef { return openapi3.NewSchemaRef("#/components/schemas/"+name, nil) }
	problemResponse := func(description string) *openapi3.ResponseRef {
		return &openapi3.ResponseRef{Value: openapi3.NewResponse().WithDescription(description).WithJSONSchemaRef(ref("Problem"))}
	}
	unauthorized := problemResponse("There is no valid bearer token.")

	doc := &openapi3.T{
		OpenAPI: "3.0.3",
		Info: &openapi3.Info{
			Title:   "grots-delivery",
			Version: "1.0.0",
			Description: "Tracks the delivery of the pizzas of Grot's Pizzeria: it assigns a driver when an order is ready and says how far the " +
				"delivery has got, one step after another (`preparing`, `baking`, `quality-control`, `out-for-delivery`, `delivered`). " +
				"The deliveries are kept in memory.\n\n" +
				"Every endpoint except `/healthz` needs a JWT bearer token, signed with the key shared by the services (HS256, with the " +
				"issuer `grots-web` and the audience `grots-pizzeria` unless configured otherwise). `grots-orders` passes on the one it was given.",
		},
		Tags: openapi3.Tags{
			{Name: "Deliveries", Description: "Start tracking a delivery, and see how it is going."},
			{Name: "Health", Description: "Whether the service is running."},
		},
		Security: openapi3.SecurityRequirements{{"bearerAuth": []string{}}},
		Components: &openapi3.Components{
			Schemas: schemas,
			SecuritySchemes: openapi3.SecuritySchemes{
				"bearerAuth": &openapi3.SecuritySchemeRef{Value: openapi3.NewJWTSecurityScheme().WithDescription(
					"A JWT signed (HS256) with the shared `JWT_SIGNING_KEY`, with the expected `iss` and `aud` claims."),
				},
			},
		},
		Paths: openapi3.NewPaths(),
	}

	doc.Paths.Set("/healthz", &openapi3.PathItem{
		Get: &openapi3.Operation{
			Tags:        []string{"Health"},
			Summary:     "Check that the service is running",
			Security:    &openapi3.SecurityRequirements{},
			OperationID: "health",
			Responses: openapi3.NewResponses(openapi3.WithStatus(http.StatusOK, &openapi3.ResponseRef{
				Value: openapi3.NewResponse().WithDescription("The service is running.").WithJSONSchema(health),
			})),
		},
	})

	doc.Paths.Set("/deliveries", &openapi3.PathItem{
		Post: &openapi3.Operation{
			Tags:        []string{"Deliveries"},
			Summary:     "Start a delivery",
			Description: "Starts tracking the delivery of an order. It is safe to ask twice: the delivery that already exists is returned.",
			OperationID: "createDelivery",
			RequestBody: &openapi3.RequestBodyRef{Value: openapi3.NewRequestBody().WithRequired(true).WithJSONSchemaRef(ref("CreateDeliveryRequest"))},
			Responses: openapi3.NewResponses(
				openapi3.WithStatus(http.StatusCreated, &openapi3.ResponseRef{
					Value: openapi3.NewResponse().WithDescription("The delivery has started.").WithJSONSchemaRef(ref("Tracking")),
				}),
				openapi3.WithStatus(http.StatusOK, &openapi3.ResponseRef{
					Value: openapi3.NewResponse().WithDescription("There is a delivery for the order already.").WithJSONSchemaRef(ref("Tracking")),
				}),
				openapi3.WithStatus(http.StatusBadRequest, problemResponse("The body is not valid JSON, or the order id, address or item count is not valid.")),
				openapi3.WithStatus(http.StatusUnauthorized, unauthorized),
			),
		},
	})

	orderID := openapi3.NewPathParameter("orderId").
		WithDescription("The id of the order.").
		WithSchema(openapi3.NewStringSchema().WithMaxLength(64))
	orderID.Example = "ord_1a2b3c4d5e6f"

	doc.Paths.Set("/deliveries/{orderId}", &openapi3.PathItem{
		Get: &openapi3.Operation{
			Tags:        []string{"Deliveries"},
			Summary:     "Track a delivery",
			Description: "Says where the delivery is now. Which steps are complete depends on how long ago the delivery started.",
			OperationID: "getDelivery",
			Parameters:  openapi3.Parameters{&openapi3.ParameterRef{Value: orderID}},
			Responses: openapi3.NewResponses(
				openapi3.WithStatus(http.StatusOK, &openapi3.ResponseRef{
					Value: openapi3.NewResponse().WithDescription("How the delivery is going.").WithJSONSchemaRef(ref("Tracking")),
				}),
				openapi3.WithStatus(http.StatusUnauthorized, unauthorized),
				openapi3.WithStatus(http.StatusNotFound, problemResponse("There is no delivery for that order.")),
			),
		},
	})

	// The references to the schemas are only names until they are resolved, which validating the document needs
	if err := openapi3.NewLoader().ResolveRefsIn(doc, nil); err != nil {
		return nil, fmt.Errorf("resolving the references of the OpenAPI document: %w", err)
	}

	if err := doc.Validate(context.Background()); err != nil {
		return nil, fmt.Errorf("the OpenAPI document is not valid: %w", err)
	}

	return json.MarshalIndent(doc, "", "  ")
}

// name gives the schemas the names they have in the API, rather than those of the Go types they were generated from.
func name(schemas openapi3.Schemas) {
	for from, to := range map[string]string{"createRequest": "CreateDeliveryRequest", "stepProgress": "TrackingStep", "problem": "Problem"} {
		if schema, ok := schemas[from]; ok {
			schemas[to] = schema
			delete(schemas, from)
		}
	}

	if steps := schemas["Tracking"].Value.Properties["steps"]; steps != nil && steps.Value != nil && steps.Value.Items != nil {
		steps.Value.Items.Ref = "#/components/schemas/TrackingStep"
	}
}

// describe adds what the generator cannot know from the types: what the properties mean.
func describe(schemas openapi3.Schemas) {
	text := map[string]map[string]string{
		"CreateDeliveryRequest": {
			"orderId":   "The order to deliver, at most 64 characters.",
			"address":   "Where to deliver it, at most 256 characters.",
			"itemCount": "How many items are in the order: at least 1.",
		},
		"Tracking": {
			"orderId":    "The order being delivered.",
			"address":    "Where it is being delivered to.",
			"status":     "The step the delivery is at now: `preparing`, `baking`, `quality-control`, `out-for-delivery` or `delivered`.",
			"driver":     "Who is delivering it.",
			"etaMinutes": "How long until it arrives, in minutes (0 once it has).",
			"createdAt":  "When the delivery started.",
			"updatedAt":  "When the status last changed.",
			"steps":      "Every step of the delivery, in order, and how far it has got.",
		},
		"TrackingStep": {
			"status":    "The step: the same values as the delivery's `status`.",
			"label":     "What to call the step.",
			"completed": "The delivery has finished this step.",
			"reached":   "The delivery is at, or past, this step.",
			"at":        "When the step was reached, or is expected to be.",
		},
		"Problem": {
			"title":  "A short summary of what went wrong.",
			"status": "The HTTP status code.",
			"detail": "What went wrong, in full.",
		},
	}

	for name, properties := range text {
		schema, ok := schemas[name]
		if !ok || schema.Value == nil {
			continue
		}
		for property, description := range properties {
			if p, found := schema.Value.Properties[property]; found && p.Value != nil {
				// The generator gives every property of the same type the very same schema, so describing one means copying it
				described := *p.Value
				described.Description = description
				schema.Value.Properties[property] = &openapi3.SchemaRef{Value: &described}
			}
		}
	}

	if request, ok := schemas["CreateDeliveryRequest"]; ok && request.Value != nil {
		request.Value.Description = "The order to start delivering."
		if itemCount := request.Value.Properties["itemCount"]; itemCount != nil && itemCount.Value != nil {
			minimum := float64(1)
			itemCount.Value.Min = &minimum // (already a copy of its own, from the descriptions above)
		}
		request.Value.Example = map[string]any{"orderId": "ord_1a2b3c4d5e6f", "address": "1 Loki Lane, Tempo Town", "itemCount": 3}
	}

	if tracking, ok := schemas["Tracking"]; ok && tracking.Value != nil {
		tracking.Value.Description = "How a delivery is going."
	}

	if p, ok := schemas["Problem"]; ok && p.Value != nil {
		p.Value.Description = "What every error response looks like."
		p.Value.Example = map[string]any{"title": "Delivery not found", "status": http.StatusNotFound, "detail": "No delivery exists for order " + strconv.Quote("ord_1") + "."}
	}
}
