<div align="center">

<h1>
<img alt="Dovetail" src="Dovetail/icon.svg" height="64">

Dovetail
</h1>

[![Dovetail Nuget Version](https://img.shields.io/nuget/v/Dovetail?style=for-the-badge&logo=nuget&label=Dovetail)](https://www.nuget.org/packages/Dovetail)
[![Dovetail.Report Nuget Version](https://img.shields.io/nuget/v/Dovetail.Report?style=for-the-badge&logo=nuget&label=Dovetail.Report)](https://www.nuget.org/packages/Dovetail.Report)

Easily build fully type-checked, concurrent-running pipelines from composable segments.

[Quickstart](#-quickstart) • [Diagnostics](#-diagnostics) • [Reporting](#-dovetailreport-tool) • [Example](Dovetail.Example)

</div>

---

Dovetail is a Roslyn source generator for building async pipelines out of composable, independent segments. You write segments that each do one thing; Dovetail reads their input and result types, works out which ones depend on which, and generates one `ExecuteAsync` that runs everything concurrently except where one segment depends on the other.

```csharp
public partial class UserSummaryPipeline(
    [Segment] IPipelineSegment<UserId, User> user,
    [Segment] IPipelineSegment<User, Permissions> permissions,
    [Segment] IPipelineSegment<User, RecentActivity> activity,
    [Segment] IPipelineSegment<Permissions, RecentActivity, UserSummary> summary
) : IPipeline<UserId, UserSummary>;
```

Dovetail extensively checks your pipelines with clear, helpful diagnostic messages, ensuring issues are caught at compile time.

## 📖 Table of Contents

* **[Why Dovetail?](#%EF%B8%8F-why-dovetail)**
    * [Who Dovetail Is For](#-who-dovetail-is-for) | [Who Dovetail Is Not For](#-who-dovetail-is-not-for) 
* **[Quickstart](#-quickstart)**
    * [Report Tool](#-report-tool) 
* **[Detailed Explanation](#-detailed-explanation)**
    * [Dependency Injection](#-dependency-injection) | [Constructors](#%EF%B8%8F-constructors) | [Static Segment Methods](#%EF%B8%8F-static-segment-methods) | [Managing Concurrency](#-managing-concurrency) | [Generics](#-generics) | [Pipelines-as-Segments](#-pipelines-as-segments) | [Tracing](#-tracing)
* **[Debugging](#-debugging)**
    * [Reading the Generated Source](#-reading-the-generated-source) | [Concurrency and Exceptions](#%EF%B8%8F-concurrency-and-exceptions) | [DI Lifetimes](#-di-lifetimes) | [Isolating Failures](#-isolating-failures) | [Generated Diagrams](#%EF%B8%8F-generated-diagrams)
* **[Dovetail.Report Tool](#-dovetailreport-tool)**
    * [Running](#-running) | [Output](#-output) | [Using in CI](#%EF%B8%8F-using-in-ci)
* **[Architectural Considerations](#%EF%B8%8F-architectural-considerations)**
    * [Concurrency](#-concurrency) | [Endomorphism](#%EF%B8%8F-endomorphism) | [Exception Handling](#-exception-handling) | [Collecting Multiple Errors](#-collecting-multiple-errors) | [Conditional Execution](#%EF%B8%8F-conditional-execution) | [Testing](#-testing)
* **[Diagnostics](#-diagnostics)**

## 🕊️ Why Dovetail?

**Compile-time correctness, helpful diagnostics:** Every dependency within the pipeline is checked when your project builds, not when a request hits production. There's no string-keyed registration, no reflection-based service location, no runtime graph to misconfigure. The type system is the only source of truth.

**Real parallelism, no boilerplate:** Segments that don't depend on each other run concurrently automatically; you never hand-write `Task.WhenAll` and you never accidentally serialize independent work by awaiting too early. Cancellation propagation, draining in-flight work when something fails, and bounding concurrency are all the kind of thing that's easy to get subtly wrong by hand.

**Generated code you can actually read:** `ExecuteAsync` is plain async/await, nothing you couldn't have written yourself, just correctly and without the tedium. No runtime reflection, no DI container in the hot path, nothing hidden behind the generator once your project is built.

**Lightweight and quick to set up:** One NuGet package, no forced dependencies, no base classes to inherit, no configuration files, no startup registration required. A segment is a class implementing one interface; a pipeline is a partial class with a few `[Segment]` attributes.

### 🎯 Who Dovetail Is For

Dovetail is designed for managing complexity in composition and aggregation workflows: fanning out to several independent services or data sources and merging the results into one response. Specific use cases include BFF-style endpoints, dashboard and summary views, or GraphQL-style resolvers implemented over REST.

In addition, Dovetail works quite well for:

* **Read-side composition in CQRS-style architectures:** Query handlers that fan out to multiple read models or caches and assemble a view model.
* **Fixed-shape async initialization sequences:** A small, static DAG of async setup steps where some branches are genuinely independent of each other.
* **Teams splitting ownership across a composed endpoint:** Segments are plain, DI-constructible classes with no shared orchestration code, so different people can own different segments without touching how they're wired together.

### 🚫 Who Dovetail Is Not For

Most notably, Dovetail is for managing the complexity of aggregation logic that needs to be spread across many services. It's overkill if you have very simple use cases. That said, there are other properly complex use cases Dovetail isn't suited for:

* **Streaming or incremental results:** One execution produces one final result; Dovetail does not support `IAsyncEnumerable`, nor progressive rendering as branches complete.
* **Long-running or durable workflows:** Dovetail has no persistence, no checkpointing, and no resuming after a crash. Dovetail is an in-process, single-execution composition helper, not a durable orchestrator. Use [Temporal](https://github.com/temporalio/sdk-dotnet) or the [Durable Task Framework](https://github.com/Azure/durabletask) for durable execution, or [Hangfire](https://www.hangfire.io/) if you only need durable background jobs.
* **Dynamic pipeline shapes:** The DAG is resolved entirely by compile-time type matching, so the shape of the pipeline can't dynamically change at runtime. Dovetail does allow a limited degree of [conditional segment execution](#-conditional-segment-execution), but only within the context of a rigid, compile-time graph shape. [TPL Dataflow](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/dataflow-task-parallel-library) is the closest equivalent but with a runtime-assembled mesh instead of compile-time construction.

## 🚀 Quickstart

```
dotnet add package Dovetail
```

Dovetail relies on two main concepts: _pipelines_ and _segments_. _Segments_ are like normal services, but they encapsulate some operation that takes 0 or more inputs and produces some output. _Pipelines_ are composed of one or more _segments_. The segments that make up a pipeline need to have matching input/result types such that they can be stitched together in one call chain (i.e. SegmentA's result can be used as the input in SegmentB and so on). Dovetail does the code generation required to wire up those call chains, running operations asynchronously where able.

A segment is any class or struct that implements `IPipelineSegment<TResult>` (or the multi-input generic variants like `IPipelineSegment<TInput, TResult>`, up to eight inputs). This signifies that it can be used as a segment in a pipeline. Its inputs and result types have no restrictions:

```csharp
public class UserSegment(IUserStore users) : IPipelineSegment<UserId, User>
{
    public Task<User> ExecuteAsync(UserId id, CancellationToken ct) =>
        users.GetAsync(id, ct);
}

public class PermissionsSegment(IAuthService auth) : IPipelineSegment<User, Permissions>
{
    public Task<Permissions> ExecuteAsync(User user, CancellationToken ct) =>
        auth.GetForRoleAsync(user.RoleId, ct);
}

public class RecentActivitySegment(IActivityLog log) : IPipelineSegment<User, RecentActivity>
{
    public Task<RecentActivity> ExecuteAsync(User user, CancellationToken ct) =>
        log.GetRecentAsync(user.LatestActivityUuid, ct);
}
```

Declare the pipeline as a partial class, attaching `[Segment]` to a constructor parameter for each segment. Segments can also be static member methods:

```csharp
public partial class UserSummaryPipeline(
    [Segment] IPipelineSegment<UserId, User> user,
    [Segment] IPipelineSegment<User, Permissions> permissions,
    [Segment] IPipelineSegment<User, RecentActivity> activity
) : IPipeline<UserId, UserSummary>
{
    [Segment]
    private static UserSummary Summary(User user, Permissions permissions, RecentActivity activity) =>
        new UserSummary(user.Id, user.Username, permissions, activity);
}
```

Like `IPipelineSegment<...>`, `IPipeline<...>` comes in variants up to eight inputs (`IPipeline<T1, ..., T8, TResult>`). Any segment input that isn't produced by another segment is matched against the pipeline's own declared input types, so a multi-input pipeline just spreads those across its segments however the dependency graph calls for.

That's it! Dovetail generates `ExecuteAsync`:

```csharp
var pipeline = new UserSummaryPipeline(userSegment, permissionsSegment, activitySegment);
UserSummary summary = await pipeline.ExecuteAsync(userId, cancellationToken);
```

### 📝 Report Tool

Optionally, for more advanced insights, you can install the [Dovetail.Report](Dovetail.Report) tool to generate an HTML site that helps visualize the DAGs generated by Dovetail:

```
dotnet tool install --global Dovetail.Report # install
dovetail-report # generate the report
```

> [!TIP]
> The generated report will be in the `/dovetail-report` folder, you can open the `index.html` file from there in any browser.

> [!NOTE]
> [See an example report](https://dovetailreportwebsite-production.up.railway.app/) generated from the [example app](Dovetail.Example).

## 🔍 Detailed Explanation

Dovetail reads each segment's `IPipelineSegment<...>` interface to learn its input and result types, then wires the pipeline together purely by matching those types:

* A segment's input is satisfied by the pipeline's own input, or by another segment whose result matches. No other segment may produce the same type.
* The segment whose result matches the pipeline's own result type becomes the terminal step.
* The generated `ExecuteAsync` starts every segment concurrently, awaits the terminal step, and returns its result.
* If anything fails, Dovetail cancels a shared token and waits for the rest of the in-flight segments to unwind before rethrowing, leaving nothing running or unobserved.

Roughly, the pipeline above generates:

```csharp
public partial class UserSummaryPipeline
{
    public async Task<UserSummary> ExecuteAsync(UserId input, CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var linkedToken = cts.Token;

        var userTask = UserAsync();
        var permissionsTask = PermissionsAsync();
        var activityTask = ActivityAsync();
        var SummaryTask = SummaryAsync();

        try
        {
            return await SummaryTask.ConfigureAwait(false);
        }
        catch
        {
            cts.Cancel();
            try { await Task.WhenAll(userTask, permissionsTask, activityTask).ConfigureAwait(false); }
            catch { }
            throw;
        }

        async Task<User> UserAsync() =>
            await user.ExecuteAsync(input, linkedToken).ConfigureAwait(false);

        async Task<Permissions> PermissionsAsync() =>
            await permissions.ExecuteAsync(await userTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);

        async Task<RecentActivity> ActivityAsync() =>
            await activity.ExecuteAsync(await userTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);

        async Task<UserSummary> SummaryAsync() =>
            Summary(
                await userTask.ConfigureAwait(false),
                await permissionsTask.ConfigureAwait(false),
                await activityTask.ConfigureAwait(false));
    }
}
```

> [!NOTE]
> The code above is simplified for readability. The generator fully qualifies every type it emits, as well as extra handling for telemetry and concurrency management.

### 🔌 Dependency Injection

If your project references `Microsoft.Extensions.DependencyInjection`, Dovetail also generates an `AddPipelines()` extension method:

```csharp
services.AddPipelines();
```

This registers every segment and pipeline it finds anywhere in your compilation by their concrete type. With that in place, pipelines and segments alike can be injected:

```csharp
public class UsersController(UserSummaryPipeline pipeline)
{
    public Task<UserSummary> GetAsync(UserId userId, CancellationToken ct) =>
        pipeline.ExecuteAsync(userId, ct);
}
```

> [!IMPORTANT]
> `AddPipelines()` is only generated when the DI package is actually referenced. This keeps Dovetail from having a dependency on it, so projects that don't use DI are unaffected.

> [!NOTE]
> The generated extension only registers segments and pipelines themselves, whatever _they_ depend on (an `HttpClient`, a typed client, a repository) still needs its own ordinary registration:
>
> ```csharp
> services.AddHttpClient<IAuthService, AuthService>();
> services.AddPipelines();
> ```

Every segment and pipeline is registered transient by default. Add `[Lifetime(DependencyLifetime.Singleton)]` or `[Lifetime(DependencyLifetime.Scoped)]` from `Dovetail.DependencyInjection` to change a segment or pipeline's lifetime:

```csharp
using Dovetail.DependencyInjection;

[Lifetime(DependencyLifetime.Singleton)]
public class ExpensiveClientSegment(ExpensiveClient client) : IPipelineSegment<Request, Response>
{
    public Task<Response> ExecuteAsync(Request request, CancellationToken ct) => client.SendAsync(request, ct);
}
```

Each non-generic segment is also registered against every `IPipelineSegment<...>` interface it implements, so it resolves whether a pipeline asks for it by its concrete type or by any of those interfaces. A segment implementing more than one `IPipelineSegment<...>` interface (each with its own shape) is registered against each of them. Generic segments are registered by concrete type only, since there's no way to express a DI service type that mixes closed and open type arguments.

> [!WARNING]
> If two segments implement the exact same `IPipelineSegment<...>` interface, `AddPipelines()` wouldn't know which one to use for that interface, so this is a compile error (DOVE017).

### 🏗️ Constructors

Both primary and conventional constructors work:

```csharp
public partial class UserPermissionsPipeline : IPipeline<UserId, Permissions>
{
    private readonly UserSegment _user;
    private readonly PermissionsSegment _permissions;

    public UserPermissionsPipeline([Segment] UserSegment user, [Segment] PermissionsSegment permissions)
    {
        _user = user;
        _permissions = permissions;
    }
}
```

Here, Dovetail resolves each `[Segment]` parameter's value by finding the one field or property on the type whose declared type matches the parameter's: `_user` and `_permissions` above, regardless of their names. If no member matches, or more than one does, that's a compile error (DOVE010/DOVE011) rather than something you'd discover at runtime, so name your backing members however you like.

> [!TIP]
> A `[Segment]` parameter can also be typed as the segment's `IPipelineSegment<...>` interface instead of its concrete type:
>
> ```csharp
> public partial class UserPermissionsPipeline(
>     [Segment] IPipelineSegment<UserId, User> user,
>     [Segment] PermissionsSegment permissions
> ) : IPipeline<UserId, Permissions>;
> ```

### ⚙️ Static Segment Methods

Dovetail supports static methods in the pipeline class being used as segments:

```csharp
public partial class MyPipeline : IPipeline<int, string>
{
    [Segment]
    private static string Stringify(int num) => num.ToString();
}
```

While `IPipelineSegment<...>` only supports up to eight inputs, static segment methods support any number of inputs. This can be a particular benefit when aggregating all of the segment results into the final pipeline output (also sparing the need for a dangling "Assembler" segment):

```csharp
public partial class LargePipeline(
    [Segment] SegmentOne one,
    [Segment] SegmentTwo two,
    /* ... */
    [Segment] SegmentTwelve twelve
) : IPipeline<LargeQuery, LargeModel>
{
    [Segment]
    private static LargeModel Aggregate(OneResult one, TwoResult two, /* ... */, TwelveResult twelve) =>
        new LargeModel(/* ... */);
}
```

This also supports cases where it would be cumbersome to create a segment class for simple data transformations in the middle of a pipeline run:

```csharp
public class UserSegment : IPipelineSegment<UserId, User> { ... }
public class TeamSegment : IPipelineSegment<TeamId, Team> { ... }

public partial class UserTeamPipeline(
    [Segment] UserSegment user,
    [Segment] TeamSegment team
) : IPipeline<UserId, Team>
{
    [Segment]
    private static TeamId UserToTeamId(User user) => user.TeamId;
}
```

A segment method may take an optional trailing `CancellationToken`, whether or not it's `async`, and it can be `async` too, returning `Task<TResult>` instead of `TResult` directly, exactly like a class-based segment:

```csharp
[Segment]
private static async Task<Result> SomeSegment(Input input, CancellationToken ct) => await ...;
```

> [!IMPORTANT]
> The method must be `static` (DOVE012) and must return a value (either `TResult` or `Task<TResult>`) (DOVE013). The static restriction guarantees the method's only inputs are the parameters Dovetail can see and validate.

### 🚦 Managing Concurrency

Add `[MaxConcurrency(n)]` to a pipeline to bound how many of its segments may run at once:

```csharp
[MaxConcurrency(4)]
public partial class MyPipeline(
    [Segment] IPipelineSegment<Input, A> first,
    [Segment] IPipelineSegment<Input, B> second,
    [Segment] IPipelineSegment<A, B, Output> combine
) : IPipeline<Input, Output>;
```

Without it, every eligible segment starts at once. With it, each segment's execution is gated behind a shared semaphore instead, so at most `n` are ever running concurrently. It applies uniformly to every kind of segment, instance-based or static `[Segment]` methods alike, and composes correctly with cancellation: a segment still waiting for a free slot when a sibling fails is cancelled out of its wait immediately, rather than left waiting.

> [!TIP]
> `[MaxConcurrency(1)]` can be used to force the pipeline to execute sequentially.

> [!WARNING]
> The limit is per-pipeline, not global: a nested pipeline used as a segment ([Pipelines-as-Segments](#-pipelines-as-segments)) fans out (and throttles, if it declares its own `[MaxConcurrency(n)]`) independently of its parent.
>
> `n` must be a positive integer (DOVE019). Omit the attribute to leave concurrency unbounded, which is the default.

### 🪈 Generics

Pipelines and segments can be generic, and a pipeline's own type parameters can flow through to its segments, each segment using a different one:

```csharp
public class FirstSegment<T> : IPipelineSegment<Input, T> { ... }
public class SecondSegment<T> : IPipelineSegment<T, Result> { ... }

public partial class MyPipeline<T, U>(
    [Segment] FirstSegment<T> first,
    [Segment] SecondSegment<U> second
) : IPipeline<Input, Result>
{
    [Segment]
    private static U TtoU(T t) => t.ToU();
}
```

### 🔗 Pipelines-as-Segments

`IPipelineSegment<...>` and `IPipeline<...>` share the same method name (`ExecuteAsync`) wherever their shapes line up (the same input types, in the same order, and the same result type). This means a pipeline can double as a segment of another pipeline by implementing both interfaces:

```csharp
public partial class InnerPipeline(
    [Segment] IPipelineSegment<Input, Model> first,
    [Segment] IPipelineSegment<Model, Output> second
) : IPipeline<Input, Output>, IPipelineSegment<Input, Output>;
```

Since both interfaces declare an identical `Task<Output> ExecuteAsync(Input, CancellationToken)`, the one `ExecuteAsync` Dovetail already generates for `IPipeline<Input, Output>` satisfies `IPipelineSegment<Input, Output>` too, so there's nothing extra for you to write. `InnerPipeline` can now be called directly, or used as `[Segment] InnerPipeline inner` inside a larger pipeline, and either way it's the same generated method doing the work.

> [!NOTE]
> This only applies when the shapes match. A type that implements `IPipelineSegment<...>` without a matching `IPipeline<...>` still needs its `ExecuteAsync` hand-written, exactly like any other segment.

### 📡 Tracing

If `System.Diagnostics.DiagnosticSource` is available, Dovetail wraps the pipeline and every segment in an `Activity`, so you can see exactly which segment was slow without adding anything yourself:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Dovetail"));
```

Every pipeline's `ExecuteAsync` starts an activity named `"{Pipeline}.ExecuteAsync"`, and each segment gets its own nested `"{Pipeline}.{segment}"` activity, nested such that a segment's span starts while it's still the ambient activity from the pipeline that kicked it off. Each activity carries `dovetail.pipeline`, and segment activities also carry `dovetail.segment` (its role in this pipeline) and `dovetail.segment.type` (its concrete class). If a segment throws, its activity is marked `Error` before the exception propagates.

> [!IMPORTANT]
> Like the dependency injection generation, the tracing logic is only generated when `System.Diagnostics.DiagnosticSource` is available; Dovetail doesn't depend on it. When the namespace is unavailable, `ExecuteAsync` is generated exactly as if tracing didn't exist.

> [!NOTE]
> The tracing calls are still nearly free if nothing's listening: `Activity.StartActivity` returns `null` without a registered listener, and every call after it is a `?.`-guarded no-op.

## 🐛 Debugging

Because a pipeline's shape is resolved entirely by compile-time type matching, most structural mistakes (i.e. a wrong input type, a cycle, an unreachable segment, or an ambiguous match) are already caught as a [diagnostic](#-diagnostics) with an actionable message, not a runtime surprise. If a pipeline behaves unexpectedly, check for a DOVE0xx error before assuming the logic itself is wrong.

> [!WARNING]
> A pipeline class with zero `[Segment]`-tagged members produces no diagnostic and no generated code at all, since Dovetail only examines types with at least one `[Segment]` usage. The error you'll see in this case is `CS0535: does not implement interface member` instead of a Dovetail-specific one, which can look like a missing-feature bug rather than a missing `[Segment]` attribute.

### 📚 Reading the Generated Source

The fastest way to understand what a pipeline actually executes is to read the code Dovetail wrote for it rather than infer it. Dovetail outputs relatively simple, human-readable code. Add this to a project to write the generated files to disk:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

> [!TIP]
> You'll find the generated code in `Generated/Dovetail`. Like any other file, you can set breakpoints in the generated source.

### 🏎️ Concurrency and Exceptions

Every segment starts running immediately, but only the segment producing the pipeline's result is directly awaited; every other segment is guaranteed to be awaited transitively somewhere along the way there (that's what [DOVE008](#-diagnostics) enforces). When two or more segments fail around the same time, it comes down to a race for which exception actually reaches the caller (see [Exception Handling](#-exception-handling)).

> [!IMPORTANT]
> If you have "break on all exceptions" enabled you'll see a first-chance exception break for _every_ failing segment even though only one of them ends up as the exception `ExecuteAsync` actually throws. The extra breaks aren't extra bugs.

> [!NOTE]
> The `CancellationToken` a segment receives isn't the same token instance passed into `ExecuteAsync`. Rather, Dovetail links it internally so it can cancel sibling segments as soon as one fails. This only matters if you're comparing token instances directly.

### 🌱 DI Lifetimes

`[Lifetime(...)]` defaults every pipeline and segment to `Transient`, which is the safe default. The risk shows up once you opt into `Scoped` or `Singleton`: any mutable state your own segment holds is now shared across concurrent pipeline executions. Turning on `ValidateScopes` and `ValidateOnBuild` when building your `ServiceProvider` is good practice generally, and it'll catch a `Scoped` segment landing inside a longer-lived pipeline at startup instead of at first request.

### 🛟 Isolating Failures

Segments are plain, [independently testable](#-testing) classes, so reproduce a suspected bug by exercising the segment directly instead of running the whole pipeline. If you've adopted the [Result pattern](#-collecting-multiple-errors) for multi-error collection, remember that debugging shifts from catching an exception to inspecting the returned `Result`.

### 🗺️ Generated Diagrams

To make it easy to visually assess Dovetail's DAG, every generated `ExecuteAsync` carries an XML doc comment with a Mermaid flowchart of that pipeline's computed segment graph. Most IDEs won't render Mermaid directly in a tooltip, but the diagram can be copied into [mermaid.live](https://mermaid.live) or any other Mermaid viewer.

<details><summary>Mermaid sample from the Example app</summary>

```mermaid
flowchart TD
    in_0(["input: OrderId"])
    seg_order["order: OrderDetails"]
    seg_OrderDetailsToUserId["OrderDetailsToUserId: UserId"]
    seg_customer["customer: CustomerProfile"]
    seg_payment["payment: PaymentStatus"]
    seg_shipment["shipment: ShipmentTrackingInfo"]
    seg_Assemble("Assemble: OrderConfirmation")
    in_0 --> seg_order
    seg_order --> seg_OrderDetailsToUserId
    seg_OrderDetailsToUserId --> seg_customer
    seg_order --> seg_payment
    seg_order --> seg_shipment
    seg_order --> seg_Assemble
    seg_customer --> seg_Assemble
    seg_payment --> seg_Assemble
    seg_shipment --> seg_Assemble
```

</details>

> [!TIP]
> For a more comprehensive visual diagram of the generated DAG, consider using `dovetail-report`, described in the next section:

## 📝 Dovetail.Report Tool

[Dovetail.Report](Dovetail.Report) is a `dotnet` tool that generates a static, offline-capable HTML report of the DAGs of the pipelines generated by Dovetail. It's useful for helping to understand exactly how Dovetail is wiring the pipelines together.

```
dotnet tool install --global Dovetail.Report
```

For CI usage (see below), install it to a local tool manifest instead, so the pinned version is explicit and reproducible rather than always floating to latest:

```
dotnet new tool-manifest # if your repo doesn't already have one
dotnet tool install Dovetail.Report
```

> [!NOTE]
> [See an example report](https://dovetailreportwebsite-production.up.railway.app/) generated from the [example app](Dovetail.Example).

### 🏃 Running

```
dovetail-report --project path/to/MyApp.csproj
```

Or `--solution path/to/MyApp.sln` to report on every project in a solution at once. Omit both to auto-discover in the current directory.

Reports write to `./dovetail-report` by default; pass `--output <path>` to choose another location. Each run replaces that directory's previous contents entirely, so no stale pages are left behind from a since-renamed or since-removed pipeline.

```
dovetail-report --project Dovetail.Example/Dovetail.Example.csproj --output ./report
```

### 📂 Output

The output directory is fully self-contained: `index.html`, one `{FullyQualifiedPipelineName}.html` page per discovered pipeline, and a `vendor/` folder holding the pinned Mermaid.js build and stylesheet the pages need. Open `index.html` directly from disk. Nothing on the page reaches out to the network.

> [!NOTE]
> Projects with no pipelines will produce an empty report. Projects that don't compile will fail the report generator.

### ⚒️ Using in CI

As long as you have `Dovetail.Report` in your repo's tool manifest, `dotnet tool restore` will pull the tool in, and `dovetail-report` can be used as a step in your CI build. Here's a GitHub Actions example:

```yaml
- name: Generate Dovetail report
  run: |
    dotnet tool restore
    dotnet tool run dovetail-report --project MyApp/MyApp.csproj --output dovetail-report

- name: Upload Dovetail report
  uses: actions/upload-artifact@v4
  with:
    name: dovetail-report
    path: dovetail-report
```

## 🏛️ Architectural Considerations

Dovetail is deliberately narrow. It isn't a workflow engine, a saga framework, or a runtime-configured service graph. Its entire job is to take a set of single-purpose units of work whose inputs and outputs are declared in the type system, and generate the one thing you'd otherwise have to hand-write and hand-verify yourself: the call graph connecting them, running as much of it concurrently as the data dependencies allow. The DAG isn't discovered, configured, or built up at runtime; it's just the parameter types. That gives us the compile-time security that Dovetail intends to provide.

That narrowness pushes toward a particular shape for the part of an app that uses it. Specifically, as pipelines must resolve to some single output, their entire focus is on managing many different sources of data to compose a single object. This sort of aggregation is typically the responsibility of the business/domain/application layer, though no doubt Dovetail could be used at any reasonable point in the application.

### ⚡ Concurrency

Segments that don't depend on each other run genuinely concurrently, not just asynchronously in sequence, which results in several considerations to design around up front, beyond exception/error handling (see below):

* **Concurrency comes from overlapping I/O, not from spreading compute across cores.** A segment that's actually CPU-heavy runs no differently than it would in hand-written async code without an explicit `Task.Run`: Dovetail doesn't parallelize it for you, but it doesn't get in the way either. Wrap the computation in `Task.Run` yourself if you want it to actually run on another thread.

* **Shared dependencies need to tolerate concurrent use.** If two segments in the same pipeline take the same injected instance (i.e. an EF Core `DbContext`, a non-thread-safe cache client, anything not built for concurrent access) they can genuinely collide mid-execution, not just under load. This is different from a DI lifetime mistake leaking state across executions; here, two segments are touching the same object during one execution. Give each segment its own instance (typically `Scoped` per execution, or `Transient`), or use a dependency that's actually safe to share.

* **Ordering is only what the type graph says it is.** If segment B needs to run after segment A but doesn't actually consume A's result, Dovetail has no way to know that; declaration order doesn't matter, only whether one segment's input is another's output. Any ordering requirement that isn't expressed as a real data dependency is undefined. Therefore, you should express it as one, even a trivial pass-through, rather than relying on how the DAG happens to schedule things today.

* **A sibling's failure doesn't stop an independent segment's side effects.** When one segment fails, Dovetail cancels a shared token and drains the rest, but that's cooperative, not preemptive, as a segment that doesn't check the token keeps running until it finishes. If the code is running on a branch independent of the failure, it can complete in full even though the pipeline as a whole ends up throwing. Put side-effecting segments as late in the DAG as you reasonably can, so they only run once everything upstream of them has actually succeeded, rather than racing alongside branches that might fail.

* **Fan-out is unbounded by default.** Every eligible segment starts at once, so a pipeline fanning out to a few dozen segments that each call an external API fires that many concurrent calls simultaneously, making connection-pool exhaustion and rate-limit responses a real risk. [`[MaxConcurrency(n)]`](#-managing-concurrency) bounds this per pipeline, but it's still easy to undercount the real concurrency of an outer pipeline: the limit doesn't compound automatically, so a nested pipeline used as a segment ([Pipelines-as-Segments](#-pipelines-as-segments)) fans out independently of its parent's limit.

### ♻️ Endomorphism

An _endomorphic_ segment is one that consumes and produces the same type: `IPipelineSegment<T, T>`. These frequently come up in use cases like refinement, enrichment, and validation. Dovetail supports exactly one such segment per type in a pipeline, even though in other cases Dovetail explicitly forbids two segments producing the same type. As long as one segment (or the pipeline's own input) produces a type and one other segment both consumes and produces that same type, Dovetail chains them automatically, and anything else in the pipeline that needs the type receives the refined value, not the original.

```csharp
public partial class MyPipeline(
    [Segment] IPipelineSegment<A, B> aToB,
    [Segment] IPipelineSegment<B, B> bToB,
    [Segment] IPipelineSegment<B, C> bToC
) : IPipeline<A, C>;
```

`bToB` both consumes and produces `B`, so Dovetail knows it runs right after `aToB`. `bToC`, or anything else in the pipeline that needs a `B`, automatically receives the result of `bToB` with no extra wiring beyond declaring the segment.

Chains of endomorphic segments (i.e. multiple `IPipelineSegment<B, B>` segments in the example above) are explicitly not supported; Dovetail favors simplicity over supporting smaller edge cases. Executing a chain of endomorphic segments is trivially simple to hand-write, so the recommended workaround is to write a single segment that sequentially executes the pieces.

Also explicitly not supported are graph structures where some subset of the graph composes to an endomorphism, such as the following where the middle two segments could be considered a single endomorphic segment when taken together:

```
(A => B) -> (B => C) -> (C => B) -> (B => D)
```

In these cases, the recommended approach is to compose the middle two segments together in a [pipeline-as-segment](#-pipelines-as-segments). This forces your code to specifically acknowledge the intent to treat the subgraph as an endomorphism.

### 💥 Exception Handling

Segments are not sandboxed within a pipeline, so an exception from one segment fails the entire pipeline. It was deliberately chosen that Dovetail has no concept of an "optional" segment. If a segment should degrade gracefully instead of failing the whole pipeline, catch its own failure and return a fallback value:

```csharp
public class RecentActivitySegment(IActivityLog log) : IPipelineSegment<User, RecentActivity>
{
    public async Task<RecentActivity> ExecuteAsync(User user, CancellationToken ct)
    {
        try
        {
            return await log.GetRecentAsync(user.LatestActivityUuid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RecentActivity.Empty;
        }
    }
}
```

> [!WARNING]
> Don't catch `OperationCanceledException` this way. If the pipeline is actually being cancelled, that should propagate normally rather than being swallowed into a fallback value.

If multiple concurrent segments fail to catch their own exceptions, only one exception ever reaches the caller of `ExecuteAsync`, not an `AggregateException` containing failures from every segment. The generated code's `try`/`catch` only observes the exception that surfaces through the terminal segment's own await chain, and sibling branches that fail independently of that chain are cancelled and drained via `Task.WhenAll(...)` inside a `catch { }` that discards their exceptions.

> [!TIP]
> If you need visibility into every exception rather than just the one that propagates, [tracing](#-tracing) marks every throwing segment's own activity `Error`, regardless of which single exception makes it back to the caller.

### 📋 Collecting Multiple Errors

Given the limits of collecting exceptions, a better pattern is to collect error results through the pipeline if you need visibility into multiple error states. Some result pattern that captures error results should be used:

```csharp
public class DataAccessSegment(IDataRepo repo) : IPipelineSegment<Input, Result<DbRecord>>
{
    public async Task<Result<DbRecord>> ExecuteAsync(Input input, CancellationToken ct)
    {
        try
        {
            return new SuccessResult<DbRecord>(await repo.GetRecordAsync(input, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ErrorResult(ex.Message);
        }
    }
}
```

In typical fashion for the result pattern, this propagates `Result<T>` across all the segments, requiring that they both resolve the model from the result object and handle non-success cases:

```csharp
public class ProcessingSegment(...) : IPipelineSegment<Result<DbRecord>, Result<Model>>
{
    public async Task<Result<Model>> ExecuteAsync(Result<DbRecord> dbResult, CancellationToken ct) =>
        dbResult switch
        {
            ErrorResult => ...,
            SuccessResult { Value: var record } => ...,
            ...
        }
}
```

### ⁉️ Conditional Execution

Dovetail has no dedicated feature for conditional execution, but because segments are just plain classes with constructor-injected dependencies, wrapping one in another gets you a limited form of it for free.

Static segment methods can't help here: a `[Segment]` method must be `static` (see [Static Segment Methods](#%EF%B8%8F-static-segment-methods)), so it has no access to constructor-injected dependencies like a feature flag service. Conditional branching therefore has to live in an ordinary segment class, with the real segment and the flag service as its constructor dependencies. You'll need to write your own `ExecuteAsync` for this:

```csharp
public class ConditionalSegment(
    FeatureFlagService flags,
    IPipelineSegment<Input, Output> inner
) : IPipelineSegment<Input, Output>
{
    public async Task<Output> ExecuteAsync(Input input, CancellationToken ct) =>
        flags.IsSuperFeatureEnabled
        ? await inner.ExecuteAsync(input, ct)
        : new Output();
}
```

The same technique scales to whole branches by combining it with [pipelines-as-segments](#-pipelines-as-segments): you can define the branch as its own pipeline, then wrap that pipeline the same way you'd wrap a single segment:

```csharp
public partial class InnerPipeline(
    [Segment] IPipelineSegment<SecondModel, ThirdModel> second,
    [Segment] IPipelineSegment<ThirdModel, FourthModel> third
) : IPipeline<SecondModel, FourthModel>, IPipelineSegment<SecondModel, FourthModel>;

public class ConditionalInnerSegment(
    FeatureFlagService flags,
    InnerPipeline inner
) : IPipelineSegment<SecondModel, FourthModel>
{
    public async Task<FourthModel> ExecuteAsync(SecondModel model, CancellationToken ct) =>
        flags.IsSuperFeatureEnabled
        ? await inner.ExecuteAsync(model, ct)
        : new FourthModel();
}

public partial class OuterPipeline(
    [Segment] IPipelineSegment<FirstModel, SecondModel> first,
    [Segment] ConditionalInnerSegment innerConditional,
    [Segment] IPipelineSegment<FourthModel, FifthModel> fourth
) : IPipeline<FirstModel, FifthModel>;
```

### 🧪 Testing

Segments are plain classes with constructor-injected dependencies so you can test them exactly like you'd test any other class, with whatever approach you already use:

```csharp
public class PermissionsSegmentTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsPermissionsForUsersRole()
    {
        var segment = new PermissionsSegment(new FakeAuthService(Permissions.ReadOnly));

        var result = await segment.ExecuteAsync(new User { RoleId = RoleId.Viewer }, CancellationToken.None);

        Assert.Equal(Permissions.ReadOnly, result);
    }

    private class FakeAuthService(Permissions permissions) : IAuthService
    {
        public Task<Permissions> GetForRoleAsync(RoleId roleId, CancellationToken ct) =>
            Task.FromResult(permissions);
    }
}
```

> [!NOTE]
> `ExecuteAsync` itself isn't something you typically need to unit test as Dovetail generates it, and its correctness (dependency resolution, concurrency, failure handling) is covered by Dovetail's own test suite. Test each segment's logic in isolation, and integration-test the assembled pipeline the same way you'd test anything else built on `IPipeline<...>`.

## 🩺 Diagnostics

| ID | Meaning |
|---|---|
| DOVE001 | The pipeline type must be `partial`. |
| DOVE002 | The pipeline type must implement exactly one `IPipeline<...>` interface. |
| DOVE003 | A `[Segment]` parameter's type must implement exactly one `IPipelineSegment<...>` interface; if its concrete type implements more than one, type the parameter as the specific interface instead. |
| DOVE004 | No segment produces the pipeline's result type; add one or change the pipeline's declared result type. |
| DOVE005 | Two or more segments produce the same type; change one's result type or remove the extras. |
| DOVE006 | Nothing produces a segment's input; add a segment that does or declare it as one of the pipeline's own inputs. |
| DOVE007 | The segments form a dependency cycle; break it by removing or redirecting one of the dependencies. |
| DOVE008 | A segment's result is never used, directly or transitively, by the segment producing the pipeline's result; remove it or route its result onto that path. |
| DOVE009 | The pipeline declares the same input type more than once; wrap one in its own type or combine them into a single input. |
| DOVE010 | A `[Segment]` parameter on a non-primary constructor has no field or property of its type to read its value from; use a primary constructor or add one. |
| DOVE011 | A `[Segment]` parameter on a non-primary constructor has more than one field or property of its type; Dovetail can't tell which one to use, so use a primary constructor or remove the extras. |
| DOVE012 | A `[Segment]` method must be `static`. |
| DOVE013 | A `[Segment]` method must return a value, either `TResult` or `Task<TResult>`. |
| DOVE014 | Every type containing a nested pipeline must be `partial`. |
| DOVE015 | A pipeline can't be nested inside a generic type; move it out, or make the ancestor non-generic. |
| DOVE016 | A `[Segment]` method can't have its own type parameters; it can use the pipeline's, but can't introduce new ones. |
| DOVE017 | Two or more segments implement the same `IPipelineSegment<...>` interface, so `AddPipelines()` can't tell which one to register for it. |
| DOVE018 | A segment's input ambiguously matches both a pipeline input and another segment's result; give one of them a distinct type. |
| DOVE019 | `[MaxConcurrency(n)]`'s value must be 1 or greater; use a positive integer, or remove the attribute. |
| DOVE020 | Segments producing the same type don't form a single valid chain (more than one may both consume and produce it, or three or more produce it at once); remove the extras, or restructure so only one segment transforms the type into itself. |