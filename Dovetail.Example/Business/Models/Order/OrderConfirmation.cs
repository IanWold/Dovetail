namespace Dovetail.Example.Business;

public record OrderConfirmation(
    OrderDetails Order,
    CustomerProfile Customer,
    PaymentStatus Payment,
    ShipmentTrackingInfo Shipment
);
