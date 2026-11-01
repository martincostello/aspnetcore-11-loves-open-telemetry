package com.grotspizzeria.orders;

import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.List;
import java.util.UUID;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;

import com.grotspizzeria.orders.DownstreamClients.CheckoutClient;
import com.grotspizzeria.orders.DownstreamClients.DeliveryClient;
import com.grotspizzeria.orders.DownstreamClients.MenuClient;
import com.grotspizzeria.orders.Models.CheckoutRequest;
import com.grotspizzeria.orders.Models.CheckoutResponse;
import com.grotspizzeria.orders.Models.DeliveryRequest;
import com.grotspizzeria.orders.Models.ItemRequest;
import com.grotspizzeria.orders.Models.Order;
import com.grotspizzeria.orders.Models.OrderRequest;
import com.grotspizzeria.orders.Models.PriceRequest;
import com.grotspizzeria.orders.Models.PriceResponse;

import io.opentelemetry.api.trace.Span;

@Service
class OrderService {

    private static final Logger log = LoggerFactory.getLogger(OrderService.class);

    private final MenuClient menu;
    private final CheckoutClient checkout;
    private final DeliveryClient delivery;
    private final OrderRepository repository;
    private final OrderMetrics metrics;

    OrderService(MenuClient menu, CheckoutClient checkout, DeliveryClient delivery,
            OrderRepository repository, OrderMetrics metrics) {
        this.menu = menu;
        this.checkout = checkout;
        this.delivery = delivery;
        this.repository = repository;
        this.metrics = metrics;
    }

    Order place(OrderRequest request) {
        String orderId = "ord_" + UUID.randomUUID().toString().replace("-", "").substring(0, 12);
        Span.current().setAttribute("grot.order.id", orderId);

        try {
            validate(request);

            // 1. Price the basket
            PriceResponse price = menu.price(new PriceRequest(request.items()));

            // Never take payment for nothing, or less than nothing, whatever the quote says
            if (price.subtotal() == null || price.subtotal().signum() <= 0) {
                throw new ProblemException(HttpStatus.BAD_GATEWAY, "Bad price",
                        "The menu service returned a price that cannot be charged.");
            }

            // 2. Take payment
            CheckoutResponse payment = checkout.charge(new CheckoutRequest(
                    orderId, price.subtotal(), "GBP",
                    request.payment().method(), request.payment().cardLast4()));

            // 3. Schedule the delivery
            int itemCount = request.items().stream().mapToInt(ItemRequest::quantity).sum();
            delivery.schedule(new DeliveryRequest(orderId, request.address(), itemCount));

            Order order = new Order(
                    orderId, request.userId(), request.userName(), "confirmed", request.address(),
                    price.lines(), price.subtotal(), payment.tax(), payment.taxRate(), payment.total(), payment.currency(),
                    payment.transactionId(), Instant.now().truncatedTo(ChronoUnit.SECONDS));
            repository.save(order);

            metrics.placed();
            if (log.isInfoEnabled()) {
                log.info("Order {} placed for user {}: {} item(s), total {} {}",
                        orderId, request.userId(), itemCount, order.total(), order.currency());
            }
            return order;
        } catch (ProblemException e) {
            if (e.status().is4xxClientError()) {
                metrics.rejected();
            } else {
                metrics.failed();
            }
            if (log.isWarnEnabled()) {
                log.warn("Order {} was not placed: {} ({})", orderId, e.getMessage(), e.status().value());
            }
            throw e;
        } catch (RuntimeException e) {
            metrics.failed();
            log.warn("Order {} failed unexpectedly", orderId, e);
            throw e;
        }
    }

    Order get(String id) {
        Span.current().setAttribute("grot.order.id", id);
        return repository.findById(id).orElseThrow(
                () -> new ProblemException(HttpStatus.NOT_FOUND, "Not found", "Order " + id + " was not found."));
    }

    List<Order> listForUser(String userId) {
        return repository.findByUserId(userId);
    }

    private static void validate(OrderRequest request) {
        if (isBlank(request.userId()) || isBlank(request.address())) {
            throw invalid("userId and address are required.");
        }
        if (request.items() == null || request.items().isEmpty()) {
            throw invalid("At least one item is required.");
        }
        if (request.items().stream().anyMatch(i -> i == null || i.quantity() < 1 || i.quantity() > 20)) {
            throw invalid("Each item needs a quantity between 1 and 20.");
        }
        if (request.payment() == null
                || !("card".equals(request.payment().method()) || "cash".equals(request.payment().method()))) {
            throw invalid("payment.method must be 'card' or 'cash'.");
        }
    }

    private static boolean isBlank(String value) {
        return value == null || value.isBlank();
    }

    private static ProblemException invalid(String detail) {
        return new ProblemException(HttpStatus.BAD_REQUEST, "Invalid order", detail);
    }
}
