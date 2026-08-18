<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="logo-light.svg">
  <img alt="Dovetail" src="logo-light.svg" width="96">
</picture>

# Dovetail

<a href="https://www.nuget.org/packages/Dovetail"><img alt="NuGet Version" src="https://img.shields.io/nuget/vpre/PostgreSignalR?style=for-the-badge&logo=nuget&label=%20&labelColor=gray"></a>


A source generator for implementing asynchronous pipelines of any complexity

</div>

---

Dovetail is a Roslyn source generator for building async pipelines out of small, independently testable steps. You write segments that each do one thing; Dovetail figures out which ones depend on which, runs everything that can run concurrently, and generates the orchestration code for you.

## Why Dovetail?

**Fully type-safe.** There's no string-keyed registration, no reflection-based service location, no runtime graph to misconfigure. A segment's dependencies are just its `IPipelineSegment<...>` type parameters — if nothing produces the type it needs, or two segments produce the same type, that's a compile error, not a bug you find in production.

**Helpful diagnostics.** Dovetail validates the whole segment graph at compile time: missing terminal segments, ambiguous or unresolved dependencies, cycles, segments that don't feed the result. Every failure mode has a specific diagnostic that points at the problem, right in your editor.

**Real parallelism, not just async.** Segments that don't depend on each other run concurrently automatically — you never hand-write `Task.WhenAll` or accidentally serialize independent work by awaiting too early. Segments that do depend on something simply await the task that produces it, and the generated code takes care of the rest.

## Install

```bash
dotnet add package Dovetail
```

## Quick start

A segment is any class implementing `IPipelineSegment<TResult>` (or the multi-input generic variants, up to eight inputs). Its inputs and result are ordinary types — no interfaces or base classes required on them.

```csharp
public class ItemInfoSegment(IDataRepo repo) : IPipelineSegment<int, ItemInfo>
{
    public Task<ItemInfo> RunAsync(int itemId, CancellationToken ct) =>
        repo.GetInfoAsync(itemId, ct);
}

public class ItemPriceSegment(IPriceService prices) : IPipelineSegment<ItemInfo, ItemPrice>
{
    public Task<ItemPrice> RunAsync(ItemInfo info, CancellationToken ct) =>
        prices.GetCurrentPriceAsync(info.Sku, ct);
}

public class ItemImagesSegment(ICmsService cms) : IPipelineSegment<ItemInfo, ItemImages>
{
    public Task<ItemImages> RunAsync(ItemInfo info, CancellationToken ct) =>
        cms.GetImagesAsync(info.Slug, ct);
}

public class ItemAssembler : IPipelineSegment<ItemInfo, ItemPrice, ItemImages, ItemModel>
{
    public Task<ItemModel> RunAsync(ItemInfo info, ItemPrice price, ItemImages images, CancellationToken ct) =>
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

That's it — Dovetail generates `ExecuteAsync`:

```csharp
var pipeline = new ItemPipeline(infoSegment, priceSegment, imagesSegment, assembler);
ItemModel model = await pipeline.ExecuteAsync(itemId, cancellationToken);
```

## How it works

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
            await info.RunAsync(input, linkedToken).ConfigureAwait(false);

        async Task<ItemPrice> PriceAsync() =>
            await price.RunAsync(await infoTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);

        async Task<ItemImages> ImagesAsync() =>
            await images.RunAsync(await infoTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);

        async Task<ItemModel> AssemblerAsync() =>
            await assembler.RunAsync(
                await infoTask.ConfigureAwait(false),
                await priceTask.ConfigureAwait(false),
                await imagesTask.ConfigureAwait(false),
                linkedToken).ConfigureAwait(false);
    }
}
```

(Simplified for readability — the generator fully qualifies every type it emits.)

## Diagnostics

Dovetail validates the segment graph at compile time and reports one of the following instead of generating broken code:

| ID | Meaning |
|---|---|
| DOVE001 | The pipeline type must be `partial`. |
| DOVE002 | The pipeline type must implement `IPipeline<TResult>` or `IPipeline<TInput, TResult>`. |
| DOVE003 | A `[Segment]` parameter's type must implement exactly one `IPipelineSegment<...>` interface. |
| DOVE004 | No segment produces the pipeline's result type. |
| DOVE005 | Two or more segments produce the same type. |
| DOVE006 | A segment's input isn't produced by any other segment or the pipeline's own input. |
| DOVE007 | The segments form a dependency cycle. |
| DOVE008 | A segment's result is never used, directly or transitively, by the segment producing the pipeline's result. |

## Requirements

- The package targets `netstandard2.0`, so it can be referenced from almost any .NET project.
- The pipeline declaration syntax above (primary constructors, semicolon-bodied classes) needs C# 12 or later — set `<LangVersion>` accordingly if your project targets an older one.

## Project layout

- **Dovetail** — the public API (`IPipeline`, `IPipelineSegment`, `SegmentAttribute`) and the source generator, shipped in a single package.
- **Dovetail.Tests** — xUnit v3 tests, including end-to-end tests that compile generated pipelines and run them.

## Status

Dovetail is early. The generator and its diagnostics are covered by tests, but the API may still change before a 1.0 release.
