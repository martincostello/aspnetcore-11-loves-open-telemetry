package com.grotspizzeria.orders;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.inOrder;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;

import java.math.BigDecimal;
import java.util.List;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.NullSource;
import org.junit.jupiter.params.provider.ValueSource;
import org.mockito.ArgumentCaptor;
import org.springframework.http.HttpStatus;

import com.grotspizzeria.orders.DownstreamClients.CheckoutClient;
import com.grotspizzeria.orders.DownstreamClients.DeliveryClient;
import com.grotspizzeria.orders.DownstreamClients.MenuClient;
import com.grotspizzeria.orders.Models.CheckoutRequest;
import com.grotspizzeria.orders.Models.CheckoutResponse;
import com.grotspizzeria.orders.Models.DeliveryRequest;
import com.grotspizzeria.orders.Models.ItemRequest;
import com.grotspizzeria.orders.Models.Order;
import com.grotspizzeria.orders.Models.OrderLine;
import com.grotspizzeria.orders.Models.OrderRequest;
import com.grotspizzeria.orders.Models.PaymentRequest;
import com.grotspizzeria.orders.Models.PriceRequest;
import com.grotspizzeria.orders.Models.PriceResponse;

/**
 * What it takes to place an order: price it, take payment, start the delivery, and keep it, in that order, with each step
 * only happening if the one before it worked. The services it calls are replaced, so these are about the orchestration and not
 * the services.
 */
class OrderServiceTests {

    private MenuClient menu;
    private CheckoutClient checkout;
    private DeliveryClient delivery;
    private OrderRepository repository;
    private OrderMetrics metrics;
    private OrderService service;

    @BeforeEach
    void setUp() {
        menu = mock(MenuClient.class);
        checkout = mock(CheckoutClient.class);
        delivery = mock(DeliveryClient.class);
        repository = new OrderRepository();
        metrics = mock(OrderMetrics.class);
        service = new OrderService(menu, checkout, delivery, repository, metrics);
    }

    @Test
    void pricesThenPaysThenDeliversThenKeepsTheOrder() {
        // A currency that is not pounds, because nothing here may assume one
        when(menu.price(any())).thenReturn(quote("19.00", "EUR"));
        when(checkout.charge(any())).thenReturn(receipt("19.00", "1.52", "20.52", "EUR"));

        Order order = service.place(request());

        var calls = inOrder(menu, checkout, delivery);
        calls.verify(menu).price(any());
        calls.verify(checkout).charge(any());
        calls.verify(delivery).schedule(any());

        // The menu is asked about what the customer asked for, and what is charged is what the menu said it costs
        ArgumentCaptor<PriceRequest> priced = ArgumentCaptor.forClass(PriceRequest.class);
        verify(menu).price(priced.capture());
        assertEquals(request().items(), priced.getValue().items());

        ArgumentCaptor<CheckoutRequest> charged = ArgumentCaptor.forClass(CheckoutRequest.class);
        verify(checkout).charge(charged.capture());
        assertEquals(new BigDecimal("19.00"), charged.getValue().amount());
        assertEquals("EUR", charged.getValue().currency());
        assertEquals("card", charged.getValue().paymentMethod());
        assertEquals("4242", charged.getValue().cardLast4());
        assertEquals(order.id(), charged.getValue().orderId());

        // The delivery is for this order, to this address, of everything in it
        ArgumentCaptor<DeliveryRequest> delivered = ArgumentCaptor.forClass(DeliveryRequest.class);
        verify(delivery).schedule(delivered.capture());
        assertEquals(new DeliveryRequest(order.id(), "1 Loki Lane, Tempo Town", 3), delivered.getValue());

        // The order says what was charged, in the currency it was charged in, and can be found again
        assertEquals("confirmed", order.status());
        assertEquals(new BigDecimal("20.52"), order.total());
        assertEquals("EUR", order.currency());
        assertEquals("txn_1", order.transactionId());
        assertSame(order, service.get(order.id()));
        assertEquals(List.of(order), service.listForUser("42"));
        verify(metrics).placed();
    }

    @ParameterizedTest
    @NullSource
    @ValueSource(strings = {"0", "0.00", "-0.01", "-19"})
    void neverTakesPaymentForANothingOrLessThanNothingQuote(String subtotal) {
        when(menu.price(any())).thenReturn(new PriceResponse(lines(), subtotal == null ? null : new BigDecimal(subtotal), "GBP"));

        ProblemException problem = assertThrows(ProblemException.class, () -> service.place(request()));

        assertEquals(HttpStatus.BAD_GATEWAY, problem.status());
        assertEquals("Bad price", problem.title());
        assertNothingAfterPricing();
        verify(metrics).failed();
    }

    @ParameterizedTest
    @NullSource
    @ValueSource(strings = {"", "  "})
    void neverTakesPaymentInACurrencyThatTheMenuDidNotSay(String currency) {
        when(menu.price(any())).thenReturn(quote("19.00", currency));

        ProblemException problem = assertThrows(ProblemException.class, () -> service.place(request()));

        assertEquals(HttpStatus.BAD_GATEWAY, problem.status());
        assertNothingAfterPricing();
        verify(metrics).failed();
    }

    @Test
    void aDeclinedPaymentStopsTheOrderBeforeAnythingIsDeliveredOrKept() {
        when(menu.price(any())).thenReturn(quote("19.00", "GBP"));
        when(checkout.charge(any())).thenThrow(new ProblemException(HttpStatus.PAYMENT_REQUIRED, "Payment declined", "The payment was declined."));

        ProblemException problem = assertThrows(ProblemException.class, () -> service.place(request()));

        // The caller is told what the checkout service said: it was a decision, not a failure
        assertEquals(HttpStatus.PAYMENT_REQUIRED, problem.status());
        assertEquals("Payment declined", problem.title());
        verify(delivery, never()).schedule(any());
        assertTrue(repository.findByUserId("42").isEmpty());
        verify(metrics).rejected();
        verify(metrics, never()).failed();
    }

