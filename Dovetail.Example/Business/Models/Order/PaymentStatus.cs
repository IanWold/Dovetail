namespace Dovetail.Example.Business;

public record PaymentStatus(
    PaymentState State,
    string? TransactionId
);
