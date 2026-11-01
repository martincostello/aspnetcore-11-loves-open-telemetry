package com.grotspizzeria.orders;

import java.math.BigDecimal;
import java.time.Instant;
import java.util.List;

/** The JSON shapes exchanged by this service (see docs/service-contract.md). */
// A namespace for the records below: there is nothing to instantiate and nothing static to call
@SuppressWarnings("PMD.MissingStaticMethodInNonInstantiatableClass")
public final class Models {

    private Models() {
    }

    // Incoming requests

    public record OrderRequest(
            String userId,
            String userName,
            String address,
            List<ItemRequest> items,
            PaymentRequest payment) {
    }

    public record ItemRequest(String pizzaId, int quantity) {
    }

    public record PaymentRequest(String method, String cardLast4) {
    }

    // Outgoing responses

    public record Order(
            String id,
            String userId,
            String userName,
            String status,
            String address,
            List<OrderLine> items,
            BigDecimal subtotal,
            BigDecimal tax,
            BigDecimal taxRate,
            BigDecimal total,
            String currency,
            String transactionId,
            Instant createdAt) {
    }

    public record OrderLine(
            String pizzaId,
            String name,
            int quantity,
            BigDecimal unitPrice,
            BigDecimal lineTotal) {
    }

    public record Problem(String title, int status, String detail) {
    }

    // Downstream: menu

    public record PriceRequest(List<ItemRequest> items) {
    }

    public record PriceResponse(List<OrderLine> lines, BigDecimal subtotal) {
    }

    // Downstream: checkout

    public record CheckoutRequest(
            String orderId,
            BigDecimal amount,
            String currency,
            String paymentMethod,
            String cardLast4) {
    }

    public record CheckoutResponse(
            String transactionId,
            String orderId,
            BigDecimal subtotal,
            BigDecimal tax,
            BigDecimal taxRate,
            BigDecimal total,
            String currency,
            String status) {
    }

    // Downstream: delivery

    public record DeliveryRequest(String orderId, String address, int itemCount) {
    }
}
