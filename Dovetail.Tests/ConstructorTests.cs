using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class ConstructorTests
{
    [Fact]
    public async Task GeneratedPipeline_ForConventionalConstructor_ReadsSegmentsFromBackingField()
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

            public partial class NumberPipeline : IPipeline<int, string>
            {
                private readonly DoubleSegment _doubler;
                private readonly ToStringSegment _stringifier;

                public NumberPipeline([Segment] DoubleSegment doubler, [Segment] ToStringSegment stringifier)
                {
                    _doubler = doubler;
                    _stringifier = stringifier;
                }
            }
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
    public async Task GeneratedPipeline_ForConventionalConstructor_ReadsSegmentsFromAutoProperty()
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
                private FooSegment Foo { get; }

                public FooPipeline([Segment] FooSegment foo)
                {
                    Foo = foo;
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
    public void ReportsDiagnostic_WhenConventionalConstructorHasNoBackingMember()
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
                public FooPipeline([Segment] FooSegment foo)
                {
                }
            }
            """;

        AssertSingleDiagnostic(source, "DOVE010");
    }

    [Fact]
    public void ReportsDiagnostic_WhenConventionalConstructorBackingMemberIsAmbiguous()
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
                private readonly FooSegment _foo;
                private readonly FooSegment _fooAgain;

                public FooPipeline([Segment] FooSegment foo)
                {
                    _foo = foo;
                    _fooAgain = foo;
                }
            }
            """;

        AssertSingleDiagnostic(source, "DOVE011");
    }
}
