import os

# Must be set before `app` is imported: the service refuses to start without a signing key.
os.environ.setdefault("JWT_SIGNING_KEY", "unit-test-signing-key-0123456789-abcdef")
