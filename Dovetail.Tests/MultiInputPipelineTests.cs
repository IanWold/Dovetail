using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class MultiInputPipelineTests
{
    [Fact]
    public void EmitsFanOutFanIn_ForMultiInputPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Doubled(int Value);
            public readonly record struct Tripled(long Value);
            public class FinalResult { public long Value { get; init; } }

            public class DoubleSegment : IPipelineSegment<int, Doubled>
            {
                public Task<Doubled> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Doubled(value * 2));
            }

            public class TripleSegment : IPipelineSegment<long, Tripled>
            {
                public Task<Tripled> ExecuteAsync(long value, CancellationToken ct) => Task.FromResult(new Tripled(value * 3));
            }

            public class SumSegment : IPipelineSegment<Doubled, Tripled, FinalResult>
            {
                public Task<FinalResult> ExecuteAsync(Doubled doubled, Tripled tripled, CancellationToken ct) =>
                    Task.FromResult(new FinalResult { Value = doubled.Value + tripled.Value });
            }

            public partial class SumPipeline(
                [Segment] DoubleSegment doubler,
                [Segment] TripleSegment tripler,
                [Segment] SumSegment sum
            ) : IPipeline<int, long, FinalResult>;
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var generated = Assert.Single(result.Results.Single().GeneratedSources, static s => s.HintName == "SumPipeline.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains("public async global::System.Threading.Tasks.Task<global::Sample.FinalResult> ExecuteAsync(int input1, long input2, global::System.Threading.CancellationToken token)", text);
        Assert.Contains("await doubler.ExecuteAsync(input1, linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("await tripler.ExecuteAsync(input2, linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("await sum.ExecuteAsync(await doublerTask.ConfigureAwait(false), await triplerTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("return await sumTask.ConfigureAwait(false);", text);
    }

    [Fact]
    public async Task GeneratedPipeline_ForMultiInputPipeline_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Doubled(int Value);
            public readonly record struct Tripled(long Value);
            public class FinalResult { public long Value { get; init; } }

            public class DoubleSegment : IPipelineSegment<int, Doubled>
            {
                public Task<Doubled> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Doubled(value * 2));
            }

            public class TripleSegment : IPipelineSegment<long, Tripled>
            {
                public Task<Tripled> ExecuteAsync(long value, CancellationToken ct) => Task.FromResult(new Tripled(value * 3));
            }

            public class SumSegment : IPipelineSegment<Doubled, Tripled, FinalResult>
            {
                public Task<FinalResult> ExecuteAsync(Doubled doubled, Tripled tripled, CancellationToken ct) =>
                    Task.FromResult(new FinalResult { Value = doubled.Value + tripled.Value });
            }

            public partial class SumPipeline(
                [Segment] DoubleSegment doubler,
                [Segment] TripleSegment tripler,
                [Segment] SumSegment sum
            ) : IPipeline<int, long, FinalResult>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.SumPipeline")!;
        var doubler = Activator.CreateInstance(assembly.GetType("Sample.DoubleSegment")!)!;
        var tripler = Activator.CreateInstance(assembly.GetType("Sample.TripleSegment")!)!;
        var sum = Activator.CreateInstance(assembly.GetType("Sample.SumSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, doubler, tripler, sum)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [5, 10L, CancellationToken.None])!;

        await task;

        var resultProperty = task.GetType().GetProperty("Result")!;
        var result = resultProperty.GetValue(task)!;
        var value = (long)result.GetType().GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(40L, value);
    }
}
