using Dovetail.Example.Business;

namespace Dovetail.Example.Presentation;

internal static class OrderEndpoints
{
    extension(WebApplication app)
    {
        internal void MapOrderEndpoints()
        {
            app.MapGet("/orders/{orderId:int}", async (int orderId, OrderConfirmationPipeline pipeline, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await pipeline.ExecuteAsync(new OrderId(orderId), ct));
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(ex.Message);
                }
            });
        }
    }
}