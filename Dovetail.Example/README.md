# Dovetail.Example

A small, self-contained storefront BFF (backend-for-frontend) demonstrating Dovetail in a setting closer to real use than the snippets in the main [README](../README.md). Everything runs in-process against seeded, in-memory "services" — no network access, no API keys, no external dependencies. Just `dotnet run`.

## Running it

```
dotnet run --project Dovetail.Example
```

The app listens on `http://localhost:5080`. `GET /` lists the available endpoints.

## Layout

Three top-level directories, a straightforward top-down dependency chain:

```
Presentation/       Program.cs — the HTTP host and the composition root
Business/            Ids.cs, Models/, Pipelines/ — domain types, business rules,
                     and Dovetail's pipelines/segments themselves
Infrastructure/      pure data access: its own records, its own interfaces,
                     zero references to anything in Business
```

`Infrastructure` knows nothing about `Business` — it exposes its own record types (`ProductRecord`, `OrderRecord`, `PaymentRecord`, ...) shaped like whatever a real downstream system would actually return (e.g. `PaymentRecord.StatusCode` is a raw string, not `Business`'s `PaymentState` enum), keyed by plain `int`/`string` rather than `Business`'s `Sku`/`UserId`/etc. wrapper types. It contains *only* lookups — no discount percentages, no stock-level thresholds, no promotion eligibility, no shipping-cost tiers. Those are business rules, and they live in `Business/Pipelines`, computed after mapping an Infrastructure record into a `Business` domain type. `Business` is therefore the layer that depends downward on `Infrastructure` (to fetch data) as well as up on nothing — it owns the conversion from Infrastructure's wire-shaped records into its own domain model. `Presentation`'s `Program.cs` is the composition root: the only place that maps each Infrastructure interface to its concrete implementation (`AddSingleton<IProductCatalogDataAccess, ProductCatalogDataAccess>()`) and where `AddPipelines()` registers every segment and pipeline Dovetail found.

A concrete example of the split: `PricingSegment` (in `Business/Pipelines/ProductDetailPipeline.cs`) computes a category-based discount purely from a `ProductInfo` it already has — no Infrastructure dependency at all, because there's no actual data access involved, just a rule. `InventorySegment`, by contrast, really does need a fetched value (the raw unit count from `IInventoryDataAccess`), but the *threshold* for what counts as "low stock" is applied in `Business`, on top of that raw number — `Infrastructure` only ever hands back the count.

## What's in here

Four pipelines, three of them exposed as endpoints:

- **`ProductDetailPipeline`** — `GET /products/{sku}` — fans out to catalog, pricing, inventory, reviews, and recommendations. Pricing and recommendations both depend on the catalog lookup's result rather than the raw SKU, so it's a real dependency graph, not just a flat fan-out.
- **`CartSummaryPipeline`** — `GET /cart/{userId}/{cartId}` — a two-input pipeline (`IPipeline<UserId, CartId, TResult>`). Its cart-contents segment needs both inputs directly; its promotions and shipping segments each mix a pipeline input with another segment's result in the same parameter list.
- **`OrderConfirmationPipeline`** — `GET /orders/{orderId}` — a multi-level chain: `OrderId` → order details → an extracted `UserId` → customer profile, alongside payment and shipment lookups.
- **`CustomerProfilePipeline`** — not exposed directly, but implements both `IPipeline<UserId, CustomerProfile>` and the matching `IPipelineSegment<UserId, CustomerProfile>` shape, built from two small segments: `CustomerAccountSegment` and `LoyaltyStatusSegment`.

That last pipeline is reused two different ways, both from the same two segment classes:

- **As a whole**, inside `OrderConfirmationPipeline` — wired in as `[Segment] CustomerProfilePipeline customer`, the same pipeline-as-segment composition shown in the main README.
- **As its parts**, inside `CartSummaryPipeline` — `CustomerAccountSegment` and `LoyaltyStatusSegment` (the exact same classes, not copies) are wired in directly, since the cart page wants the account and loyalty data as two separate fields rather than pre-bundled.

Run the app and hit both `/cart/1/1` and `/orders/1` with the console trace visible: `CartSummaryPipeline.account` / `CartSummaryPipeline.loyalty` show up as direct children of the cart pipeline, while the order pipeline's trace shows the identically-named segments nested one level deeper, under `CustomerProfilePipeline.*` — same two segment classes, two different pipelines, two different ways of assembling them.

Every ID (`Sku`, `UserId`, `CartId`, `OrderId`) is its own wrapper type around an `int` rather than a bare primitive — see [`Business/Ids.cs`](Business/Ids.cs). Dovetail matches segment inputs purely by type, so without that, a `UserId` and an `OrderId` would be indistinguishable to the generator.

### Things worth trying

| Request | What it shows |
|---|---|
| `GET /products/1` | A normal, healthy fan-out/fan-in. |
| `GET /products/7` | The warehouse feed for this one SKU always times out. `InventorySegment` catches it and degrades to `Unknown` stock instead of failing the whole page — the [optional/fallback pattern](../README.md#error-handling) from the main README. Watch the console trace: `inventory` completes cleanly, no error. |
| `GET /products/999` | A SKU that doesn't exist. This one *isn't* caught, so the whole pipeline fails — watch the console trace show every sibling segment get cancelled and drained before the 404 comes back. |
| `GET /cart/1/1` | A full cart with promotions and standard shipping. |
| `GET /cart/2/2` | An empty cart — every segment still runs, just against zero items. |
| `GET /orders/1` | A shipped order. |
| `GET /orders/2` | A paid order that hasn't shipped yet — ordinary data (`hasShipped: false`), not a failure. |
| `GET /cart/1/1` then `GET /orders/1` | Compare the console traces: `account`/`loyalty` appear directly under `CartSummaryPipeline`, but nested under `CustomerProfilePipeline` when reached through `OrderConfirmationPipeline` — the same two segments, reused two different ways. |

### Tracing

The app wires up a plain `System.Diagnostics.ActivityListener` (see [`Infrastructure/Tracing.cs`](Infrastructure/Tracing.cs)) that prints every `dovetail.pipeline` / `dovetail.segment` activity straight to the console as it runs, with nesting and duration. No OpenTelemetry package required — this is exactly what Dovetail emits when *anything* is listening. Watch the console while making requests to see which segments ran concurrently and how long each took.
