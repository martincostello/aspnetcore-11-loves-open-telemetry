"""Request and response models for the checkout API."""

from decimal import Decimal
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel


class ApiModel(BaseModel):
    """Base model that (de)serializes camelCase JSON."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class CheckoutRequest(ApiModel):
    order_id: Annotated[str, Field(min_length=1, max_length=64)]
    amount: Annotated[Decimal, Field(gt=0, le=10_000, decimal_places=2)]
    currency: Literal["GBP"] = "GBP"
    payment_method: Literal["card", "cash"]
    card_last4: Annotated[str, Field(pattern=r"^\d{4}$")] | None = None

    @model_validator(mode="after")
    def card_requires_last4(self) -> "CheckoutRequest":
        if self.payment_method == "card" and self.card_last4 is None:
            raise ValueError("cardLast4 is required when paymentMethod is 'card'")
        return self


class CheckoutResponse(ApiModel):
    transaction_id: str
    order_id: str
    subtotal: float
    tax: float
    tax_rate: float
    total: float
    currency: str
    status: Literal["approved"]


class Problem(BaseModel):
    title: str
    status: int
    detail: str
