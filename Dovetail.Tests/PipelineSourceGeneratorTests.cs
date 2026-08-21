using System.Diagnostics;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;

namespace Dovetail.Tests;

public class PipelineSourceGeneratorTests
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
    public void EmitsAddPipelines_RegisteringEverySegmentAndPipeline()
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

            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var result = RunServiceCollectionGenerator(source);

        var generated = Assert.Single(result.GeneratedTrees);
        var text = generated.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.Contains("namespace Microsoft.Extensions.DependencyInjection;", text);
        Assert.Contains("public static IServiceCollection AddPipelines(this IServiceCollection services)", text);
        Assert.Contains("services.AddTransient<global::Sample.FooSegment>();", text);
        Assert.Contains("services.AddTransient<global::Sample.FooPipeline>();", text);
    }

    [Fact]
    public void DoesNotEmitAddPipelines_WhenServiceCollectionIsUnavailable()
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

            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var result = RunServiceCollectionGenerator(source, includeServiceCollection: false);

        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public async Task AddPipelines_RegistersAndResolvesAWorkingPipeline()
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

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator(), new ServiceCollectionExtensionsGenerator());

        var services = new ServiceCollection();
        var extensionsType = assembly.GetType("Microsoft.Extensions.DependencyInjection.DovetailServiceCollectionExtensions")!;

        extensionsType.GetMethod("AddPipelines")!.Invoke(null, [services]);

        var provider = services.BuildServiceProvider();
        var pipelineType = assembly.GetType("Sample.NumberPipeline")!;
        var pipeline = provider.GetRequiredService(pipelineType);

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("42", result);
    }

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

    [Fact]
    public void EmitsTracing_WhenActivitySourceIsAvailable()
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

            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var sources = result.Results.Single().GeneratedSources;
        Assert.Equal(2, sources.Length);

        var activitySource = Assert.Single(sources, static s => s.HintName == "DovetailActivitySource.g.cs").SourceText.ToString();
        Assert.Contains("internal static class DovetailActivitySource", activitySource);
        Assert.Contains("new global::System.Diagnostics.ActivitySource(\"Dovetail\")", activitySource);

        var pipelineSource = Assert.Single(sources, static s => s.HintName == "FooPipeline.g.cs").SourceText.ToString();
        Assert.Contains("using var activity = global::Dovetail.DovetailActivitySource.Instance.StartActivity(\"FooPipeline.ExecuteAsync\");", pipelineSource);
        Assert.Contains("activity?.SetTag(\"dovetail.pipeline\", \"Sample.FooPipeline\");", pipelineSource);
        Assert.Contains("using var segmentActivity = global::Dovetail.DovetailActivitySource.Instance.StartActivity(\"FooPipeline.foo\");", pipelineSource);
        Assert.Contains("segmentActivity?.SetTag(\"dovetail.segment\", \"foo\");", pipelineSource);
        Assert.Contains("segmentActivity?.SetTag(\"dovetail.segment.type\", \"Sample.FooSegment\");", pipelineSource);
        Assert.Contains("segmentActivity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, ex.Message);", pipelineSource);
    }

    [Fact]
    public void DoesNotEmitTracing_WhenActivitySourceIsUnavailable()
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

            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var result = RunGenerator(source, includeActivitySource: false);

        Assert.Empty(result.Diagnostics);

        var pipelineSource = Assert.Single(result.Results.Single().GeneratedSources);

        Assert.Equal("FooPipeline.g.cs", pipelineSource.HintName);
        Assert.DoesNotContain("StartActivity", pipelineSource.SourceText.ToString());
    }

    [Fact]
    public async Task GeneratedPipeline_RecordsActivitiesForPipelineAndEachSegment()
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

        var startedActivities = new System.Collections.Concurrent.ConcurrentBag<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == "Dovetail",
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => startedActivities.Add(activity),
        };
        
        ActivitySource.AddActivityListener(listener);

        try
        {
            var pipelineType = assembly.GetType("Sample.NumberPipeline")!;
            var doubler = Activator.CreateInstance(assembly.GetType("Sample.DoubleSegment")!)!;
            var stringifier = Activator.CreateInstance(assembly.GetType("Sample.ToStringSegment")!)!;
            var pipeline = Activator.CreateInstance(pipelineType, doubler, stringifier)!;

            var method = pipelineType.GetMethod("ExecuteAsync")!;
            var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
            var result = await task;

            Assert.Equal("42", result);
        }
        finally
        {
            listener.Dispose();
        }

        var names = startedActivities.Select(a => a.OperationName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["NumberPipeline.ExecuteAsync", "NumberPipeline.doubler", "NumberPipeline.stringifier"], names);

        var pipelineActivity = Assert.Single(startedActivities, a => a.OperationName == "NumberPipeline.ExecuteAsync");
        Assert.Equal("Sample.NumberPipeline", pipelineActivity.GetTagItem("dovetail.pipeline"));

        var doublerActivity = Assert.Single(startedActivities, a => a.OperationName == "NumberPipeline.doubler");
        Assert.Equal("doubler", doublerActivity.GetTagItem("dovetail.segment"));
        Assert.Equal("Sample.DoubleSegment", doublerActivity.GetTagItem("dovetail.segment.type"));
    }

    [Fact]
    public void PipelineSourceGenerator_SkipsRegeneration_WhenAnUnrelatedFileChanges()
    {
        const string pipelineSource = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var (segmentParametersReason, sourceOutputReason) = RunTwiceAndGetStepReasons(
            new PipelineSourceGenerator().AsSourceGenerator(),
            pipelineSource,
            PipelineSourceGenerator.SegmentParametersTrackingName
        );

        Assert.Equal(IncrementalStepRunReason.Cached, segmentParametersReason);
        Assert.Equal(IncrementalStepRunReason.Cached, sourceOutputReason);
    }

    [Fact]
    public void ServiceCollectionExtensionsGenerator_SkipsRegeneration_WhenAnUnrelatedFileChanges()
    {
        const string pipelineSource = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var (registeredTypesReason, sourceOutputReason) = RunTwiceAndGetStepReasons(
            new ServiceCollectionExtensionsGenerator().AsSourceGenerator(),
            pipelineSource,
            ServiceCollectionExtensionsGenerator.RegisteredTypesTrackingName
        );

        Assert.Equal(IncrementalStepRunReason.Cached, registeredTypesReason);
        Assert.Equal(IncrementalStepRunReason.Cached, sourceOutputReason);
    }

    private static (IncrementalStepRunReason NamedStep, IncrementalStepRunReason SourceOutput) RunTwiceAndGetStepReasons(ISourceGenerator generator, string pipelineSource, string trackingName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var unrelatedTreeV1 = CSharpSyntaxTree.ParseText("namespace Sample; public class Unrelated { public int Value => 1; }", cancellationToken: cancellationToken);
        var unrelatedTreeV2 = CSharpSyntaxTree.ParseText("namespace Sample; public class Unrelated { public int Value => 2; }", cancellationToken: cancellationToken);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"Dovetail.Tests.Generated.{Guid.NewGuid():N}",
            syntaxTrees: [CSharpSyntaxTree.ParseText(pipelineSource, cancellationToken: cancellationToken), unrelatedTreeV1],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true)
        );

        driver = driver.RunGenerators(compilation, cancellationToken);

        var updatedCompilation = compilation.ReplaceSyntaxTree(unrelatedTreeV1, unrelatedTreeV2);
        
        driver = driver.RunGenerators(updatedCompilation, cancellationToken);

        var result = driver.GetRunResult().Results.Single();
        var namedStepReason = result.TrackedSteps[trackingName].Single().Outputs.Single().Reason;
        var sourceOutputReason = result.TrackedSteps["SourceOutput"].Single().Outputs.Single().Reason;

        return (namedStepReason, sourceOutputReason);
    }

    [Fact]
    public void ReportsDiagnostic_WhenContainingTypeIsNotPartial()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public class NotPartialPipeline([Segment] FooSegment foo) : IPipeline<int, int>;
            """;

        AssertSingleDiagnostic(source, "DOVE001");
    }

    [Fact]
    public void ReportsDiagnostic_WhenContainingTypeDoesNotImplementIPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class NotAPipeline([Segment] FooSegment foo);
            """;

        AssertSingleDiagnostic(source, "DOVE002");
    }

    [Fact]
    public void ReportsDiagnostic_WhenSegmentTypeDoesNotImplementIPipelineSegment()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class NotASegment;

            public partial class BadPipeline([Segment] NotASegment notSegment) : IPipeline<NotASegment, NotASegment>;
            """;

        AssertSingleDiagnostic(source, "DOVE003");
    }

    [Fact]
    public void ReportsDiagnostic_WhenNoSegmentProducesThePipelineResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class MissingTerminalPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        AssertSingleDiagnostic(source, "DOVE004");
    }

    [Fact]
    public void ReportsDiagnostic_WhenTwoSegmentsProduceTheSameType()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public class BarSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class DuplicatePipeline([Segment] FooSegment foo, [Segment] BarSegment bar) : IPipeline<int, int>;
            """;

        AssertSingleDiagnostic(source, "DOVE005");
    }

    [Fact]
    public void ReportsDiagnostic_WhenASegmentInputIsUnresolved()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<string, int>
            {
                public Task<int> ExecuteAsync(string value, CancellationToken ct) => Task.FromResult(value.Length);
            }

            public partial class UnresolvedPipeline([Segment] FooSegment foo) : IPipeline<int, int>;
            """;

        AssertSingleDiagnostic(source, "DOVE006");
    }

    [Fact]
    public void ReportsDiagnostic_WhenSegmentsFormACycle()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class A;
            public class B;

            public class SegA : IPipelineSegment<B, A>
            {
                public Task<A> ExecuteAsync(B value, CancellationToken ct) => Task.FromResult(new A());
            }

            public class SegB : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B());
            }

            public partial class CyclePipeline([Segment] SegA segA, [Segment] SegB segB) : IPipeline<A>;
            """;

        AssertSingleDiagnostic(source, "DOVE007");
    }

    [Fact]
    public void ReportsDiagnostic_WhenASegmentIsUnreachableFromTheResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Foo;
            public class Bar;

            public class FooSegment : IPipelineSegment<int, Foo>
            {
                public Task<Foo> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Foo());
            }

            public class OrphanSegment : IPipelineSegment<int, Bar>
            {
                public Task<Bar> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Bar());
            }

            public partial class OrphanPipeline([Segment] FooSegment foo, [Segment] OrphanSegment orphan) : IPipeline<int, Foo>;
            """;

        AssertSingleDiagnostic(source, "DOVE008");
    }

    [Fact]
    public void ReportsDiagnostic_WhenThePipelineDeclaresTheSameInputTypeMoreThanOnce()
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

            public partial class DuplicateInputPipeline([Segment] FooSegment foo) : IPipeline<int, int, string>;
            """;

        AssertSingleDiagnostic(source, "DOVE009");
    }

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
    public async Task GeneratedPipeline_ResolvesCorrectly_WhenSegmentsAreDeclaredOutOfDependencyOrder()
    {
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

    [Fact]
    public async Task GeneratedPipeline_ForClosedGenericSegmentType_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Wrapper<T> : IPipelineSegment<int, T>
            {
                private readonly T _value;
                public Wrapper(T value) => _value = value;
                public Task<T> ExecuteAsync(int input, CancellationToken ct) => Task.FromResult(_value);
            }

            public partial class MyPipeline([Segment] Wrapper<string> wrapper) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.MyPipeline")!;
        var wrapperType = assembly.GetType("Sample.Wrapper`1")!.MakeGenericType(typeof(string));
        var wrapper = Activator.CreateInstance(wrapperType, "hello")!;
        var pipeline = Activator.CreateInstance(pipelineType, wrapper)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [1, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("hello", result);
    }

    [Fact]
    public void EmitsFanOutFanIn_ForGenericPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Input { public int Value { get; init; } }
            public class Result { public int Value { get; init; } }

            public class FirstSegment<T> : IPipelineSegment<Input, T> where T : new()
            {
                public Task<T> ExecuteAsync(Input input, CancellationToken ct) => Task.FromResult(new T());
            }

            public class SecondSegment<T> : IPipelineSegment<T, Result>
            {
                public Task<Result> ExecuteAsync(T value, CancellationToken ct) => Task.FromResult(new Result());
            }

            public partial class MyPipeline<T>(
                [Segment] FirstSegment<T> first,
                [Segment] SecondSegment<T> second
            ) : IPipeline<Input, Result> where T : new();
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        
        var generated = Assert.Single(result.Results.Single().GeneratedSources, static s => s.HintName == "MyPipeline.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains("partial class MyPipeline<T>", text);
        Assert.Contains("async global::System.Threading.Tasks.Task<T> FirstAsync()", text);
        Assert.Contains("await second.ExecuteAsync(await firstTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);", text);
    }

    [Fact]
    public async Task GeneratedPipeline_ForGenericPipelineWithGenericSegments_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Input { public int Value { get; init; } }

            public readonly record struct Boxed<T>(T Value);

            public class FirstSegment<T> : IPipelineSegment<Input, T>
            {
                private readonly T _value;
                public FirstSegment(T value) => _value = value;
                public Task<T> ExecuteAsync(Input input, CancellationToken ct) => Task.FromResult(_value);
            }

            public class SecondSegment<T> : IPipelineSegment<T, Boxed<T>>
            {
                public Task<Boxed<T>> ExecuteAsync(T value, CancellationToken ct) => Task.FromResult(new Boxed<T>(value));
            }

            public partial class MyPipeline<T>(
                [Segment] FirstSegment<T> first,
                [Segment] SecondSegment<T> second
            ) : IPipeline<Input, Boxed<T>>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.MyPipeline`1")!.MakeGenericType(typeof(string));
        var firstType = assembly.GetType("Sample.FirstSegment`1")!.MakeGenericType(typeof(string));
        var secondType = assembly.GetType("Sample.SecondSegment`1")!.MakeGenericType(typeof(string));

        var first = Activator.CreateInstance(firstType, "hello")!;
        var second = Activator.CreateInstance(secondType)!;
        var pipeline = Activator.CreateInstance(pipelineType, first, second)!;
        var input = Activator.CreateInstance(assembly.GetType("Sample.Input")!)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [input, CancellationToken.None])!;
        
        await task;

        var boxed = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (string)boxed.GetType().GetProperty("Value")!.GetValue(boxed)!;

        Assert.Equal("hello", value);
    }

    [Fact]
    public async Task GeneratedPipeline_ForNestedGenericPipeline_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment<T> : IPipelineSegment<int, T>
            {
                private readonly T _value;
                public FooSegment(T value) => _value = value;
                public Task<T> ExecuteAsync(int input, CancellationToken ct) => Task.FromResult(_value);
            }

            public partial class Outer
            {
                public partial class NestedPipeline<T>([Segment] FooSegment<T> foo) : IPipeline<int, T>;
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var outerType = assembly.GetType("Sample.Outer")!;
        var pipelineType = outerType.GetNestedType("NestedPipeline`1")!.MakeGenericType(typeof(string));
        var fooType = assembly.GetType("Sample.FooSegment`1")!.MakeGenericType(typeof(string));

        var foo = Activator.CreateInstance(fooType, "nested-and-generic")!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [1, CancellationToken.None])!;
        
        await task;

        var result = (string)task.GetType().GetProperty("Result")!.GetValue(task)!;

        Assert.Equal("nested-and-generic", result);
    }

    [Fact]
    public void EmitsFanOutFanIn_ForStaticMethodSegmentOnGenericPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Input { public int Value { get; init; } }
            public readonly record struct Boxed<T>(T Value);

            public class FirstSegment<T> : IPipelineSegment<Input, T>
            {
                private readonly T _value;
                public FirstSegment(T value) => _value = value;
                public Task<T> ExecuteAsync(Input input, CancellationToken ct) => Task.FromResult(_value);
            }

            public partial class MyPipeline<T>(
                [Segment] FirstSegment<T> first
            ) : IPipeline<Input, Boxed<T>>
            {
                [Segment]
                private static Boxed<T> Wrap(T value) => new(value);
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        
        var generated = Assert.Single(result.Results.Single().GeneratedSources, static s => s.HintName == "MyPipeline.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains("partial class MyPipeline<T>", text);
        Assert.Contains("async global::System.Threading.Tasks.Task<global::Sample.Boxed<T>> WrapAsync()", text);
        Assert.Contains("return Wrap(await firstTask.ConfigureAwait(false));", text);
    }

    [Fact]
    public async Task GeneratedPipeline_ForStaticMethodSegmentOnGenericPipeline_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Input { public int Value { get; init; } }
            public readonly record struct Boxed<T>(T Value);

            public class FirstSegment<T> : IPipelineSegment<Input, T>
            {
                private readonly T _value;
                public FirstSegment(T value) => _value = value;
                public Task<T> ExecuteAsync(Input input, CancellationToken ct) => Task.FromResult(_value);
            }

            public partial class MyPipeline<T>(
                [Segment] FirstSegment<T> first
            ) : IPipeline<Input, Boxed<T>>
            {
                [Segment]
                private static Boxed<T> Wrap(T value) => new(value);
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.MyPipeline`1")!.MakeGenericType(typeof(string));
        var firstType = assembly.GetType("Sample.FirstSegment`1")!.MakeGenericType(typeof(string));

        var first = Activator.CreateInstance(firstType, "hello")!;
        var pipeline = Activator.CreateInstance(pipelineType, first)!;
        var input = Activator.CreateInstance(assembly.GetType("Sample.Input")!)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [input, CancellationToken.None])!;
        
        await task;

        var boxed = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (string)boxed.GetType().GetProperty("Value")!.GetValue(boxed)!;

        Assert.Equal("hello", value);
    }

    private static void AssertSingleDiagnostic(string source, string expectedId)
    {
        var result = RunGenerator(source);

        Assert.Empty(result.GeneratedTrees);

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(expectedId, diagnostic.Id);
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.True(diagnostic.Location.IsInSource);
    }

    private static GeneratorDriverRunResult RunGenerator(string source, bool includeActivitySource = true) =>
        CSharpGeneratorDriver.Create(new PipelineSourceGenerator())
        .RunGenerators(CreateCompilation(source, includeActivitySource: includeActivitySource)).GetRunResult();

    private static GeneratorDriverRunResult RunServiceCollectionGenerator(string source, bool includeServiceCollection = true) =>
        CSharpGeneratorDriver.Create(new ServiceCollectionExtensionsGenerator())
        .RunGenerators(CreateCompilation(source, includeServiceCollection)).GetRunResult();

    private static Assembly CompileAndLoad(string source, params IIncrementalGenerator[] generators)
    {
        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(generators);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics);

        using var stream = new MemoryStream();
        EmitResult emitResult = outputCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        return Assembly.Load(stream.ToArray());
    }

    private static CSharpCompilation CreateCompilation(string source, bool includeServiceCollection = true, bool includeActivitySource = true) =>
        CSharpCompilation.Create(
            assemblyName: $"Dovetail.Tests.Generated.{Guid.NewGuid():N}",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly =>
                    !assembly.IsDynamic
                    && !string.IsNullOrEmpty(assembly.Location)
                    && (includeServiceCollection || !(assembly.GetName().Name ?? "").StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal))
                    && (includeActivitySource || !(assembly.GetName().Name ?? "").StartsWith("System.Diagnostics.DiagnosticSource", StringComparison.Ordinal))
                )
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location)
            ),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
}
