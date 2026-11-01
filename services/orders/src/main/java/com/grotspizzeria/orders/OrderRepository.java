package com.grotspizzeria.orders;

import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentMap;

import org.springframework.stereotype.Repository;

import com.grotspizzeria.orders.Models.Order;

/** Orders live in memory for the lifetime of the process. */
@Repository
class OrderRepository {

    private final ConcurrentMap<String, Order> orders = new ConcurrentHashMap<>();

    void save(Order order) {
        orders.put(order.id(), order);
    }

    Optional<Order> findById(String id) {
        return Optional.ofNullable(orders.get(id));
    }

    List<Order> findByUserId(String userId) {
        return orders.values().stream()
                .filter(o -> o.userId().equals(userId))
                .sorted(Comparator.comparing(Order::createdAt).reversed())
                .toList();
    }
}
