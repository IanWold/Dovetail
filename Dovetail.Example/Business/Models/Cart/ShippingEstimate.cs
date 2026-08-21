namespace Dovetail.Example.Business;

public record ShippingEstimate(
    string Method,
    decimal Cost,
    int EstimatedDays
);
