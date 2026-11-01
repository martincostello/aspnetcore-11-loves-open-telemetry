"""Grot's Pizzeria - checkout service."""

import logging

from fastapi import APIRouter, Depends, FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from opentelemetry import trace
from opentelemetry.processor.baggage import BaggageSpanProcessor

from .models import CheckoutRequest, CheckoutResponse, Problem
from .payments import (
    TAX_RATE,
    GatewayUnavailable,
    PaymentDeclined,
    authorize,
    calculate_tax,
    money,
    payments_counter,
)
from .security import Unauthorized, require_token

logger = logging.getLogger("grots.checkout")
logging.basicConfig(level=logging.INFO)

# Copy the sample's own baggage (grot.*) onto every span, so that it can be seen in the trace.
# (opentelemetry-instrument has already created the tracer provider by the time this module is imported.)
_provider = trace.get_tracer_provider()
if hasattr(_provider, "add_span_processor"):
    _provider.add_span_processor(BaggageSpanProcessor(lambda key: key.startswith("grot.")))

app = FastAPI(title="Grot's Pizzeria - Checkout", docs_url=None, redoc_url=None, openapi_url=None)


def problem(status: int, title: str, detail: str) -> JSONResponse:
    return JSONResponse(Problem(title=title, status=status, detail=detail).model_dump(), status_code=status)


@app.exception_handler(RequestValidationError)
async def validation_error(_: Request, exc: RequestValidationError) -> JSONResponse:
    def field(loc: tuple) -> str:
        return ".".join(p for p in loc[1:] if isinstance(p, str)) or "body"

    detail = "; ".join(f"{field(e['loc'])}: {e['msg']}" for e in exc.errors())
    return problem(400, "Invalid request", detail)


@app.exception_handler(Unauthorized)
async def unauthorized(_: Request, __: Unauthorized) -> JSONResponse:
    response = problem(401, "Unauthorized", "A valid bearer token is required.")
    response.headers["WWW-Authenticate"] = "Bearer"
    return response


@app.get("/healthz")
async def healthz() -> dict[str, str]:
    return {"status": "ok"}


# Everything except /healthz requires a valid JWT.
secured = APIRouter(dependencies=[Depends(require_token)])


@secured.post("/checkout", status_code=201, response_model=CheckoutResponse)
async def checkout(request: CheckoutRequest):
    trace.get_current_span().set_attribute("grot.order.id", request.order_id)
    attributes = {"grot.payment.method": request.payment_method}

    try:
        transaction_id = await authorize(request.payment_method, request.card_last4)
    except PaymentDeclined:
        payments_counter.add(1, {**attributes, "grot.payment.outcome": "declined"})
        logger.warning("Payment for order %s was declined", request.order_id)
        return problem(402, "Payment declined", "The payment was declined by the issuer.")
    except GatewayUnavailable:
        payments_counter.add(1, {**attributes, "grot.payment.outcome": "error"})
        logger.error("Payment gateway unavailable while processing order %s", request.order_id)
        return problem(503, "Payment gateway unavailable", "The payment gateway is unavailable, please try again.")

    subtotal = money(request.amount)
    tax = calculate_tax(subtotal)
    total = subtotal + tax

    payments_counter.add(1, {**attributes, "grot.payment.outcome": "approved"})
    logger.info("Approved payment %s for order %s (total %s %s)", transaction_id, request.order_id, total, request.currency)

    return CheckoutResponse(
        transaction_id=transaction_id,
        order_id=request.order_id,
        subtotal=float(subtotal),
        tax=float(tax),
        tax_rate=float(TAX_RATE),
        total=float(total),
        currency=request.currency,
        status="approved",
    )


app.include_router(secured)
