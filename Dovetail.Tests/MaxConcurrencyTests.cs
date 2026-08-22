using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class MaxConcurrencyTests
{
    [Fact]
    public void EmitsConcurrencyGate_WhenMaxConcurrencyAttributeIsPresent()
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

            [MaxConcurrency(2)]
            public partial class GatedPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var result = RunGenerator(source, includeActivitySource: false);
        var generated = Assert.Single(result.GeneratedTrees);
        var text = generated.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.Contains("using var concurrencyGate = new global::System.Threading.SemaphoreSlim(2);", text);
        Assert.Contains("await concurrencyGate.WaitAsync(linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("concurrencyGate.Release();", text);
    }

    [Fact]
    public void EmitsConcurrencyGate_WhenMaxConcurrencyAttributeIsPresent_WithTracing()
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

            [MaxConcurrency(2)]
            public partial class GatedPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var result = RunGenerator(source, includeActivitySource: true);
        var sources = result.Results.Single().GeneratedSources;
        var text = Assert.Single(sources, static s => s.HintName == "GatedPipeline.g.cs").SourceText.ToString();

        Assert.Contains("using var concurrencyGate = new global::System.Threading.SemaphoreSlim(2);", text);
        Assert.Contains("await concurrencyGate.WaitAsync(linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("segmentActivity?.SetTag(\"dovetail.segment\", \"foo\");", text);
        Assert.Contains("concurrencyGate.Release();", text);
    }

    [Fact]
    public void DoesNotEmitConcurrencyGate_WhenMaxConcurrencyAttributeIsAbsent()
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

            public partial class UngatedPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var result = RunGenerator(source, includeActivitySource: false);
        var generated = Assert.Single(result.GeneratedTrees);
        var text = generated.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.DoesNotContain("concurrencyGate", text);
        Assert.DoesNotContain("SemaphoreSlim", text);
    }

    [Fact]
    public async Task GeneratedPipeline_WithMaxConcurrency_NeverExceedsTheConfiguredLimit()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public static class Counters
            {
                public static int Current;
                public static int Peak;
            }

            public static class Tracker
            {
                public static async Task<int> TrackAsync(int value, CancellationToken ct)
                {
                    var current = Interlocked.Increment(ref Counters.Current);

                    int observedPeak;
                    do
                    {
                        observedPeak = Counters.Peak;
                        if (current <= observedPeak)
                        {
                            break;
                        }
                    }
                    while (Interlocked.CompareExchange(ref Counters.Peak, current, observedPeak) != observedPeak);

                    await Task.Delay(50, ct);

                    Interlocked.Decrement(ref Counters.Current);
                    return value;
                }
            }

            public readonly record struct R1(int Value);
            public readonly record struct R2(int Value);
            public readonly record struct R3(int Value);
            public readonly record struct R4(int Value);
            public readonly record struct R5(int Value);
            public readonly record struct R6(int Value);

            public class Worker1 : IPipelineSegment<int, R1> { public async Task<R1> ExecuteAsync(int value, CancellationToken ct) => new R1(await Tracker.TrackAsync(value, ct)); }
            public class Worker2 : IPipelineSegment<int, R2> { public async Task<R2> ExecuteAsync(int value, CancellationToken ct) => new R2(await Tracker.TrackAsync(value, ct)); }
            public class Worker3 : IPipelineSegment<int, R3> { public async Task<R3> ExecuteAsync(int value, CancellationToken ct) => new R3(await Tracker.TrackAsync(value, ct)); }
            public class Worker4 : IPipelineSegment<int, R4> { public async Task<R4> ExecuteAsync(int value, CancellationToken ct) => new R4(await Tracker.TrackAsync(value, ct)); }
            public class Worker5 : IPipelineSegment<int, R5> { public async Task<R5> ExecuteAsync(int value, CancellationToken ct) => new R5(await Tracker.TrackAsync(value, ct)); }
            public class Worker6 : IPipelineSegment<int, R6> { public async Task<R6> ExecuteAsync(int value, CancellationToken ct) => new R6(await Tracker.TrackAsync(value, ct)); }

            public readonly record struct Total(int Value);

            [MaxConcurrency(2)]
            public partial class GatedPipeline(
                [Segment] Worker1 one,
                [Segment] Worker2 two,
                [Segment] Worker3 three,
                [Segment] Worker4 four,
                [Segment] Worker5 five,
                [Segment] Worker6 six
            ) : IPipeline<int, Total>
            {
                [Segment]
                private static Total Aggregate(R1 one, R2 two, R3 three, R4 four, R5 five, R6 six) =>
                    new Total(one.Value + two.Value + three.Value + four.Value + five.Value + six.Value);
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.GatedPipeline")!;

        var workers = new[] { "Worker1", "Worker2", "Worker3", "Worker4", "Worker5", "Worker6" }
            .Select(name => Activator.CreateInstance(assembly.GetType($"Sample.{name}")!)!)
            .ToArray();
        var pipeline = Activator.CreateInstance(pipelineType, workers)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [1, CancellationToken.None])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var total = (int)result.GetType().GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(6, total);

        var countersType = assembly.GetType("Sample.Counters")!;
        var peak = (int)countersType.GetField("Peak")!.GetValue(null)!;

        Assert.True(peak <= 2, $"Expected peak concurrency to never exceed 2, but observed {peak}.");
        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task GeneratedPipeline_WithMaxConcurrency_GatesStaticSegmentMethodsToo()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Wrapped(int Value);

            public class FooSegment : IPipelineSegment<int, Wrapped>
            {
                public Task<Wrapped> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Wrapped(value));
            }

            [MaxConcurrency(1)]
            public partial class GatedPipeline([Segment] FooSegment foo) : IPipeline<int, string>
            {
                [Segment]
                private static string Stringify(Wrapped foo) => foo.Value.ToString();
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.GatedPipeline")!;
        var foo = Activator.CreateInstance(assembly.GetType("Sample.FooSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }
}
