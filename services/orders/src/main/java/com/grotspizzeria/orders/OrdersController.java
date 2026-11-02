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
import com.grotspizzeria.orders.Models.Problem;

import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.media.Content;
import io.swagger.v3.oas.annotations.media.Schema;
import io.swagger.v3.oas.annotations.responses.ApiResponse;
import io.swagger.v3.oas.annotations.security.SecurityRequirements;
import io.swagger.v3.oas.annotations.tags.Tag;

@RestController
class OrdersController {

    private final OrderService orders;

    OrdersController(OrderService orders) {
        this.orders = orders;
    }

    @GetMapping("/healthz")
    @Tag(name = "Health")
    @Operation(summary = "Check that the service is running")
    @SecurityRequirements
    @ApiResponse(responseCode = "200", description = "The service is running.")
    Map<String, String> health() {
        return Map.of("status", "ok");
    }

    @PostMapping("/orders")
    @ResponseStatus(HttpStatus.CREATED)
    @Tag(name = "Orders")
    @Operation(summary = "Place an order",
            description = "Prices the order with the menu, takes payment and schedules the delivery. If a step fails, so does the order: "
                    + "the response is the failure of the service that was responsible, and no order is recorded.")
    @ApiResponse(responseCode = "201", description = "The order was placed.")
    @ApiResponse(responseCode = "401", description = "There is no valid bearer token.",
            content = @Content(schema = @Schema(implementation = Problem.class)))
    @ApiResponse(responseCode = "400", description = "The order is not valid, or the menu does not know one of the items.",
            content = @Content(schema = @Schema(implementation = Problem.class)))
    @ApiResponse(responseCode = "402", description = "The payment was declined.",
            content = @Content(schema = @Schema(implementation = Problem.class)))
    @ApiResponse(responseCode = "502", description = "A service that the order needs could not be reached or failed.",
            content = @Content(schema = @Schema(implementation = Problem.class)))
    @ApiResponse(responseCode = "503", description = "The payment gateway is unavailable. Try again.",
            content = @Content(schema = @Schema(implementation = Problem.class)))
    Order place(@RequestBody OrderRequest request) {
        return orders.place(request);
    }

    @GetMapping("/orders")
    @Tag(name = "Orders")
    @Operation(summary = "List a user's orders", description = "The orders that the user has placed, newest first.")
    @ApiResponse(responseCode = "200", description = "The user's orders (none, if they have placed none).")
    @ApiResponse(responseCode = "401", description = "There is no valid bearer token.",
            content = @Content(schema = @Schema(implementation = Problem.class)))
    @ApiResponse(responseCode = "400", description = "There is no `userId`.",
            content = @Content(schema = @Schema(implementation = Problem.class)))
    List<Order> list(@RequestParam String userId) {
        return orders.listForUser(userId);
    }

    @GetMapping("/orders/{id}")
    @Tag(name = "Orders")
    @Operation(summary = "Get an order")
    @ApiResponse(responseCode = "200", description = "The order.")
    @ApiResponse(responseCode = "401", description = "There is no valid bearer token.",
            content = @Content(schema = @Schema(implementation = Problem.class)))
    @ApiResponse(responseCode = "404", description = "There is no order with that id.",
            content = @Content(schema = @Schema(implementation = Problem.class)))
    Order get(@PathVariable String id) {
        return orders.get(id);
    }
}
