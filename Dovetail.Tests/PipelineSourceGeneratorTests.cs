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
                public Task<RootResult> RunAsync(int value, CancellationToken ct) => Task.FromResult(new RootResult { Value = value });
            }

            public class LeftSegment : IPipelineSegment<RootResult, LeftResult>
            {
                public Task<LeftResult> RunAsync(RootResult root, CancellationToken ct) => Task.FromResult(new LeftResult { Value = root.Value + 1 });
            }

            public class RightSegment : IPipelineSegment<RootResult, RightResult>
            {
                public Task<RightResult> RunAsync(RootResult root, CancellationToken ct) => Task.FromResult(new RightResult { Value = root.Value + 2 });
            }

            public class JoinSegment : IPipelineSegment<RootResult, LeftResult, RightResult, FinalResult>
            {
                public Task<FinalResult> RunAsync(RootResult root, LeftResult left, RightResult right, CancellationToken ct) =>
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
        var generated = Assert.Single(result.GeneratedTrees);
        var text = generated.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.Contains("public async global::System.Threading.Tasks.Task<global::Sample.FinalResult> ExecuteAsync(int input, global::System.Threading.CancellationToken token)", text);
        Assert.Contains("var rootTask = RootAsync();", text);
        Assert.Contains("var leftTask = LeftAsync();", text);
        Assert.Contains("var rightTask = RightAsync();", text);
        Assert.Contains("var joinTask = JoinAsync();", text);
        Assert.Contains("return await joinTask.ConfigureAwait(false);", text);
        Assert.Contains("Task.WhenAll(rootTask, leftTask, rightTask)", text);
        Assert.Contains("await root.RunAsync(input, linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("await left.RunAsync(await rootTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);", text);
        Assert.Contains("await join.RunAsync(await rootTask.ConfigureAwait(false), await leftTask.ConfigureAwait(false), await rightTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);", text);
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
                public Task<Doubled> RunAsync(int value, CancellationToken ct) => Task.FromResult(new Doubled(value * 2));
            }

            public class ToStringSegment : IPipelineSegment<Doubled, string>
            {
                public Task<string> RunAsync(Doubled value, CancellationToken ct) => Task.FromResult(value.Value.ToString());
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
                public Task<Doubled> RunAsync(int value, CancellationToken ct) => throw new InvalidOperationException("boom");
            }

            public class ToStringSegment : IPipelineSegment<Doubled, string>
            {
                public Task<string> RunAsync(Doubled value, CancellationToken ct) => Task.FromResult(value.Value.ToString());
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

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> RunAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, int>;
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

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> RunAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, int>;
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
                public Task<Doubled> RunAsync(int value, CancellationToken ct) => Task.FromResult(new Doubled(value * 2));
            }

            public class ToStringSegment : IPipelineSegment<Doubled, string>
            {
                public Task<string> RunAsync(Doubled value, CancellationToken ct) => Task.FromResult(value.Value.ToString());
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
                public Task<int> RunAsync(int value, CancellationToken ct) => Task.FromResult(value);
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
                public Task<int> RunAsync(int value, CancellationToken ct) => Task.FromResult(value);
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
                public Task<int> RunAsync(int value, CancellationToken ct) => Task.FromResult(value);
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
                public Task<int> RunAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public class BarSegment : IPipelineSegment<int, int>
            {
                public Task<int> RunAsync(int value, CancellationToken ct) => Task.FromResult(value);
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
                public Task<int> RunAsync(string value, CancellationToken ct) => Task.FromResult(value.Length);
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
                public Task<A> RunAsync(B value, CancellationToken ct) => Task.FromResult(new A());
            }

            public class SegB : IPipelineSegment<A, B>
            {
                public Task<B> RunAsync(A value, CancellationToken ct) => Task.FromResult(new B());
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
                public Task<Foo> RunAsync(int value, CancellationToken ct) => Task.FromResult(new Foo());
            }

            public class OrphanSegment : IPipelineSegment<int, Bar>
            {
                public Task<Bar> RunAsync(int value, CancellationToken ct) => Task.FromResult(new Bar());
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
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var driver = CSharpGeneratorDriver.Create(new PipelineSourceGenerator());
        return driver.RunGenerators(CreateCompilation(source)).GetRunResult();
    }

    private static GeneratorDriverRunResult RunServiceCollectionGenerator(string source, bool includeServiceCollection = true)
    {
        var driver = CSharpGeneratorDriver.Create(new ServiceCollectionExtensionsGenerator());
        return driver.RunGenerators(CreateCompilation(source, includeServiceCollection)).GetRunResult();
    }

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

    private static CSharpCompilation CreateCompilation(string source, bool includeServiceCollection = true) =>
        CSharpCompilation.Create(
            assemblyName: $"Dovetail.Tests.Generated.{Guid.NewGuid():N}",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly =>
                    !assembly.IsDynamic
                    && !string.IsNullOrEmpty(assembly.Location)
                    && (includeServiceCollection || !(assembly.GetName().Name ?? "").StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal))
                )
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location)
            ),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
}
