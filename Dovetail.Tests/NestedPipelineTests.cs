using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class NestedPipelineTests
{
    [Fact]
    public async Task GeneratedPipeline_ForNestedPipelineType_ProducesCorrectResult()
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

            public partial class Outer
            {
                public partial class NestedPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var outerType = assembly.GetType("Sample.Outer")!;
        var pipelineType = outerType.GetNestedType("NestedPipeline")!;
        var foo = Activator.CreateInstance(assembly.GetType("Sample.FooSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public async Task GeneratedPipeline_ForNestedPipelineType_WithMultipleChainedSegments_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Second;
            public class Third;
            public class Fourth;

            public class SecondSegment : IPipelineSegment<Second, Third>
            {
                public Task<Third> ExecuteAsync(Second value, CancellationToken ct) => Task.FromResult(new Third());
            }

            public class ThirdSegment : IPipelineSegment<Third, Fourth>
            {
                public Task<Fourth> ExecuteAsync(Third value, CancellationToken ct) => Task.FromResult(new Fourth());
            }

            public partial class Outer
            {
                public partial class NestedPipeline(
                    [Segment] SecondSegment second,
                    [Segment] ThirdSegment third
                ) : IPipeline<Second, Fourth>;
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var outerType = assembly.GetType("Sample.Outer")!;
        var pipelineType = outerType.GetNestedType("NestedPipeline")!;
        var second = Activator.CreateInstance(assembly.GetType("Sample.SecondSegment")!)!;
        var third = Activator.CreateInstance(assembly.GetType("Sample.ThirdSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, second, third)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var secondValue = Activator.CreateInstance(assembly.GetType("Sample.Second")!)!;
        var task = (Task)method.Invoke(pipeline, [secondValue, CancellationToken.None])!;
        
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        Assert.Equal("Sample.Fourth", result.GetType().FullName);
    }

    [Fact]
    public async Task GeneratedPipeline_ForDeeplyNestedPipelineType_ProducesCorrectResult()
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

            public partial class Outer
            {
                public partial struct MiddleStruct
                {
                    public partial record MiddleRecord
                    {
                        public partial class NestedPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
                    }
                }
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.Outer+MiddleStruct+MiddleRecord+NestedPipeline")!;
        var foo = Activator.CreateInstance(assembly.GetType("Sample.FooSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public void ReportsDiagnostic_WhenAncestorOfNestedPipelineIsNotPartial()
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

            public class Outer
            {
                public partial class NestedPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            }
            """;

        AssertSingleDiagnostic(source, "DOVE014");
    }

    [Fact]
    public void ReportsDiagnostic_WhenPipelineIsNestedInsideAGenericType()
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

            public partial class Outer<T>
            {
                public partial class NestedPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            }
            """;

        AssertSingleDiagnostic(source, "DOVE015");
    }
}
