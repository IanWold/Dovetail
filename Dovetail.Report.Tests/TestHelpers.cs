using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Dovetail.Report.Tests;

internal static class TestHelpers
{
    static TestHelpers()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    internal static CSharpCompilation CreateCompilation(string source) =>
        CSharpCompilation.Create(
            assemblyName: $"Dovetail.Report.Tests.Generated.{Guid.NewGuid():N}",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: DefaultReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

    private static IEnumerable<MetadataReference> DefaultReferences =>
        AppDomain.CurrentDomain.GetAssemblies()
        .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
        .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location));

    internal static void EmitFakeDovetailAssembly(string destinationPath, string version)
    {
        var source = """
            [assembly: System.Reflection.AssemblyVersion("__VERSION__")]
            namespace Dovetail;
            public class Marker { }
            """
            .Replace("__VERSION__", version);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Dovetail",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: DefaultReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var stream = new FileStream(destinationPath, FileMode.Create);

        var emitResult = compilation.Emit(stream);

        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
    }

    internal static string CreateTempProject(string classSource, [CallerMemberName] string testName = "")
    {
        var directory = Directory.CreateTempSubdirectory($"dovetail-report-tests-{testName}-").FullName;

        File.WriteAllText(Path.Combine(directory, "Temp.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        File.WriteAllText(Path.Combine(directory, "Class1.cs"), classSource);

        return Path.Combine(directory, "Temp.csproj");
    }

    internal static string FindRepoRoot([CallerFilePath] string here = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(here)!);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dovetail.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Dovetail.slnx not found in any ancestor directory).");
    }
}
