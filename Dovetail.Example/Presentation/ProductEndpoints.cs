using Dovetail.Example.Business;

namespace Dovetail.Example.Presentation;

internal static class ProductEndpoints
{
    extension (WebApplication app)
    {
        internal void MapProductEndpoints()
        {
            app.MapGet("/products/{sku:int}", async (int sku, ProductDetailPipeline pipeline, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await pipeline.ExecuteAsync(new Sku(sku), ct));
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(ex.Message);
                }
            });
        }
    }
}
