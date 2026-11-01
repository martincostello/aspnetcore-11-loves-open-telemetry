package com.grotspizzeria.orders;

import java.time.Duration;

import org.springframework.boot.context.properties.ConfigurationProperties;

/** URLs and timeouts for the services this service calls. */
@ConfigurationProperties("downstream")
public record DownstreamProperties(
        String menuUrl,
        String checkoutUrl,
        String deliveryUrl,
        Duration connectTimeout,
        Duration readTimeout) {
}
