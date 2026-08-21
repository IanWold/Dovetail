using Dovetail;

namespace Dovetail.Example.Business;

// A real multi-level chain: OrderId -> OrderDetails -> (extracted) UserId -> CustomerProfile,
// with CustomerProfilePipeline reused here as a whole nested segment.
internal partial class OrderConfirmationPipeline(
    [Segment] OrderDetailsSegment order,
    [Segment] OrderCustomerIdSegment customerId,
    [Segment] CustomerProfilePipeline customer,
    [Segment] PaymentStatusSegment payment,
    [Segment] ShipmentTrackingSegment shipment,
    [Segment] OrderConfirmationAssembler assembler
) : IPipeline<OrderId, OrderConfirmation>;
