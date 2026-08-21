using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class SegmentInterfaceInjectionTests
{
    [Fact]
    public void EmitsFanOutFanIn_ForSegmentInjectedByInterface()
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

            public partial class FooPipeline([Segment] IPipelineSegment<int, string> foo) : IPipeline<int, string>;
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        
        var generated = Assert.Single(result.Results.Single().GeneratedSources, static s => s.HintName == "FooPipeline.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains("await foo.ExecuteAsync(input, linkedToken).ConfigureAwait(false);", text);
    }

    [Fact]
    public async Task GeneratedPipeline_ForSegmentInjectedByInterface_ProducesCorrectResult()
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

            public partial class FooPipeline([Segment] IPipelineSegment<int, string> foo) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.FooPipeline")!;
        var foo = Activator.CreateInstance(assembly.GetType("Sample.FooSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public async Task GeneratedPipeline_ForSegmentInjectedByInterfaceOnConventionalConstructor_ProducesCorrectResult()
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

            public partial class FooPipeline : IPipeline<int, string>
            {
                private readonly IPipelineSegment<int, string> _foo;

                public FooPipeline([Segment] IPipelineSegment<int, string> foo)
                {
                    _foo = foo;
                }
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.FooPipeline")!;
        var foo = Activator.CreateInstance(assembly.GetType("Sample.FooSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public async Task GeneratedPipeline_ForMixOfInterfaceAndConcreteSegments_ProducesCorrectResult()
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
                [Segment] IPipelineSegment<int, Doubled> doubler,
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
}
