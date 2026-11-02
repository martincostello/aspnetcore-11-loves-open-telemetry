"""Grot's Pizzeria - checkout service."""

import logging
from pathlib import Path

from fastapi import APIRouter, Depends, FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
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

STATIC = Path(__file__).parent / "static"

TAGS = [
    {"name": "Checkout", "description": "Take payment for an order."},
    {"name": "Health", "description": "Whether the service is running."},
]

# The documentation page is the root of the service (see static/index.html), so FastAPI's own /docs and /redoc are off. The
# OpenAPI document is generated from the routes and models below, and served at /openapi.json.
app = FastAPI(
    title="grots-checkout",
    version="1.0.0",
    summary="The checkout service of Grot's Pizzeria.",
    description=(
        "Takes payment for an order: it \"authorizes\" the payment with a fake gateway (which takes 50-250ms, and fails at random "
        "so that the telemetry is interesting), adds tax, and returns a receipt. Amounts are in the currency of the request.\n\n"
        "Every endpoint except `/healthz` needs a JWT bearer token, signed with the key shared by the services (HS256, with the "
        "issuer `grots-web` and the audience `grots-pizzeria` unless configured otherwise). `grots-orders` passes on the one it was given."
    ),
    openapi_tags=TAGS,
    docs_url=None,
    redoc_url=None,
    openapi_url="/openapi.json",
)

PROBLEM = {"model": Problem, "description": "What every error response looks like."}


def build_openapi() -> dict:
    """The generated OpenAPI document, described as this service behaves.

    FastAPI documents a 422 for invalid input, but the handler below turns that into a 400 with a Problem.
    """
    if app.openapi_schema is None:
        schema = FastAPI.openapi(app)
        for path in schema["paths"].values():
            for operation in path.values():
                operation["responses"].pop("422", None)
        for name in ("HTTPValidationError", "ValidationError"):
            schema["components"]["schemas"].pop(name, None)
        app.openapi_schema = schema
    return app.openapi_schema


app.openapi = build_openapi


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


@app.get("/", include_in_schema=False)
async def documentation() -> FileResponse:
    """The API documentation page (Swagger UI, from a CDN), so the root of the service is not a 404. It needs no token."""
    return FileResponse(STATIC / "index.html")


app.mount("/swagger-ui", StaticFiles(directory=STATIC / "swagger-ui"), name="swagger-ui")


@app.get("/healthz", tags=["Health"], summary="Check that the service is running")
async def healthz() -> dict[str, str]:
    return {"status": "ok"}


# Everything except /healthz requires a valid JWT.
secured = APIRouter(dependencies=[Depends(require_token)])


@secured.post(
    "/checkout",
    status_code=201,
    response_model=CheckoutResponse,
    tags=["Checkout"],
    summary="Take payment for an order",
    description=(
        "Authorizes the payment and returns the receipt, with the tax added at the configured rate. A card ending in `0000` is "
        "always declined; otherwise, about 5% of payments fail with a 503 (`CHECKOUT_FAILURE_RATE`)."
    ),
    responses={
        400: {**PROBLEM, "description": "The request is not valid: for example the amount is not above 0 and at most 10,000, or a card payment has no `cardLast4`."},
        401: {**PROBLEM, "description": "There is no valid bearer token."},
        402: {**PROBLEM, "description": "The payment was declined by the issuer."},
        503: {**PROBLEM, "description": "The payment gateway is unavailable. Try again."},
    },
)
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
