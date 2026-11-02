package com.grotspizzeria.orders;

import java.util.List;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import io.swagger.v3.oas.models.Components;
import io.swagger.v3.oas.models.OpenAPI;
import io.swagger.v3.oas.models.info.Info;
import io.swagger.v3.oas.models.security.SecurityRequirement;
import io.swagger.v3.oas.models.security.SecurityScheme;
import io.swagger.v3.oas.models.tags.Tag;

/**
 * The parts of the OpenAPI document that are not on the controllers: what the API is, and that its operations need a bearer token.
 * The document is served at /openapi.json, and the documentation page (Swagger UI, from a CDN) at the root of the service.
 */
@Configuration
class OpenApiConfig {

    static final String BEARER = "bearerAuth";

    @Bean
    OpenAPI openApi() {
        return new OpenAPI()
                .info(new Info()
                        .title("grots-orders")
                        .version("1.0.0")
                        .description("""
                                Places and keeps the orders of Grot's Pizzeria. Placing an order prices it with the menu service, takes \
                                payment from the checkout service and starts its delivery with the delivery service, and fails as a whole \
                                if any of them does. The orders are kept in memory. Amounts are in pounds sterling (GBP).

                                Every endpoint except `/healthz` needs a JWT bearer token, signed with the key shared by the services (HS256, \
                                with the issuer `grots-web` and the audience `grots-pizzeria` unless configured otherwise). \
                                `grots-web` mints one for each call it makes, and this service passes it on to the others.
                                """))
                .tags(List.of(
                        new Tag().name("Orders").description("Place orders and look at them."),
                        new Tag().name("Health").description("Whether the service is running.")))
                .components(new Components().addSecuritySchemes(BEARER, new SecurityScheme()
                        .type(SecurityScheme.Type.HTTP)
                        .scheme("bearer")
                        .bearerFormat("JWT")
                        .description("A JWT signed (HS256) with the shared `JWT_SIGNING_KEY`, with the expected `iss` and `aud` claims.")))
                .addSecurityItem(new SecurityRequirement().addList(BEARER));
    }
}
