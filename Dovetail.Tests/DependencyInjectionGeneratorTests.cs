using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class DependencyInjectionGeneratorTests
{
    internal static GeneratorDriverRunResult RunServiceCollectionGenerator(string source, bool includeServiceCollection = true) =>
        CSharpGeneratorDriver.Create(new ServiceCollectionExtensionsGenerator())
        .RunGenerators(CreateCompilation(source, includeServiceCollection)).GetRunResult();

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
}
