package com.grotspizzeria.orders;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.bind.MissingServletRequestParameterException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

import com.grotspizzeria.orders.Models.Problem;

@RestControllerAdvice
class ApiExceptionHandler {

    private static final Logger log = LoggerFactory.getLogger(ApiExceptionHandler.class);

    @ExceptionHandler(ProblemException.class)
    ResponseEntity<Problem> handleProblem(ProblemException e) {
        return respond(e.status(), e.title(), e.getMessage());
    }

    @ExceptionHandler({HttpMessageNotReadableException.class, MissingServletRequestParameterException.class})
    ResponseEntity<Problem> handleBadRequest(Exception e) {
        return respond(HttpStatus.BAD_REQUEST, "Invalid request", "The request could not be read.");
    }

    @ExceptionHandler(Exception.class)
    ResponseEntity<Problem> handleUnexpected(Exception e) {
        log.error("Unexpected error handling request", e);
        return respond(HttpStatus.INTERNAL_SERVER_ERROR, "Internal server error", "An unexpected error occurred.");
    }

    private static ResponseEntity<Problem> respond(HttpStatus status, String title, String detail) {
        return ResponseEntity.status(status).body(new Problem(title, status.value(), detail));
    }
}
