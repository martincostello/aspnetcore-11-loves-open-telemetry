package com.grotspizzeria.orders;

import org.springframework.http.HttpStatus;

/** An error that is returned to the caller as {@code {"title","status","detail"}}. */
public class ProblemException extends RuntimeException {

    private final HttpStatus status;
    private final String title;

    public ProblemException(HttpStatus status, String title, String detail) {
        this(status, title, detail, null);
    }

    public ProblemException(HttpStatus status, String title, String detail, Throwable cause) {
        super(detail, cause);
        this.status = status;
        this.title = title;
    }

    public HttpStatus status() {
        return status;
    }

    public String title() {
        return title;
    }
}
