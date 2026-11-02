package com.grotspizzeria.orders;

import java.math.BigDecimal;
import java.time.Instant;
import java.util.List;

import io.swagger.v3.oas.annotations.media.Schema;

/** The JSON shapes exchanged by this service (see docs/service-contract.md). */
// A namespace for the records below: there is nothing to instantiate and nothing static to call
@SuppressWarnings("PMD.MissingStaticMethodInNonInstantiatableClass")
public final class Models {

    private Models() {
    }

    // Incoming requests

    @Schema(description = "An order to place.")
    public record OrderRequest(
            @Schema(description = "Who is ordering (grots-web gives the id of the signed-in user).", example = "42") String userId,
            @Schema(description = "Their name.", example = "grot") String userName,
            @Schema(description = "Where to deliver it, at most 256 characters.", example = "1 Loki Lane, Tempo Town") String address,
            @Schema(description = "What is being ordered.") List<ItemRequest> items,
            PaymentRequest payment) {
    }

    @Schema(description = "Some of one menu item.")
    public record ItemRequest(
            @Schema(description = "The id of any menu item: a pizza, a side or a dessert.", example = "margherita") String pizzaId,
            @Schema(description = "How many, from 1 to 20.", example = "2", minimum = "1", maximum = "20") int quantity) {
    }

    @Schema(description = "How the order is paid for.")
    public record PaymentRequest(
            @Schema(description = "How the customer is paying.", allowableValues = {"card", "cash"}, example = "card") String method,
            @Schema(description = "The last 4 digits of the card: required for card payments. `0000` is always declined.", example = "4242")
            String cardLast4) {
    }

    // Outgoing responses

    @Schema(description = "An order that was placed.")
    public record Order(
            @Schema(example = "ord_1a2b3c4d5e6f") String id,
            String userId,
            String userName,
            @Schema(example = "confirmed") String status,
            String address,
            List<OrderLine> items,
            @Schema(description = "The price of the items, before tax.", example = "19.00") BigDecimal subtotal,
            @Schema(description = "The tax charged.", example = "1.52") BigDecimal tax,
            @Schema(description = "The rate the tax was charged at, as a fraction: 0.08 is 8%.", example = "0.08") BigDecimal taxRate,
            @Schema(description = "What was charged: the subtotal plus the tax.", example = "20.52") BigDecimal total,
            @Schema(description = "The ISO 4217 code of the currency of the amounts: the one the menu prices things in.", example = "GBP") String currency,
            @Schema(description = "The payment's transaction.", example = "txn_0123456789ab") String transactionId,
            Instant createdAt) {
    }

    @Schema(description = "One line of an order, priced.")
    public record OrderLine(
            String pizzaId,
            String name,
            int quantity,
            @Schema(description = "What one cost when the order was placed (the offer price, if it was on offer), in the order's currency.", example = "9.50")
            BigDecimal unitPrice,
            @Schema(example = "19.00") BigDecimal lineTotal) {
    }

    @Schema(description = "What every error response looks like.")
    public record Problem(
            @Schema(example = "Payment declined") String title,
            @Schema(example = "402") int status,
            @Schema(example = "The payment was declined by the issuer.") String detail) {
    }

    // Downstream: menu

    public record PriceRequest(List<ItemRequest> items) {
    }

    public record PriceResponse(List<OrderLine> lines, BigDecimal subtotal, String currency) {
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
