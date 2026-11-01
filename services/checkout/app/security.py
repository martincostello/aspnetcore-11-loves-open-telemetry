"""Service-to-service security: validates the JWT bearer token sent by callers."""

import os
from dataclasses import dataclass
from typing import Annotated

import jwt
from fastapi import Depends
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

MIN_KEY_BYTES = 32
LEEWAY_SECONDS = 30


@dataclass(frozen=True)
class JwtSettings:
    signing_key: str
    issuer: str
    audience: str

    @staticmethod
    def from_environment() -> "JwtSettings":
        key = os.environ.get("JWT_SIGNING_KEY", "")
        if len(key.encode()) < MIN_KEY_BYTES:
            raise RuntimeError(f"JWT_SIGNING_KEY must be set and be at least {MIN_KEY_BYTES} bytes long.")
        return JwtSettings(
            signing_key=key,
            issuer=os.environ.get("JWT_ISSUER", "grots-web"),
            audience=os.environ.get("JWT_AUDIENCE", "grots-pizzeria"),
        )


# Loaded when the module is imported, so the service refuses to start without a valid key.
settings = JwtSettings.from_environment()

bearer = HTTPBearer(auto_error=False)


class Unauthorized(Exception):
    """Raised when the caller did not present a valid token."""


def require_token(
    credentials: Annotated[HTTPAuthorizationCredentials | None, Depends(bearer)],
) -> dict:
    if credentials is None:
        raise Unauthorized()

    try:
        return jwt.decode(
            credentials.credentials,
            settings.signing_key,
            algorithms=["HS256"],
            issuer=settings.issuer,
            audience=settings.audience,
            leeway=LEEWAY_SECONDS,
            options={"require": ["exp", "iss", "aud"]},
        )
    except jwt.InvalidTokenError:
        raise Unauthorized() from None
