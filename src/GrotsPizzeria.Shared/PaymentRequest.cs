namespace GrotsPizzeria.Shared;

public sealed record PaymentRequest(string Method, string? CardLast4);
