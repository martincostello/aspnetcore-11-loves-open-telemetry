package com.grotspizzeria.orders;

import org.springframework.stereotype.Component;

import io.opentelemetry.api.GlobalOpenTelemetry;
import io.opentelemetry.api.common.AttributeKey;
import io.opentelemetry.api.common.Attributes;
import io.opentelemetry.api.metrics.LongCounter;

/** Custom business metrics. The OpenTelemetry Java agent supplies the SDK behind {@link GlobalOpenTelemetry}. */
@Component
class OrderMetrics {

    private static final AttributeKey<String> OUTCOME = AttributeKey.stringKey("grot.order.outcome");

    private final LongCounter ordersPlaced = GlobalOpenTelemetry.getMeter("grots-orders")
            .counterBuilder("grot.orders.placed")
            .setUnit("{order}")
            .setDescription("The number of orders that were attempted, by outcome.")
            .build();

    void placed() {
        ordersPlaced.add(1, Attributes.of(OUTCOME, "placed"));
    }

    void rejected() {
        ordersPlaced.add(1, Attributes.of(OUTCOME, "rejected"));
    }

    void failed() {
        ordersPlaced.add(1, Attributes.of(OUTCOME, "failed"));
    }
}
