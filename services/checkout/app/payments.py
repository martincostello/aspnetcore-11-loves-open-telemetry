"""A fake payment gateway."""

import asyncio
import os
import random
import uuid
from decimal import ROUND_HALF_UP, Decimal, InvalidOperation

from opentelemetry import metrics

CENT = Decimal("0.01")


def parse_tax_rate(value: str | None) -> Decimal:
    """The tax rate as a fraction (0.08 is 8%): TAX_RATE, or 0.08 if it is not set.

    A blank value, as Docker Compose passes for an unset variable, is not set. The rate is returned with every receipt, so
    nothing else needs to know it. A value that is not from 0 up to (but not including) 1 stops the service starting, as does
    one with more precision than a JSON number can carry: the receipt would then report a slightly different rate from the one the
    tax was charged at (0.012399999999999999 is reported as 0.0124, which on 37.50 is 0.47 where 0.46 was charged).
    """
    text = (value or "").strip() or "0.08"

    try:
        rate = Decimal(text)
    except InvalidOperation:
        rate = None

    if rate is None or not rate.is_finite() or not Decimal(0) <= rate < Decimal(1):
        raise ValueError(f"TAX_RATE must be a fraction from 0 up to 1 (0.08 is 8%), but was '{text}'.")

    if Decimal(repr(float(rate))) != rate:
        raise ValueError(f"TAX_RATE has more precision than the receipt's number can carry (it would be reported as {float(rate)}), but was '{text}'.")

    return rate


TAX_RATE = parse_tax_rate(os.environ.get("TAX_RATE"))

_meter = metrics.get_meter("grots.checkout")
payments_counter = _meter.create_counter(
    "grot.checkout.payments",
    unit="{payment}",
    description="Number of payments processed by the checkout service.",
)


class PaymentDeclined(Exception):
    pass


class GatewayUnavailable(Exception):
    pass


def failure_rate() -> float:
    return float(os.environ.get("CHECKOUT_FAILURE_RATE", "0.05"))


def money(value: Decimal) -> Decimal:
    return value.quantize(CENT, rounding=ROUND_HALF_UP)


def calculate_tax(amount: Decimal) -> Decimal:
    return money(amount * TAX_RATE)


async def authorize(method: str, card_last4: str | None) -> str:
    """Make a fake call to a payment gateway and return a transaction id."""
    await asyncio.sleep(random.uniform(0.05, 0.25))

    # A declined card is deterministic, so it must not be masked by a random gateway failure.
    if method == "card" and card_last4 == "0000":
        raise PaymentDeclined()

    # Likewise a gateway that is down, for tests and demos of what a failure looks like (the random failures below are not repeatable)
    if method == "card" and card_last4 == "0503":
        raise GatewayUnavailable()

    if random.random() < failure_rate():
        raise GatewayUnavailable()

    return f"txn_{uuid.uuid4().hex[:12]}"
