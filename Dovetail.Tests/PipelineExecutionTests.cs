using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class PipelineExecutionTests
{
    [Fact]
    public void EmitsFanOutFanIn_ForDiamondDependencyPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class RootResult { public int Value { get; init; } }
            public class LeftResult { public int Value { get; init; } }
            public class RightResult { public int Value { get; init; } }
            public class FinalResult { public int Value { get; init; } }

            public class RootSegment : IPipelineSegment<int, RootResult>
            {
                public Task<RootResult> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new RootResult { Value = value });
            }

            public class LeftSegment : IPipelineSegment<RootResult, LeftResult>
            {
                public Task<LeftResult> ExecuteAsync(RootResult root, CancellationToken ct) => Task.FromResult(new LeftResult { Value = root.Value + 1 });
            }

            public class RightSegment : IPipelineSegment<RootResult, RightResult>
            {
                public Task<RightResult> ExecuteAsync(RootResult root, CancellationToken ct) => Task.FromResult(new RightResult { Value = root.Value + 2 });
            }

            public class JoinSegment : IPipelineSegment<RootResult, LeftResult, RightResult, FinalResult>
            {
                public Task<FinalResult> ExecuteAsync(RootResult root, LeftResult left, RightResult right, CancellationToken ct) =>
                    Task.FromResult(new FinalResult { Value = root.Value + left.Value + right.Value });
            }

            public partial class DiamondPipeline(
                [Segment] RootSegment root,
                [Segment] LeftSegment left,
                [Segment] RightSegment right,
                [Segment] JoinSegment join
            ) : IPipeline<int, FinalResult>;
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var generated = Assert.Single(result.Results.Single().GeneratedSources, static s => s.HintName == "DiamondPipeline.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains("public async global::System.Threading.Tasks.Task<global::Sample.FinalResult> ExecuteAsync(int input, global::System.Threading.CancellationToken token)", text);
        Assert.Contains("var rootTask = RootAsync();", text);
        Assert.Contains("var leftTask = LeftAsync();", text);
        Assert.Contains("var rightTask = RightAsync();", text);
        Assert.Contains("var joinTask = JoinAsync();", text);
        Assert.Contains("return await joinTask.ConfigureAwait(false);", text);
        Assert.Contains("Task.WhenAll(rootTask, leftTask, rightTask)", text);
        Assert.Contains("await root.ExecuteAsync(input, linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("await left.ExecuteAsync(await rootTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("await join.ExecuteAsync(await rootTask.ConfigureAwait(false), await leftTask.ConfigureAwait(false), await rightTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);", text);
    }

    [Fact]
    public async Task GeneratedPipeline_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Doubled(int Value);

            public class DoubleSegment : IPipelineSegment<int, Doubled>
            {
                public Task<Doubled> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Doubled(value * 2));
            }

            public class ToStringSegment : IPipelineSegment<Doubled, string>
            {
                public Task<string> ExecuteAsync(Doubled value, CancellationToken ct) => Task.FromResult(value.Value.ToString());
            }

            public partial class NumberPipeline(
                [Segment] DoubleSegment doubler,
                [Segment] ToStringSegment stringifier
            ) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.NumberPipeline")!;
        var doubler = Activator.CreateInstance(assembly.GetType("Sample.DoubleSegment")!)!;
        var stringifier = Activator.CreateInstance(assembly.GetType("Sample.ToStringSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, doubler, stringifier)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("42", result);
    }

    [Fact]
    public async Task GeneratedPipeline_PropagatesSegmentFailure()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Doubled(int Value);

            public class DoubleSegment : IPipelineSegment<int, Doubled>
            {
                public Task<Doubled> ExecuteAsync(int value, CancellationToken ct) => throw new InvalidOperationException("boom");
            }

            public class ToStringSegment : IPipelineSegment<Doubled, string>
            {
                public Task<string> ExecuteAsync(Doubled value, CancellationToken ct) => Task.FromResult(value.Value.ToString());
            }

            public partial class NumberPipeline(
                [Segment] DoubleSegment doubler,
                [Segment] ToStringSegment stringifier
            ) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.NumberPipeline")!;
        var doubler = Activator.CreateInstance(assembly.GetType("Sample.DoubleSegment")!)!;
        var stringifier = Activator.CreateInstance(assembly.GetType("Sample.ToStringSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, doubler, stringifier)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task GeneratedPipeline_ResolvesCorrectly_WhenSegmentsAreDeclaredOutOfDependencyOrder()
    {
        // "leaf" is declared before "root" even though it depends on root's result. The
        // generator must topologically sort its "var xTask = XAsync();" declarations rather
        // than emitting them in raw declaration order, or leaf's task would call into root's
        // task variable before it's assigned (CS0165) — this was always latently true, just
        // never exercised because every prior test happened to declare segments in an
        // already-dependency-first order.
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class RootResult { public int Value { get; init; } }
            public class LeafResult { public int Value { get; init; } }

            public class LeafSegment : IPipelineSegment<RootResult, LeafResult>
            {
                public Task<LeafResult> ExecuteAsync(RootResult root, CancellationToken ct) => Task.FromResult(new LeafResult { Value = root.Value + 1 });
            }

            public class RootSegment : IPipelineSegment<int, RootResult>
            {
                public Task<RootResult> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new RootResult { Value = value });
            }

            public partial class OutOfOrderPipeline([Segment] LeafSegment leaf, [Segment] RootSegment root) : IPipeline<int, LeafResult>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.OutOfOrderPipeline")!;
        var leaf = Activator.CreateInstance(assembly.GetType("Sample.LeafSegment")!)!;
        var root = Activator.CreateInstance(assembly.GetType("Sample.RootSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, leaf, root)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [10, CancellationToken.None])!;

        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (int)result.GetType().GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(11, value);
    }
}
