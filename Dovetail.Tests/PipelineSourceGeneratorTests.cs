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
