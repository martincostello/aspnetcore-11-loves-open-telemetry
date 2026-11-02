import os
import time
from decimal import Decimal

import jwt
import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.payments import calculate_tax, parse_tax_rate


@pytest.fixture(autouse=True)
def no_random_failures(monkeypatch):
    monkeypatch.setenv("CHECKOUT_FAILURE_RATE", "0")


client = TestClient(app)


def token(key=None, issuer="grots-web", audience="grots-pizzeria", expires_in=300):
    now = int(time.time())
    claims = {"iss": issuer, "aud": audience, "sub": "42", "name": "grot", "iat": now, "exp": now + expires_in}
    return jwt.encode(claims, key or os.environ["JWT_SIGNING_KEY"], algorithm="HS256")


def post(payload, bearer=None):
    headers = {"Authorization": f"Bearer {bearer or token()}"}
    return client.post("/checkout", json=payload, headers=headers)


def body(**overrides):
    return {"orderId": "ord_1", "amount": 19.0, "currency": "GBP", "paymentMethod": "card", "cardLast4": "4242"} | overrides


def test_approved_card_payment():
    response = post(body())
    assert response.status_code == 201
    result = response.json()
    assert result["status"] == "approved"
    assert result["transactionId"].startswith("txn_")
    assert (result["subtotal"], result["tax"], result["total"]) == (19.0, 1.52, 20.52)
    assert result["taxRate"] == 0.08


def test_tax_is_added_at_the_configured_rate_and_the_receipt_says_what_it_was(monkeypatch):
    monkeypatch.setattr("app.payments.TAX_RATE", Decimal("0.2"))
    monkeypatch.setattr("app.main.TAX_RATE", Decimal("0.2"))

    result = post(body()).json()

    assert (result["subtotal"], result["tax"], result["total"]) == (19.0, 3.8, 22.8)
    assert result["taxRate"] == 0.2


@pytest.mark.parametrize(
    ("value", "expected"),
    [(None, "0.08"), ("", "0.08"), ("  ", "0.08"), ("0.2", "0.2"), (" 0.125 ", "0.125"), ("0", "0")],
)
def test_tax_rate_comes_from_the_configuration(value, expected):
    assert parse_tax_rate(value) == Decimal(expected)


@pytest.mark.parametrize("value", ["8", "1", "-0.01", "eight", "NaN", "Infinity"])
def test_tax_rate_must_be_a_fraction_from_zero_to_below_one(value):
    with pytest.raises(ValueError, match="TAX_RATE"):
        parse_tax_rate(value)


def test_cash_does_not_need_card():
    assert post(body(paymentMethod="cash", cardLast4=None)).status_code == 201


def test_declined_card():
    response = post(body(cardLast4="0000"))
    assert response.status_code == 402
    assert response.json()["title"] == "Payment declined"


def test_gateway_failure(monkeypatch):
    monkeypatch.setenv("CHECKOUT_FAILURE_RATE", "1")
    assert post(body()).status_code == 503


@pytest.mark.parametrize(
    "payload",
    [body(amount=0), body(amount=-0.01), body(amount=-19), body(amount=-1e9), body(amount="-5"), body(amount=10_000.01), body(cardLast4=None), body(cardLast4="12"), body(paymentMethod="bitcoin"), {}],
)
def test_validation_errors(payload):
    response = post(payload)
    assert response.status_code == 400
    assert set(response.json()) == {"title", "status", "detail"}


def test_tax_rounds_half_up():
    assert calculate_tax(Decimal("9.50")) == Decimal("0.76")
    assert calculate_tax(Decimal("11.50")) == Decimal("0.92")
    assert calculate_tax(Decimal("0.0625")) == Decimal("0.01")


def test_healthz_is_anonymous():
    assert client.get("/healthz").status_code == 200


@pytest.mark.parametrize(
    "bearer",
    [
        pytest.param(None, id="missing"),
        pytest.param("not-a-jwt", id="malformed"),
        pytest.param(token(expires_in=-120), id="expired"),
        pytest.param(token(audience="someone-else"), id="wrong-audience"),
        pytest.param(token(issuer="someone-else"), id="wrong-issuer"),
        pytest.param(token(key="a-different-signing-key-0123456789-abcdef"), id="wrong-key"),
    ],
)
def test_invalid_token_is_unauthorized(bearer):
    headers = {"Authorization": f"Bearer {bearer}"} if bearer else {}
    response = client.post("/checkout", json=body(), headers=headers)
    assert response.status_code == 401
    assert response.json()["title"] == "Unauthorized"
    assert set(response.json()) == {"title", "status", "detail"}


def test_token_within_clock_skew_is_accepted():
    assert post(body(), bearer=token(expires_in=-10)).status_code == 201


def test_signing_key_is_required():
    from app.security import JwtSettings

    os.environ["JWT_SIGNING_KEY"], previous = "too-short", os.environ["JWT_SIGNING_KEY"]
    try:
        with pytest.raises(RuntimeError):
            JwtSettings.from_environment()
    finally:
        os.environ["JWT_SIGNING_KEY"] = previous
