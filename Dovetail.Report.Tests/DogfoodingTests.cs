using static Dovetail.Report.Tests.TestHelpers;

namespace Dovetail.Report.Tests;

/// <summary>
/// CSharpCompilation tests can't cover an actual example
/// </summary>
public class DogfoodingTests
{
    [Fact]
    public async Task RunAsync_AgainstTheRealExampleProject_ProducesAReportCoveringAllFourPipelines()
    {
        var repoRoot = FindRepoRoot();
        var projectPath = Path.Combine(repoRoot, "Dovetail.Example", "Dovetail.Example.csproj");
        
        Assert.True(File.Exists(projectPath), $"Expected to find Dovetail.Example.csproj at {projectPath}");

        var outputPath = Directory.CreateTempSubdirectory("dovetail-report-dogfood-").FullName;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = await ReportGenerator.RunAsync(["--project", projectPath, "--output", outputPath], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("Wrote report for 4 pipeline(s)", stdout.ToString());
        Assert.True(File.Exists(Path.Combine(outputPath, "index.html")));
        Assert.True(File.Exists(Path.Combine(outputPath, "vendor", "mermaid.min.js")));
        Assert.True(File.Exists(Path.Combine(outputPath, "vendor", "pico.indigo.min.css")));

        string[] expectedPipelines =
        [
            "Dovetail.Example.Business.CartSummaryPipeline",
            "Dovetail.Example.Business.CustomerProfilePipeline",
            "Dovetail.Example.Business.OrderConfirmationPipeline",
            "Dovetail.Example.Business.ProductDetailPipeline",
        ];

        foreach (var pipelineName in expectedPipelines)
        {
            var pagePath = Path.Combine(outputPath, $"{pipelineName}.html");

            Assert.True(File.Exists(pagePath), $"Expected a page for {pipelineName} at {pagePath}");

            var pageContent = await File.ReadAllTextAsync(pagePath, TestContext.Current.CancellationToken);

            Assert.Contains("flowchart TD", pageContent, StringComparison.Ordinal);
        }

        var productDetailPage = await File.ReadAllTextAsync(Path.Combine(outputPath, "Dovetail.Example.Business.ProductDetailPipeline.html"), TestContext.Current.CancellationToken);
        
        Assert.Contains("MaxConcurrency 2", productDetailPage, StringComparison.Ordinal);
    }
}
