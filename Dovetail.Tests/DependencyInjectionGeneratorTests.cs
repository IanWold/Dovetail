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

    [Fact]
    public void EmitsAddPipelines_WithLifetimeAttribute_UsesTheGivenLifetime()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;
            using Dovetail.DependencyInjection;

            namespace Sample;

            [Lifetime(ServiceLifetime.Singleton)]
            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            [Lifetime(ServiceLifetime.Scoped)]
            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var result = RunServiceCollectionGenerator(source);

        var generated = Assert.Single(result.GeneratedTrees);
        var text = generated.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.Contains("services.AddSingleton<global::Sample.FooSegment>();", text);
        Assert.Contains("services.AddScoped<global::Sample.FooPipeline>();", text);
    }

    [Fact]
    public void EmitsAddPipelines_RegisteringStructAndRecordPipelinesAndSegments()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial record struct FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var result = RunServiceCollectionGenerator(source);

        var generated = Assert.Single(result.GeneratedTrees);
        var text = generated.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.Contains("services.AddTransient(typeof(global::Sample.FooSegment), typeof(global::Sample.FooSegment));", text);
        Assert.Contains("services.AddTransient(typeof(global::Sample.FooPipeline), typeof(global::Sample.FooPipeline));", text);
    }

    [Fact]
    public void EmitsAddPipelines_RegisteringGenericPipelinesAndSegments_AsOpenGenericTypes()
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

            public partial class MyPipeline<T>([Segment] Wrapper<T> wrapper) : IPipeline<int, T>;
            """;

        var result = RunServiceCollectionGenerator(source);

        var generated = Assert.Single(result.GeneratedTrees);
        var text = generated.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.Contains("services.AddTransient(typeof(global::Sample.Wrapper<>), typeof(global::Sample.Wrapper<>));", text);
        Assert.Contains("services.AddTransient(typeof(global::Sample.MyPipeline<>), typeof(global::Sample.MyPipeline<>));", text);
    }

    [Fact]
    public void AddPipelines_ResolvesSingletonSegment_AsTheSameInstanceAcrossPipelines()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;
            using Dovetail.DependencyInjection;

            namespace Sample;

            [Lifetime(ServiceLifetime.Singleton)]
            public class CounterSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class FooPipeline([Segment] CounterSegment counter) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator(), new ServiceCollectionExtensionsGenerator());

        var services = new ServiceCollection();
        var extensionsType = assembly.GetType("Microsoft.Extensions.DependencyInjection.DovetailServiceCollectionExtensions")!;
        extensionsType.GetMethod("AddPipelines")!.Invoke(null, [services]);

        var provider = services.BuildServiceProvider();
        var segmentType = assembly.GetType("Sample.CounterSegment")!;

        var first = provider.GetRequiredService(segmentType);
        var second = provider.GetRequiredService(segmentType);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task AddPipelines_RegistersAndResolvesAGenericPipeline_ViaOpenGenericRegistration()
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

            public partial class MyPipeline<T>([Segment] Wrapper<T> wrapper) : IPipeline<int, T>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator(), new ServiceCollectionExtensionsGenerator());

        var services = new ServiceCollection();
        var wrapperType = assembly.GetType("Sample.Wrapper`1")!.MakeGenericType(typeof(string));
        services.AddTransient(wrapperType, _ => Activator.CreateInstance(wrapperType, "hello")!);

        var extensionsType = assembly.GetType("Microsoft.Extensions.DependencyInjection.DovetailServiceCollectionExtensions")!;
        extensionsType.GetMethod("AddPipelines")!.Invoke(null, [services]);

        var provider = services.BuildServiceProvider();
        var pipelineType = assembly.GetType("Sample.MyPipeline`1")!.MakeGenericType(typeof(string));
        var pipeline = provider.GetRequiredService(pipelineType);

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [1, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task AddPipelines_RegistersAndResolvesAStructPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public interface IPrefixProvider
            {
                string Prefix { get; }
            }

            public class PrefixProvider : IPrefixProvider
            {
                public string Prefix => "Value: ";
            }

            public readonly record struct FooSegment(IPrefixProvider prefixProvider) : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(prefixProvider.Prefix + value);
            }

            public partial record struct FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator(), new ServiceCollectionExtensionsGenerator());

        var services = new ServiceCollection();
        services.AddSingleton(assembly.GetType("Sample.IPrefixProvider")!, assembly.GetType("Sample.PrefixProvider")!);

        var extensionsType = assembly.GetType("Microsoft.Extensions.DependencyInjection.DovetailServiceCollectionExtensions")!;
        extensionsType.GetMethod("AddPipelines")!.Invoke(null, [services]);

        var provider = services.BuildServiceProvider();
        var pipelineType = assembly.GetType("Sample.FooPipeline")!;
        var pipeline = provider.GetRequiredService(pipelineType);

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("Value: 21", result);
    }
}