    @Test
    void anUnavailablePaymentGatewayStopsTheOrderBeforeAnythingIsDeliveredOrKept() {
        when(menu.price(any())).thenReturn(quote("19.00", "GBP"));
        when(checkout.charge(any())).thenThrow(new ProblemException(HttpStatus.BAD_GATEWAY, "Bad gateway", "The checkout service failed with status 503."));

        ProblemException problem = assertThrows(ProblemException.class, () -> service.place(request()));

        assertEquals(HttpStatus.BAD_GATEWAY, problem.status());
        verify(delivery, never()).schedule(any());
        assertTrue(repository.findByUserId("42").isEmpty());
        verify(metrics).failed();
        verify(metrics, never()).rejected();
    }

    /**
     * A KNOWN LIMITATION, which is kept as it is for the sake of a sample that is about telemetry: payment is taken before the
     * delivery is arranged and the order is kept, and nothing undoes the payment when a step after it fails. So the customer is told
     * that the order failed, with their (fake) payment taken, and there is no order to find. A real system would use a saga that
     * refunds, or an outbox, with an idempotency key on the payment so that trying again is safe.
     */
    @Test
    void aDeliveryThatFailsAfterPaymentLeavesThePaymentTakenAndNoOrderKept() {
        when(menu.price(any())).thenReturn(quote("19.00", "GBP"));
        when(checkout.charge(any())).thenReturn(receipt("19.00", "1.52", "20.52", "GBP"));
        doThrow(new ProblemException(HttpStatus.BAD_GATEWAY, "Bad gateway", "The delivery service could not be reached."))
                .when(delivery).schedule(any());

        ProblemException problem = assertThrows(ProblemException.class, () -> service.place(request()));

        assertEquals(HttpStatus.BAD_GATEWAY, problem.status());

        verify(checkout).charge(any()); // the payment was taken...
        assertTrue(repository.findByUserId("42").isEmpty()); // ...and there is no order for it
        verify(metrics).failed();
        verify(metrics, never()).placed();
    }

    @Test
    void anUnexpectedErrorIsAFailureToo() {
        when(menu.price(any())).thenThrow(new IllegalStateException("boom"));

        assertThrows(IllegalStateException.class, () -> service.place(request()));

        verifyNoInteractions(checkout, delivery);
        verify(metrics).failed();
    }

    @ParameterizedTest
    @ValueSource(ints = {0, -1, 21})
    void refusesQuantitiesThatAreNotFrom1To20WithoutAskingAnyone(int quantity) {
        OrderRequest request = new OrderRequest("42", "grot", "1 Loki Lane", List.of(new ItemRequest("margherita", quantity)), card());

        ProblemException problem = assertThrows(ProblemException.class, () -> service.place(request));

        assertEquals(HttpStatus.BAD_REQUEST, problem.status());
        verifyNoInteractions(menu, checkout, delivery);
        verify(metrics).rejected();
    }

    @Test
    void refusesWhatIsNotAnOrderWithoutAskingAnyone() {
        List<OrderRequest> notOrders = List.of(
                new OrderRequest("", "grot", "1 Loki Lane", List.of(new ItemRequest("margherita", 1)), card()),
                new OrderRequest("42", "grot", " ", List.of(new ItemRequest("margherita", 1)), card()),
                new OrderRequest("42", "grot", "1 Loki Lane", List.of(), card()),
                new OrderRequest("42", "grot", "1 Loki Lane", null, card()),
                new OrderRequest("42", "grot", "1 Loki Lane", List.of(new ItemRequest("margherita", 1)), null),
                new OrderRequest("42", "grot", "1 Loki Lane", List.of(new ItemRequest("margherita", 1)), new PaymentRequest("bitcoin", null)));

        for (OrderRequest notAnOrder : notOrders) {
            ProblemException problem = assertThrows(ProblemException.class, () -> service.place(notAnOrder));
            assertEquals(HttpStatus.BAD_REQUEST, problem.status());
        }

        verifyNoInteractions(menu, checkout, delivery);
    }

    @Test
    void anOrderThatDoesNotExistIsNotFound() {
        ProblemException problem = assertThrows(ProblemException.class, () -> service.get("ord_missing"));

        assertEquals(HttpStatus.NOT_FOUND, problem.status());
    }

    private void assertNothingAfterPricing() {
        verify(menu).price(any());
        verifyNoInteractions(checkout, delivery);
        assertTrue(repository.findByUserId("42").isEmpty());
    }

    private static OrderRequest request() {
        return new OrderRequest(
                "42", "grot", "1 Loki Lane, Tempo Town",
                List.of(new ItemRequest("margherita", 2), new ItemRequest("garlic-bread", 1)),
                card());
    }

    private static PaymentRequest card() {
        return new PaymentRequest("card", "4242");
    }

    private static List<OrderLine> lines() {
        return List.of(new OrderLine("margherita", "Margherita", 2, new BigDecimal("9.50"), new BigDecimal("19.00")));
    }

    private static PriceResponse quote(String subtotal, String currency) {
        return new PriceResponse(lines(), new BigDecimal(subtotal), currency);
    }

    private static CheckoutResponse receipt(String subtotal, String tax, String total, String currency) {
        return new CheckoutResponse("txn_1", "ord_1", new BigDecimal(subtotal), new BigDecimal(tax), new BigDecimal("0.08"),
                new BigDecimal(total), currency, "approved");
    }
}
