namespace Dovetail.Example.Infrastructure;

internal record PaymentRecord(
    string StatusCode,
    string? TransactionId
);
