"""Request and response models for the checkout API."""

from decimal import Decimal
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel


class ApiModel(BaseModel):
    """Base model that (de)serializes camelCase JSON."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class CheckoutRequest(ApiModel):
    """The payment to take for an order."""

    order_id: Annotated[str, Field(min_length=1, max_length=64, description="The order being paid for.", examples=["ord_1a2b3c4d5e6f"])]
    amount: Annotated[
        Decimal,
        Field(gt=0, le=10_000, decimal_places=2, description="What to charge before tax, in the currency.", examples=[19.0]),
    ]
    currency: Annotated[
        str,
        Field(pattern=r"^[A-Z]{3}$", description="The ISO 4217 code of the currency of the amount: the one the menu prices are in.", examples=["GBP"]),
    ]
    payment_method: Annotated[Literal["card", "cash"], Field(description="How the customer is paying.")]
    card_last4: (
        Annotated[
            str,
            Field(
                pattern=r"^\d{4}$",
                description="The last 4 digits of the card: required for card payments. `0000` is always declined.",
                examples=["4242"],
            ),
        ]
        | None
    ) = None

    @model_validator(mode="after")
    def card_requires_last4(self) -> "CheckoutRequest":
        if self.payment_method == "card" and self.card_last4 is None:
            raise ValueError("cardLast4 is required when paymentMethod is 'card'")
        return self


class CheckoutResponse(ApiModel):
    """The receipt for an approved payment."""

    transaction_id: Annotated[str, Field(examples=["txn_0123456789ab"])]
    order_id: str
    subtotal: Annotated[float, Field(description="The amount, before tax.", examples=[19.0])]
    tax: Annotated[float, Field(description="The tax charged, to the nearest penny.", examples=[1.52])]
    tax_rate: Annotated[float, Field(description="The rate the tax was charged at, as a fraction: 0.08 is 8%.", examples=[0.08])]
    total: Annotated[float, Field(description="What was charged: the subtotal plus the tax.", examples=[20.52])]
    currency: Annotated[str, Field(description="The currency of the amounts: the one that was asked for.", examples=["GBP"])]
    status: Literal["approved"]


class Problem(BaseModel):
    """What every error response looks like."""

    title: Annotated[str, Field(examples=["Payment declined"])]
    status: Annotated[int, Field(examples=[402])]
    detail: Annotated[str, Field(examples=["The payment was declined by the issuer."])]
