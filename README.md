<div align="center">

<h1>
<img alt="Dovetail" src="logo-color.svg" height="64">
  
Dovetail
</h1>

<a href="https://www.nuget.org/packages/Dovetail"><img alt="NuGet Version" src="https://img.shields.io/nuget/vpre/Dovetail?style=for-the-badge&logo=nuget&label=%20&labelColor=gray"></a>


A source generator for implementing asynchronous pipelines of any complexity.

[Quickstart](#quickstart) • [Detailed Explanation](#detailed-explanation) • [Diagnostics](#diagnostics)

</div>

---

Dovetail is a Roslyn source generator for building async pipelines out of small, independently testable steps. You write segments that each do one thing; Dovetail figures out which ones depend on which, runs everything that can run concurrently, and generates the orchestration code for you.

## Why Dovetail?

**Compile-time correctness, helpful diagnostics:** Every dependency within the pipeline is checked when your project builds, not when a request hits production. A missing dependency, two segments producing the same type, a cycle: these are compile errors with a specific, located diagnostic, not bugs you find at 2 AM. There's no string-keyed registration, no reflection-based service location, no runtime graph to misconfigure. The type system is the only source of truth.

**Real parallelism, no boilerplate:** Segments that don't depend on each other run concurrently automatically; you never hand-write `Task.WhenAll` and you never accidentally serialize independent work by awaiting too early. Cancellation propagation and draining in-flight work when something fails are the kind of thing that's easy to get subtly wrong by hand.

**Generated code you can actually read:** `ExecuteAsync` is plain async/await — nothing you couldn't have written yourself, just correctly and without the tedium. No runtime reflection, no DI container in the hot path, nothing hidden behind the generator once your project is built.

**Lightweight and quick to set up:** One NuGet package, no forced dependencies, no base classes to inherit, no configuration files, no startup registration required. A segment is a class implementing one interface; a pipeline is a partial class with a few `[Segment]` attributes — there's nothing else standing between `dotnet add package` and a working pipeline.

### Who Dovetail Is For

Dovetail is designed particularly for composition and aggregation workflows: fanning out to several independent services or data sources and merging the results into one response. BFF-style endpoints, product-detail-page assembly, dashboard and summary views, GraphQL-style resolvers implemented over REST. Dovetail is best used for managing complexity in this domain.

In addition, Dovetail works quite well for:

* **Read-side composition in CQRS-style architectures:** Query handlers that fan out to multiple read models or caches and assemble a view model.
* **Fixed-shape async initialization sequences:** A small, static DAG of async setup steps where some branches are genuinely independent of each other.
* **Teams splitting ownership across a composed endpoint:** Segments are plain, DI-constructible classes with no shared orchestration code, so different people can own different segments without touching how they're wired together.

### Who Dovetail Is Not For

Most notably, Dovetail is for managing the complexity of aggregation logic that needs to be spread across many services. It's overkill if you have very simple use cases. That said, there are other properly complex use cases Dovetail isn't suited for:

* **Dynamic or conditional graph shapes:** The DAG is resolved entirely by compile-time type matching. There's no "run this segment only if that one says so," no step list that varies by tenant, feature flag, or runtime config. If your workflow needs conditional branching, this isn't the tool.
* **Long-running or durable workflows:** Dovetail has no persistence, no checkpointing, and no resuming after a crash. Dovetail is an in-process, single-execution composition helper, not a durable orchestrator.
* **Heavy CPU-bound work:** The concurrency model overlaps I/O waits and doesn't spread compute across cores. If your "segments" are actually CPU-heavy, this won't help beyond what `async`/`await` already gives you.
* **Workflows that need multi-error aggregation as a first-class concern:** Only one exception surfaces per execution (see [Concurrent Failures](#concurrent-failures)). Dovetail does support working around this by returning errors as results from segments, but it is the wrong default if your domain fundamentally wants "tell me everything that failed."
* **Streaming or incremental results:** One execution produces one final result; Dovetail does not support `IAsyncEnumerable`, nor progressive rendering as branches complete.

## Quickstart

A segment is any class implementing `IPipelineSegment<TResult>` (or the multi-input generic variants, up to eight inputs). Its inputs and result are ordinary types — no interfaces or base classes required on them.

```csharp
public class ItemInfoSegment(IDataRepo repo) : IPipelineSegment<int, ItemInfo>
{
    public Task<ItemInfo> ExecuteAsync(int itemId, CancellationToken ct) =>
        repo.GetInfoAsync(itemId, ct);
}

public class ItemPriceSegment(IPriceService prices) : IPipelineSegment<ItemInfo, ItemPrice>
{
    public Task<ItemPrice> ExecuteAsync(ItemInfo info, CancellationToken ct) =>
        prices.GetCurrentPriceAsync(info.Sku, ct);
}

public class ItemImagesSegment(ICmsService cms) : IPipelineSegment<ItemInfo, ItemImages>
{
    public Task<ItemImages> ExecuteAsync(ItemInfo info, CancellationToken ct) =>
        cms.GetImagesAsync(info.Slug, ct);
}

public class ItemAssembler : IPipelineSegment<ItemInfo, ItemPrice, ItemImages, ItemModel>
{
    public Task<ItemModel> ExecuteAsync(ItemInfo info, ItemPrice price, ItemImages images, CancellationToken ct) =>
        Task.FromResult(new ItemModel(info, price, images));
}
```

Declare the pipeline as a partial class, attaching `[Segment]` to a constructor parameter for each step:

```csharp
public partial class ItemPipeline(
    [Segment] ItemInfoSegment info,
    [Segment] ItemPriceSegment price,
    [Segment] ItemImagesSegment images,
    [Segment] ItemAssembler assembler
) : IPipeline<int, ItemModel>;
```

Like `IPipelineSegment<...>`, `IPipeline<...>` comes in variants up to eight inputs (`IPipeline<T1, ..., T8, TResult>`). Any segment input that isn't produced by another segment is matched against the pipeline's own declared input types, so a multi-input pipeline just spreads those across its segments however the dependency graph calls for.

That's it — Dovetail generates `ExecuteAsync`:

```csharp
var pipeline = new ItemPipeline(infoSegment, priceSegment, imagesSegment, assembler);
ItemModel model = await pipeline.ExecuteAsync(itemId, cancellationToken);
```

## Detailed Explanation

Dovetail reads each segment's `IPipelineSegment<...>` interface to learn its input and result types, then wires the pipeline together purely by matching those types:

- A segment's input is satisfied by the pipeline's own input, or by another segment whose result matches — no other segment may produce the same type.
- The segment whose result matches the pipeline's own result type becomes the terminal step.
- The generated `ExecuteAsync` starts every segment concurrently, awaits the terminal step, and returns its result.
- If anything fails, Dovetail cancels a shared token and waits for the rest of the in-flight segments to unwind before rethrowing — nothing is left running or unobserved.

Roughly, the pipeline above generates:

```csharp
public partial class ItemPipeline
{
    public async Task<ItemModel> ExecuteAsync(int input, CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var linkedToken = cts.Token;

        var infoTask = InfoAsync();
        var priceTask = PriceAsync();
        var imagesTask = ImagesAsync();
        var assemblerTask = AssemblerAsync();

        try
        {
            return await assemblerTask.ConfigureAwait(false);
        }
        catch
        {
            cts.Cancel();
            try { await Task.WhenAll(infoTask, priceTask, imagesTask).ConfigureAwait(false); }
            catch { }
            throw;
        }

        async Task<ItemInfo> InfoAsync() =>
            await info.ExecuteAsync(input, linkedToken).ConfigureAwait(false);

        async Task<ItemPrice> PriceAsync() =>
            await price.ExecuteAsync(await infoTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);

        async Task<ItemImages> ImagesAsync() =>
            await images.ExecuteAsync(await infoTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);

        async Task<ItemModel> AssemblerAsync() =>
            await assembler.ExecuteAsync(
                await infoTask.ConfigureAwait(false),
                await priceTask.ConfigureAwait(false),
                await imagesTask.ConfigureAwait(false),
                linkedToken).ConfigureAwait(false);
    }
}
```

(Simplified for readability — the generator fully qualifies every type it emits.)

### Constructors

Both primary and conventional constructors work:

```csharp
public partial class ItemPipeline : IPipeline<int, ItemModel>
{
    private readonly ItemInfoSegment _info;
    private readonly ItemPriceSegment _price;

    public ItemPipeline([Segment] ItemInfoSegment info, [Segment] ItemPriceSegment price)
    {
        _info = info;
        _price = price;
    }
}
```

Here, Dovetail resolves each `[Segment]` parameter's value by finding the one field or property on the type whose declared type matches the parameter's: `_info` and `_price` above, regardless of their names. If no member matches, or more than one does, that's a compile error (DOVE010/DOVE011) rather than something you'd discover at runtime, so name your backing members however you like.

### Dependency Injection

If your project references `Microsoft.Extensions.DependencyInjection`, Dovetail also generates an `AddPipelines()` extension method:

```csharp
services.AddPipelines();
```

This registers every segment and pipeline it finds anywhere in your compilation by their concrete type. With that in place, pipelines and segments alike can be injected:

```csharp
public class ItemsController(ItemPipeline pipeline)
{
    public Task<ItemModel> GetAsync(int itemId, CancellationToken ct) =>
        pipeline.ExecuteAsync(itemId, ct);
}
```

`AddPipelines()` is only generated when the DI package is actually referenced. This keeps Dovetail from having a dependency on it, so projects that don't use DI are unaffected.

### Chaining Pipelines

`IPipelineSegment<...>` and `IPipeline<...>` share the same method name (`ExecuteAsync`) wherever their shapes line up (the same input types, in the same order, and the same result type). This means a pipeline can double as a segment of another pipeline by implementing both interfaces:

```csharp
public partial class ItemInfoPipeline(
    [Segment] SomeSegment a,
    [Segment] AnotherSegment b
) : IPipeline<int, ItemInfo>, IPipelineSegment<int, ItemInfo>;
```

Since both interfaces declare an identical `Task<ItemInfo> ExecuteAsync(int, CancellationToken)`, the one `ExecuteAsync` Dovetail already generates for `IPipeline<int, ItemInfo>` satisfies `IPipelineSegment<int, ItemInfo>` too, so there's nothing extra for you to write. `ItemInfoPipeline` can now be called directly, or used as `[Segment] ItemInfoPipeline info` inside a larger pipeline, and either way it's the same generated method doing the work.

This only applies when the shapes match. A type that implements `IPipelineSegment<...>` without a matching `IPipeline<...>` — the ordinary case still needs its `ExecuteAsync` hand-written, exactly like any other segment.

### Tracing

If `System.Diagnostics.DiagnosticSource` is available, Dovetail wraps the pipeline and every segment in an `Activity`, so you can see exactly which segment was slow without adding anything yourself:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Dovetail"));
```

Every pipeline's `ExecuteAsync` starts an activity named `"{Pipeline}.ExecuteAsync"`, and each segment gets its own nested `"{Pipeline}.{segment}"` activity, nested such that a segment's span starts while it's still the ambient activity from the pipeline that kicked it off. Each activity carries `dovetail.pipeline`, and segment activities also carry `dovetail.segment` (its role in this pipeline) and `dovetail.segment.type` (its concrete class). If a segment throws, its activity is marked `Error` before the exception propagates.

Like the dependency injection generation, the tracing logic is only generated when `System.Diagnostics.DiagnosticSource` is available; Dovetail doesn't depend on it. When the namespace is unavailable, `ExecuteAsync` is generated exactly as if tracing didn't exist.

Note that the tracing calls are still nearly free if nothing's listening: `Activity.StartActivity` returns `null` without a registered listener, and every call after it is a `?.`-guarded no-op.

## Architectural Considerations

### Error Handling

Segments are not sandboxed within a pipeline, so an exception from one segment fails the entire pipeline. It was deliberately chosen that Dovetail has no concept of an "optional" segment. If a segment should degrade gracefully instead of failing the whole pipeline, catch its own failure and return a fallback value:

```csharp
public class ItemImagesSegment(ICmsService cms) : IPipelineSegment<ItemInfo, ItemImages>
{
    public async Task<ItemImages> ExecuteAsync(ItemInfo info, CancellationToken ct)
    {
        try
        {
            return await cms.GetImagesAsync(info.Slug, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ItemImages([]);
        }
    }
}
```

One thing worth being careful about: don't catch `OperationCanceledException` this way. If the pipeline is actually being cancelled, that should propagate normally rather than being swallowed into a fallback value.

### Concurrent Failures

When two or more segments fail at the same time, only **one** exception ever reaches the caller of `ExecuteAsync`, not an `AggregateException` containing failures from multiple segments. The generated code's `try`/`catch` only observes the exception that surfaces through the terminal segment's own await chain, and sibling branches that fail independently of that chain are cancelled and drained via `Task.WhenAll(...)` inside a `catch { }` that discards their exceptions.

In practice: if two unrelated segments both throw at once, you'll see whichever one happened to be part of the chain the terminal segment was awaiting when it faulted, not both. If you need visibility into every failure rather than just the one that propagates, [tracing](#tracing) marks *every* failing segment's own activity `Error`, regardless of which single exception makes it back to the caller.

### Testing Segments

Segments are plain classes with constructor-injected dependencies so you can test them exactly like you'd test any other class, with whatever approach you already use:

```csharp
public class ItemPriceSegmentTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsCurrentPrice()
    {
        var segment = new ItemPriceSegment(new FakePriceService(19.99m));

        var result = await segment.ExecuteAsync(new ItemInfo { Sku = "SKU-1" }, CancellationToken.None);

        Assert.Equal(19.99m, result.Amount);
    }

    private class FakePriceService(decimal price) : IPriceService
    {
        public Task<Price> GetCurrentPriceAsync(string sku, CancellationToken ct) =>
            Task.FromResult(new Price(price));
    }
}
```

`ExecuteAsync` itself isn't something you typically need to unit test — Dovetail generates it, and its correctness (dependency resolution, concurrency, failure handling) is covered by Dovetail's own test suite. Test each segment's logic in isolation, and integration-test the assembled pipeline the same way you'd test anything else built on `IPipeline<...>`.

## Diagnostics

Dovetail validates the segment graph at compile time and reports one of the following instead of generating broken code:

| ID | Meaning |
|---|---|
| DOVE001 | The pipeline type must be `partial`. |
| DOVE002 | The pipeline type must implement exactly one `IPipeline<...>` interface. |
| DOVE003 | A `[Segment]` parameter's type must implement exactly one `IPipelineSegment<...>` interface. |
| DOVE004 | No segment produces the pipeline's result type. |
| DOVE005 | Two or more segments produce the same type. |
| DOVE006 | A segment's input isn't produced by any other segment or one of the pipeline's own input types. |
| DOVE007 | The segments form a dependency cycle. |
| DOVE008 | A segment's result is never used, directly or transitively, by the segment producing the pipeline's result. |
| DOVE009 | The pipeline declares the same input type more than once. |
| DOVE010 | A `[Segment]` parameter on a non-primary constructor has no field or property of its type to read its value from. |
| DOVE011 | A `[Segment]` parameter on a non-primary constructor has more than one field or property of its type — Dovetail can't tell which one to use. |
