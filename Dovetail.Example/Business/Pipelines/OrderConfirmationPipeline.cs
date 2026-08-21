using Dovetail;

namespace Dovetail.Example.Business;

// A real multi-level chain: OrderId -> OrderDetails -> (extracted) UserId -> CustomerProfile,
// with CustomerProfilePipeline reused here as a whole nested segment.
internal partial class OrderConfirmationPipeline(
    [Segment] OrderDetailsSegment order,
    [Segment] CustomerProfilePipeline customer,
    [Segment] PaymentStatusSegment payment,
    [Segment] ShipmentTrackingSegment shipment
) : IPipeline<OrderId, OrderConfirmation>
{
    [Segment]
    private static UserId ExtractCustomerId(OrderDetails order) => order.UserId;

    [Segment]
    private static OrderConfirmation Assemble(OrderDetails order, CustomerProfile customer, PaymentStatus payment, ShipmentTrackingInfo shipment) =>
        new(order, customer, payment, shipment);
}
