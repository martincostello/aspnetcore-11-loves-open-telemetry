package com.grotspizzeria.orders;

import java.nio.charset.StandardCharsets;

import org.springframework.boot.context.properties.ConfigurationProperties;

/** Settings used to validate the JWT bearer tokens minted by grots-web. */
@ConfigurationProperties("jwt")
public record JwtProperties(String signingKey, String issuer, String audience) {

    private static final int MIN_KEY_BYTES = 32;

    public JwtProperties {
        if (signingKey == null || signingKey.getBytes(StandardCharsets.UTF_8).length < MIN_KEY_BYTES) {
            throw new IllegalStateException(
                    "JWT_SIGNING_KEY must be set to at least %d bytes of text.".formatted(MIN_KEY_BYTES));
        }
    }
}
