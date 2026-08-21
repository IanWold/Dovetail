using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class PipelineDeclarationKindTests
{
    [Fact]
    public async Task GeneratedPipeline_ForRecordPipeline_ProducesCorrectResult()
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

            public partial record RecordPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.RecordPipeline")!;
        var foo = Activator.CreateInstance(assembly.GetType("Sample.FooSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public async Task GeneratedPipeline_ForRecordStructPipeline_ProducesCorrectResult()
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

            public partial record struct RecordStructPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.RecordStructPipeline")!;
        var foo = Activator.CreateInstance(assembly.GetType("Sample.FooSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public async Task GeneratedPipeline_ForStructPipelineWithMixOfInstanceAndStaticSegments_ProducesCorrectResult()
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

            public partial struct StructPipeline(
                [Segment] DoubleSegment doubler
            ) : IPipeline<int, string>
            {
                [Segment]
                private static string Stringify(Doubled value) => value.Value.ToString();
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.StructPipeline")!;
        var doubler = Activator.CreateInstance(assembly.GetType("Sample.DoubleSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, doubler)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("42", result);
    }
}
