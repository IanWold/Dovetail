# Dovetail.Example

This example is a small, self-contained storefront BFF (backend-for-frontend) demonstrating Dovetail in a setting closer to real use than the snippets in the main [README](../README.md). Everything runs in-process against seeded, in-memory "services".

## Running

```
dotnet run --project Dovetail.Example
```

The app listens on `http://localhost:5080`. `GET /` lists the available endpoints.

## Layout

This is built akin to a standard n-tier API, the standard layers are top-level directories:

* [Presentation](Presentation): API endpoints
* [Business](Business): Business logic, including the segments and pipelines
* [Infrastructure](Infrastructure): Simulated data access

## Pipelines

This project contains four pipelines, three of them exposed as endpoints:

* **`ProductDetailPipeline`:** `GET /products/{sku}`: fans out to catalog, pricing, inventory, reviews, and recommendations. Pricing and recommendations both depend on the catalog lookup's result rather than the raw SKU, showing a more complicated dependency graph.
* **`CartSummaryPipeline`:** `GET /cart/{userId}/{cartId}`: a two-input pipeline (`IPipeline<UserId, CartId, TResult>`). Its cart-contents segment needs both inputs directly; its promotions and shipping segments each mix a pipeline input with another segment's result in the same parameter list.
* **`OrderConfirmationPipeline`:** `GET /orders/{orderId}`: a larger, multi-level chain.
* **`CustomerProfilePipeline`:** not exposed directly, but implements both `IPipeline<UserId, CustomerProfile>` and the matching `IPipelineSegment<UserId, CustomerProfile>` shape, built from two small segments: `CustomerAccountSegment` and `LoyaltyStatusSegment`.

All pipelines demonstrate the pattern of having the final `[Segment]` in each one being a `private static` method declared right in the pipeline's own body (see the [main README](../README.md#static-segment-methods)). `OrderConfirmationPipeline` uses the same mechanism for the `OrderId` -> `UserId` transformation allowing the use of the `CustomerProfilePipeline`.

Every ID (`Sku`, `UserId`, `CartId`, `OrderId`) is its own wrapper type around an `int` rather than a bare primitive (see [`Business/Ids.cs`](Business/Ids.cs)). Dovetail matches segment inputs purely by type, so without that, a `UserId` and an `OrderId` would be indistinguishable to the generator.

### Things worth trying

| Request | What it shows |
|---|---|
| `GET /products/1` | A normal, healthy fan-out/fan-in. |
| `GET /products/7` | The warehouse feed for this one SKU always times out. `InventorySegment` catches it and degrades to `Unknown` stock instead of failing the whole page (the [optional/fallback pattern](../README.md#error-handling)) from the main README. Watch the console trace: `inventory` completes cleanly, no error. |
| `GET /products/999` | A SKU that doesn't exist. This one _isn't_ caught, so the whole pipeline fails. Watch the console trace show every sibling segment get cancelled and drained before the 404 comes back. |
| `GET /cart/1/1` | A full cart with promotions and standard shipping. |
| `GET /cart/2/2` | An empty cart. Every segment still runs, just against zero items. |
| `GET /orders/1` | A shipped order. |
| `GET /orders/2` | A paid order that hasn't shipped yet. |
| `GET /cart/1/1` then `GET /orders/1` | Compare the console traces: `account`/`loyalty` appear directly under `CartSummaryPipeline`, but nested under `CustomerProfilePipeline` when reached through `OrderConfirmationPipeline`, showing the same two segments reused two different ways. |

### Tracing

The app wires up a plain `System.Diagnostics.ActivityListener` (see [`Infrastructure/Tracing.cs`](Infrastructure/Tracing.cs)) that prints every `dovetail.pipeline` / `dovetail.segment` activity straight to the console as it runs, with nesting and duration. This is what Dovetail emits when _anything_ is listening. Watch the console while making requests to see which segments ran concurrently and how long each took.
