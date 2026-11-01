"""A fake payment gateway."""

import asyncio
import os
import random
import uuid
from decimal import ROUND_HALF_UP, Decimal

from opentelemetry import metrics

TAX_RATE = Decimal("0.08")
CENT = Decimal("0.01")

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

    if random.random() < failure_rate():
        raise GatewayUnavailable()

    return f"txn_{uuid.uuid4().hex[:12]}"
