using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Dovetail.Tests;

internal static class TestHelpers
{
    internal static void AssertSingleDiagnostic(string source, string expectedId)
    {
        var result = RunGenerator(source);

        Assert.Empty(result.GeneratedTrees);

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(expectedId, diagnostic.Id);
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.True(diagnostic.Location.IsInSource);
    }

    internal static GeneratorDriverRunResult RunGenerator(string source, bool includeActivitySource = true) =>
        CSharpGeneratorDriver.Create(new PipelineSourceGenerator())
        .RunGenerators(CreateCompilation(source, includeActivitySource: includeActivitySource)).GetRunResult();

    internal static Assembly CompileAndLoad(string source, params IIncrementalGenerator[] generators)
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

    internal static CSharpCompilation CreateCompilation(string source, bool includeServiceCollection = true, bool includeActivitySource = true) =>
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
