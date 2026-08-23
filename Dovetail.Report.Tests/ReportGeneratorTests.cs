using static Dovetail.Report.Tests.TestHelpers;

namespace Dovetail.Report.Tests;

public class ReportGeneratorTests
{
    [Fact]
    public async Task RunAsync_WhenNoPipelinesAreFound_SucceedsAndWritesAnEmptyReport()
    {
        var projectPath = CreateTempProject("namespace Sample; public class NotAPipeline { }");
        var outputPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "report-out");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = await ReportGenerator.RunAsync(["--project", projectPath, "--output", outputPath], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outputPath, "index.html")));
        Assert.Contains("Wrote report for 0 pipeline(s)", stdout.ToString());
    }

    [Fact]
    public async Task RunAsync_WhenTheProjectHasCompileErrors_FailsWithoutWritingAnyOutput()
    {
        var projectPath = CreateTempProject("namespace Sample; public class Broken { public int Value = \"not an int\"; }");
        var outputPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "report-out");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = await ReportGenerator.RunAsync(["--project", projectPath, "--output", outputPath], stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.False(Directory.Exists(outputPath));
        Assert.Contains("compile error", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WhenReferencedDovetailVersionDiffersFromTheOneToolBuiltAgainst_WarnsButStillSucceeds()
    {
        var projectPath = CreateTempProject("namespace Sample; public class NotAPipeline { }");
        var directory = Path.GetDirectoryName(projectPath)!;
        var outputPath = Path.Combine(directory, "report-out");
        var fakeDovetailPath = Path.Combine(directory, "Dovetail.dll");

        EmitFakeDovetailAssembly(fakeDovetailPath, "9.9.9.9");

        File.WriteAllText(projectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Dovetail">
                  <HintPath>{fakeDovetailPath}</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = await ReportGenerator.RunAsync(["--project", projectPath, "--output", outputPath], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("references Dovetail 9.9.9.9", stderr.ToString());
        Assert.Contains("dovetail-report was built against Dovetail", stderr.ToString());
        Assert.True(File.Exists(Path.Combine(outputPath, "index.html")));
    }

    [Fact]
    public async Task RunAsync_WhenProjectFileDoesNotExist_FailsCleanlyWithoutThrowing()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = await ReportGenerator.RunAsync(["--project", "does-not-exist.csproj"], stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.Contains("not found", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WhenBothProjectAndSolutionAreGiven_FailsWithoutLoadingEither()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = await ReportGenerator.RunAsync(["--project", "a.csproj", "--solution", "b.sln"], stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.Contains("only one of", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
