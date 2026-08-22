namespace Dovetail.Example.Presentation;

internal static class IndexEndpoints
{
    extension (WebApplication app)
    {
        internal void MapIndexEndpoints()
        {
            app.MapGet("/", static () => Results.Ok(new
            {
                Message = "Dovetail e-commerce example: a BFF aggregating several in-memory services into three pipelines.",
                Try = new[]
                {
                    "GET /products/{sku}          e.g. /products/1        (try /products/7 for the inventory fallback, /products/999 for a real 404)",
                    "GET /cart/{userId}/{cartId}  e.g. /cart/1/1           (try /cart/2/2 for an empty cart)",
                    "GET /orders/{orderId}        e.g. /orders/1           (try /orders/2 for an order that hasn't shipped yet)",
                }
            }));
        }
    }
}