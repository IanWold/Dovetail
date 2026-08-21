using Dovetail;

namespace Dovetail.Example.Business;

/// <summary>
/// Demonstrates more complex scenario with a pipeline reused as a segment and static segment transforming order details to uesr id
/// </summary>
internal partial class OrderConfirmationPipeline(
    [Segment] OrderDetailsSegment order,
    [Segment] CustomerProfilePipeline customer,
    [Segment] PaymentStatusSegment payment,
    [Segment] ShipmentTrackingSegment shipment
) : IPipeline<OrderId, OrderConfirmation>
{
    [Segment]
    private static UserId OrderDetailsToUserId(OrderDetails order) => order.UserId;

    [Segment]
    private static OrderConfirmation Assemble(OrderDetails order, CustomerProfile customer, PaymentStatus payment, ShipmentTrackingInfo shipment) =>
        new(order, customer, payment, shipment);
}
