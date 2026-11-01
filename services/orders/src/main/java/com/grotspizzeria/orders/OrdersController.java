package com.grotspizzeria.orders;

import java.util.List;
import java.util.Map;

import org.springframework.http.HttpStatus;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

import com.grotspizzeria.orders.Models.Order;
import com.grotspizzeria.orders.Models.OrderRequest;

@RestController
class OrdersController {

    private final OrderService orders;

    OrdersController(OrderService orders) {
        this.orders = orders;
    }

    @GetMapping("/healthz")
    Map<String, String> health() {
        return Map.of("status", "ok");
    }

    @PostMapping("/orders")
    @ResponseStatus(HttpStatus.CREATED)
    Order place(@RequestBody OrderRequest request) {
        return orders.place(request);
    }

    @GetMapping("/orders")
    List<Order> list(@RequestParam String userId) {
        return orders.listForUser(userId);
    }

    @GetMapping("/orders/{id}")
    Order get(@PathVariable String id) {
        return orders.get(id);
    }
}
