using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class PipelineChainingTests
{
    [Fact]
    public async Task PipelineImplementingMatchingSegmentShape_CanBeNestedAsASegment()
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

            public partial class InnerPipeline(
                [Segment] DoubleSegment doubler
            ) : IPipeline<int, Doubled>, IPipelineSegment<int, Doubled>;

            public class ToStringSegment : IPipelineSegment<Doubled, string>
            {
                public Task<string> ExecuteAsync(Doubled value, CancellationToken ct) => Task.FromResult(value.Value.ToString());
            }

            public partial class OuterPipeline(
                [Segment] InnerPipeline inner,
                [Segment] ToStringSegment stringifier
            ) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var outerType = assembly.GetType("Sample.OuterPipeline")!;
        var innerType = assembly.GetType("Sample.InnerPipeline")!;
        var doubler = Activator.CreateInstance(assembly.GetType("Sample.DoubleSegment")!)!;
        var inner = Activator.CreateInstance(innerType, doubler)!;
        var stringifier = Activator.CreateInstance(assembly.GetType("Sample.ToStringSegment")!)!;
        var outer = Activator.CreateInstance(outerType, inner, stringifier)!;

        Assert.Single(innerType.GetMethods(), m => m.Name == "ExecuteAsync" && m.GetParameters().Length == 2);

        var method = outerType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(outer, [10, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("20", result);
    }

    [Fact]
    public async Task MultiInputPipelineImplementingMatchingSegmentShape_CanBeNestedAsASegment()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct IntWrap(int Value);
            public readonly record struct StringWrap(string Value);
            public readonly record struct InnerResult(int IntPart, string StringPart);
            public class OuterResult { public string Value { get; init; } = ""; }

            public class InnerSegmentA : IPipelineSegment<int, IntWrap>
            {
                public Task<IntWrap> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new IntWrap(value));
            }

            public class InnerSegmentB : IPipelineSegment<string, StringWrap>
            {
                public Task<StringWrap> ExecuteAsync(string value, CancellationToken ct) => Task.FromResult(new StringWrap(value));
            }

            public class InnerJoin : IPipelineSegment<IntWrap, StringWrap, InnerResult>
            {
                public Task<InnerResult> ExecuteAsync(IntWrap a, StringWrap b, CancellationToken ct) =>
                    Task.FromResult(new InnerResult(a.Value, b.Value));
            }

            public partial class InnerPipeline(
                [Segment] InnerSegmentA a,
                [Segment] InnerSegmentB b,
                [Segment] InnerJoin join
            ) : IPipeline<int, string, InnerResult>, IPipelineSegment<int, string, InnerResult>;

            public class StringifySegment : IPipelineSegment<InnerResult, OuterResult>
            {
                public Task<OuterResult> ExecuteAsync(InnerResult value, CancellationToken ct) =>
                    Task.FromResult(new OuterResult { Value = $"{value.IntPart}-{value.StringPart}" });
            }

            public partial class OuterPipeline(
                [Segment] InnerPipeline inner,
                [Segment] StringifySegment stringifier
            ) : IPipeline<int, string, OuterResult>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var outerType = assembly.GetType("Sample.OuterPipeline")!;
        var innerType = assembly.GetType("Sample.InnerPipeline")!;
        var a = Activator.CreateInstance(assembly.GetType("Sample.InnerSegmentA")!)!;
        var b = Activator.CreateInstance(assembly.GetType("Sample.InnerSegmentB")!)!;
        var join = Activator.CreateInstance(assembly.GetType("Sample.InnerJoin")!)!;
        var inner = Activator.CreateInstance(innerType, a, b, join)!;
        var stringifier = Activator.CreateInstance(assembly.GetType("Sample.StringifySegment")!)!;
        var outer = Activator.CreateInstance(outerType, inner, stringifier)!;

        Assert.Single(innerType.GetMethods(), m => m.Name == "ExecuteAsync" && m.GetParameters().Length == 3);

        var method = outerType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(outer, [5, "hi", CancellationToken.None])!;

        await task;

        var resultProperty = task.GetType().GetProperty("Result")!;
        var result = resultProperty.GetValue(task)!;
        var value = (string)result.GetType().GetProperty("Value")!.GetValue(result)!;

        Assert.Equal("5-hi", value);
    }
}
