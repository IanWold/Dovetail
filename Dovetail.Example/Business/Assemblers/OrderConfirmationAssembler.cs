namespace Dovetail.Example.Business;

internal class OrderConfirmationAssembler : IPipelineSegment<OrderDetails, CustomerProfile, PaymentStatus, ShipmentTrackingInfo, OrderConfirmation>
{
    public Task<OrderConfirmation> ExecuteAsync(
        OrderDetails order,
        CustomerProfile customer,
        PaymentStatus payment,
        ShipmentTrackingInfo shipment,
        CancellationToken ct
    ) =>
        Task.FromResult(new OrderConfirmation(order, customer, payment, shipment));
}
