using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class StaticSegmentMethodTests
{
    [Fact]
    public void EmitsFanOutFanIn_ForStaticMethodSegment()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct OrderId(int Value);
            public readonly record struct UserId(int Value);

            public class OrderInfo { public UserId UserId { get; init; } }
            public class CustomerProfile { public UserId UserId { get; init; } }

            public class OrderInfoSegment : IPipelineSegment<OrderId, OrderInfo>
            {
                public Task<OrderInfo> ExecuteAsync(OrderId orderId, CancellationToken ct) =>
                    Task.FromResult(new OrderInfo { UserId = new UserId(orderId.Value) });
            }

            public class CustomerProfileSegment : IPipelineSegment<UserId, CustomerProfile>
            {
                public Task<CustomerProfile> ExecuteAsync(UserId userId, CancellationToken ct) =>
                    Task.FromResult(new CustomerProfile { UserId = userId });
            }

            public partial class OrderPipeline(
                [Segment] OrderInfoSegment order,
                [Segment] CustomerProfileSegment customer
            ) : IPipeline<OrderId, CustomerProfile>
            {
                [Segment]
                private static UserId ExtractUserId(OrderInfo order) => order.UserId;
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var generated = Assert.Single(result.Results.Single().GeneratedSources, static s => s.HintName == "OrderPipeline.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains("var ExtractUserIdTask = ExtractUserIdAsync();", text);
        Assert.Contains("await order.ExecuteAsync(input, linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("return ExtractUserId(await orderTask.ConfigureAwait(false));", text);
        Assert.Contains("await customer.ExecuteAsync(await ExtractUserIdTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("segmentActivity?.SetTag(\"dovetail.segment.type\", \"ExtractUserId\");", text);
    }

    [Fact]
    public async Task GeneratedPipeline_ForStaticMethodSegment_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct OrderId(int Value);
            public readonly record struct UserId(int Value);

            public class OrderInfo { public UserId UserId { get; init; } }
            public class CustomerProfile { public UserId UserId { get; init; } }

            public class OrderInfoSegment : IPipelineSegment<OrderId, OrderInfo>
            {
                public Task<OrderInfo> ExecuteAsync(OrderId orderId, CancellationToken ct) =>
                    Task.FromResult(new OrderInfo { UserId = new UserId(orderId.Value) });
            }

            public class CustomerProfileSegment : IPipelineSegment<UserId, CustomerProfile>
            {
                public Task<CustomerProfile> ExecuteAsync(UserId userId, CancellationToken ct) =>
                    Task.FromResult(new CustomerProfile { UserId = userId });
            }

            public partial class OrderPipeline(
                [Segment] OrderInfoSegment order,
                [Segment] CustomerProfileSegment customer
            ) : IPipeline<OrderId, CustomerProfile>
            {
                [Segment]
                private static UserId ExtractUserId(OrderInfo order) => order.UserId;
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.OrderPipeline")!;
        var order = Activator.CreateInstance(assembly.GetType("Sample.OrderInfoSegment")!)!;
        var customer = Activator.CreateInstance(assembly.GetType("Sample.CustomerProfileSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, order, customer)!;

        var orderIdType = assembly.GetType("Sample.OrderId")!;
        var orderId = Activator.CreateInstance(orderIdType, 42)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [orderId, CancellationToken.None])!;

        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var userId = result.GetType().GetProperty("UserId")!.GetValue(result)!;
        var value = (int)userId.GetType().GetProperty("Value")!.GetValue(userId)!;

        Assert.Equal(42, value);
    }

    [Fact]
    public void EmitsFanOutFanIn_ForAsyncStaticMethodSegment()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Seed(int Value);
            public readonly record struct Doubled(int Value);

            public class SeedSegment : IPipelineSegment<int, Seed>
            {
                public Task<Seed> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Seed(value));
            }

            public partial class DoublePipeline(
                [Segment] SeedSegment seed
            ) : IPipeline<int, Doubled>
            {
                [Segment]
                private static async Task<Doubled> Double(Seed seed, CancellationToken ct)
                {
                    await Task.Delay(1, ct);
                    return new Doubled(seed.Value * 2);
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var generated = Assert.Single(result.Results.Single().GeneratedSources, static s => s.HintName == "DoublePipeline.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains("return await Double(await seedTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);", text);
    }

    [Fact]
    public async Task GeneratedPipeline_ForAsyncStaticMethodSegment_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Seed(int Value);
            public readonly record struct Doubled(int Value);

            public class SeedSegment : IPipelineSegment<int, Seed>
            {
                public Task<Seed> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Seed(value));
            }

            public partial class DoublePipeline(
                [Segment] SeedSegment seed
            ) : IPipeline<int, Doubled>
            {
                [Segment]
                private static async Task<Doubled> Double(Seed seed, CancellationToken ct)
                {
                    await Task.Delay(1, ct);
                    return new Doubled(seed.Value * 2);
                }
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.DoublePipeline")!;
        var seed = Activator.CreateInstance(assembly.GetType("Sample.SeedSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, seed)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [21, CancellationToken.None])!;

        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (int)result.GetType().GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(42, value);
    }

    [Fact]
    public void ReportsDiagnostic_WhenSegmentMethodIsNotStatic()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class BadPipeline([Segment] FooSegment foo) : IPipeline<int, string>
            {
                [Segment]
                private string NotStatic(int value) => value.ToString();
            }
            """;

        AssertSingleDiagnostic(source, "DOVE012");
    }

    [Fact]
    public void ReportsDiagnostic_WhenSegmentMethodDoesNotReturnAValue()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class BadPipeline([Segment] FooSegment foo) : IPipeline<int, string>
            {
                [Segment]
                private static void NoReturn(int value) { }
            }
            """;

        AssertSingleDiagnostic(source, "DOVE013");
    }

    [Fact]
    public void ReportsDiagnostic_WhenSegmentMethodHasItsOwnTypeParameters()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class BadPipeline([Segment] FooSegment foo) : IPipeline<int, string>
            {
                [Segment]
                private static T2 Convert<T1, T2>(T1 value) => default!;
            }
            """;

        AssertSingleDiagnostic(source, "DOVE016");
    }

    [Fact]
    public async Task GeneratedPipeline_ForStaticMethodSegment_AggregatesMoreThanEightInputs()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct V1(int Value);
            public readonly record struct V2(int Value);
            public readonly record struct V3(int Value);
            public readonly record struct V4(int Value);
            public readonly record struct V5(int Value);
            public readonly record struct V6(int Value);
            public readonly record struct V7(int Value);
            public readonly record struct V8(int Value);
            public readonly record struct V9(int Value);
            public readonly record struct Total(int Value);

            public class V1Segment : IPipelineSegment<int, V1> { public Task<V1> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new V1(v + 1)); }
            public class V2Segment : IPipelineSegment<int, V2> { public Task<V2> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new V2(v + 2)); }
            public class V3Segment : IPipelineSegment<int, V3> { public Task<V3> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new V3(v + 3)); }
            public class V4Segment : IPipelineSegment<int, V4> { public Task<V4> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new V4(v + 4)); }
            public class V5Segment : IPipelineSegment<int, V5> { public Task<V5> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new V5(v + 5)); }
            public class V6Segment : IPipelineSegment<int, V6> { public Task<V6> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new V6(v + 6)); }
            public class V7Segment : IPipelineSegment<int, V7> { public Task<V7> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new V7(v + 7)); }
            public class V8Segment : IPipelineSegment<int, V8> { public Task<V8> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new V8(v + 8)); }
            public class V9Segment : IPipelineSegment<int, V9> { public Task<V9> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new V9(v + 9)); }

            public partial class WideAssemblyPipeline(
                [Segment] V1Segment s1, [Segment] V2Segment s2, [Segment] V3Segment s3, [Segment] V4Segment s4,
                [Segment] V5Segment s5, [Segment] V6Segment s6, [Segment] V7Segment s7, [Segment] V8Segment s8,
                [Segment] V9Segment s9
            ) : IPipeline<int, Total>
            {
                [Segment]
                private static Total Combine(V1 v1, V2 v2, V3 v3, V4 v4, V5 v5, V6 v6, V7 v7, V8 v8, V9 v9) =>
                    new Total(v1.Value + v2.Value + v3.Value + v4.Value + v5.Value + v6.Value + v7.Value + v8.Value + v9.Value);
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.WideAssemblyPipeline")!;

        var segments = Enumerable.Range(1, 9)
            .Select(i => Activator.CreateInstance(assembly.GetType($"Sample.V{i}Segment")!)!)
            .ToArray();

        var pipeline = Activator.CreateInstance(pipelineType, segments)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [0, CancellationToken.None])!;

        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (int)result.GetType().GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(45, value);
    }
}
