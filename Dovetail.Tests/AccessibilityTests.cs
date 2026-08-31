using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class AccessibilityTests
{
    [Fact]
    public async Task InternalTopLevelPipeline_GeneratesAndExecutesCorrectly()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            internal class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            internal partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
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
    public async Task PrivateNestedPipeline_GeneratesAndExecutesCorrectly()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public partial class Outer
            {
                private class FooSegment : IPipelineSegment<int, string>
                {
                    public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
                }

                private partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.Outer+FooPipeline")!;
        var foo = Activator.CreateInstance(assembly.GetType("Sample.Outer+FooSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;
        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public async Task InternalTopLevelPipelineAndSegment_RegisterAndResolveViaDI()
    {
        _ = new ServiceCollection();

        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            internal class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            internal partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator(), new ServiceCollectionExtensionsGenerator());
        var services = new ServiceCollection();
        var extensionsType = assembly.GetType("Microsoft.Extensions.DependencyInjection.DovetailServiceCollectionExtensions")!;
        
        extensionsType.GetMethod("AddPipelines")!.Invoke(null, [services]);

        var provider = services.BuildServiceProvider();
        var pipelineType = assembly.GetType("Sample.FooPipeline")!;
        var pipeline = provider.GetRequiredService(pipelineType);
        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public async Task InternalNestedPipelineAndSegment_RegisterAndResolveViaDI()
    {
        _ = new ServiceCollection();

        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public partial class Outer
            {
                internal class FooSegment : IPipelineSegment<int, string>
                {
                    public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
                }

                internal partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator(), new ServiceCollectionExtensionsGenerator());
        var services = new ServiceCollection();
        var extensionsType = assembly.GetType("Microsoft.Extensions.DependencyInjection.DovetailServiceCollectionExtensions")!;
        
        extensionsType.GetMethod("AddPipelines")!.Invoke(null, [services]);

        var provider = services.BuildServiceProvider();
        var pipelineType = assembly.GetType("Sample.Outer+FooPipeline")!;
        var pipeline = provider.GetRequiredService(pipelineType);
        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public void PrivateNestedSegment_ReportsDove022_ForTheSegmentOnly()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public partial class Outer
            {
                private class FooSegment : IPipelineSegment<int, string>
                {
                    public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
                }

                public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            }
            """;

        var result = CSharpGeneratorDriver.Create(new ServiceCollectionExtensionsGenerator())
            .RunGenerators(CreateCompilation(source), TestContext.Current.CancellationToken).GetRunResult();

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal("DOVE022", diagnostic.Id);
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Contains("Outer.FooSegment", diagnostic.GetMessage());
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void PrivateNestedPipeline_ReportsDove022_ForThePipelineOnly()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public partial class Outer
            {
                public class FooSegment : IPipelineSegment<int, string>
                {
                    public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
                }

                private partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            }
            """;

        var result = CSharpGeneratorDriver.Create(new ServiceCollectionExtensionsGenerator())
            .RunGenerators(CreateCompilation(source), TestContext.Current.CancellationToken).GetRunResult();

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal("DOVE022", diagnostic.Id);
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Contains("Outer.FooPipeline", diagnostic.GetMessage());
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public async Task SegmentNestedInsideItsOwnPipeline_WrappingAPlainService_RegistersAndResolvesViaDI()
    {
        _ = new ServiceCollection();

        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class MyService
            {
                public Task<string> DoThingAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class MyPipeline(
                [Segment] MyPipeline.MyServiceWrapper myService
            ) : IPipeline<int, string>
            {
                public class MyServiceWrapper(MyService service) : IPipelineSegment<int, string>
                {
                    public Task<string> ExecuteAsync(int value, CancellationToken ct) => service.DoThingAsync(value, ct);
                }
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator(), new ServiceCollectionExtensionsGenerator());
        var services = new ServiceCollection();

        services.AddTransient(assembly.GetType("Sample.MyService")!);

        var extensionsType = assembly.GetType("Microsoft.Extensions.DependencyInjection.DovetailServiceCollectionExtensions")!;

        extensionsType.GetMethod("AddPipelines")!.Invoke(null, [services]);

        var provider = services.BuildServiceProvider();
        var pipelineType = assembly.GetType("Sample.MyPipeline")!;
        var pipeline = provider.GetRequiredService(pipelineType);
        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("21", result);
    }

    [Fact]
    public void PrivateSegmentNestedInsideItsOwnPipeline_ReportsDove022()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class MyService
            {
                public Task<string> DoThingAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class MyPipeline(
                [Segment] MyPipeline.MyServiceWrapper myService
            ) : IPipeline<int, string>
            {
                private class MyServiceWrapper(MyService service) : IPipelineSegment<int, string>
                {
                    public Task<string> ExecuteAsync(int value, CancellationToken ct) => service.DoThingAsync(value, ct);
                }
            }
            """;

        var result = CSharpGeneratorDriver.Create(new ServiceCollectionExtensionsGenerator())
            .RunGenerators(CreateCompilation(source), TestContext.Current.CancellationToken).GetRunResult();

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal("DOVE022", diagnostic.Id);
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Contains("MyPipeline.MyServiceWrapper", diagnostic.GetMessage());
        Assert.Empty(result.GeneratedTrees);
    }
}
