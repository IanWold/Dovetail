using Dovetail.Example.Business;

namespace Dovetail.Example.Presentation;

internal static class CartEndpoints
{
    extension (WebApplication app)
    {
        internal void MapCartEndpoints()
        {
            app.MapGet("/cart/{userId:int}/{cartId:int}", async (int userId, int cartId, CartSummaryPipeline pipeline, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await pipeline.ExecuteAsync(new UserId(userId), new CartId(cartId), ct));
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(ex.Message);
                }
            });
        }
    }
}
