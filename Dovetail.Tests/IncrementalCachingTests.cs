using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Dovetail.Tests;

public class IncrementalCachingTests
{
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
}
