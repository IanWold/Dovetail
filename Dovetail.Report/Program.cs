using Dovetail;
using Dovetail.Report;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

MSBuildLocator.RegisterDefaults();

string? projectPath = null;
string? solutionPath = null;
string? outputPath = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--project" when i + 1 < args.Length:
            projectPath = args[++i];
            break;

        case "--solution" when i + 1 < args.Length:
            solutionPath = args[++i];
            break;
            
        case "--output" when i + 1 < args.Length:
            outputPath = args[++i];
            break;

        default:
            Console.Error.WriteLine($"Unrecognized argument: {args[i]}");
            return 1;
    }
}

if (projectPath is not null && solutionPath is not null)
{
    Console.Error.WriteLine("Specify only one of --project or --solution.");
    return 1;
}

if (projectPath is null && solutionPath is null)
{
    var cwd = Directory.GetCurrentDirectory();
    var projectFiles = Directory.GetFiles(cwd, "*.csproj");

    if (projectFiles.Length == 1)
    {
        projectPath = projectFiles[0];
    }
    else if (projectFiles.Length > 1)
    {
        Console.Error.WriteLine($"Multiple project files found in {cwd}; specify --project.");
        return 1;
    }
    else
    {
        var solutionFiles = Directory.GetFiles(cwd, "*.sln").Concat(Directory.GetFiles(cwd, "*.slnx")).ToArray();

        if (solutionFiles.Length == 1)
        {
            solutionPath = solutionFiles[0];
        }
        else if (solutionFiles.Length > 1)
        {
            Console.Error.WriteLine($"Multiple solution files found in {cwd}; specify --solution.");
            return 1;
        }
        else
        {
            Console.Error.WriteLine($"No project or solution file found in {cwd}; specify --project or --solution.");
            return 1;
        }
    }
}

if (solutionPath is not null && !File.Exists(solutionPath))
{
    Console.Error.WriteLine($"Solution file not found: {solutionPath}");
    return 1;
}

if (projectPath is not null && !File.Exists(projectPath))
{
    Console.Error.WriteLine($"Project file not found: {projectPath}");
    return 1;
}

outputPath ??= Path.Combine(Directory.GetCurrentDirectory(), "dovetail-report");

using var workspace = MSBuildWorkspace.Create();
workspace.RegisterWorkspaceFailedHandler(e => Console.Error.WriteLine($"warning: {e.Diagnostic.Message}"));

var compilations = new List<Compilation>();

try
{
    if (solutionPath is not null)
    {
        var solution = await workspace.OpenSolutionAsync(solutionPath);
        foreach (var project in solution.Projects)
        {
            if (await project.GetCompilationAsync() is { } projectCompilation)
            {
                compilations.Add(projectCompilation);
            }
        }
    }
    else
    {
        var project = await workspace.OpenProjectAsync(projectPath!);
        if (await project.GetCompilationAsync() is not { } projectCompilation)
        {
            Console.Error.WriteLine($"Could not compile project: {projectPath}");
            return 1;
        }

        compilations.Add(projectCompilation);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load {solutionPath ?? projectPath}: {ex.Message}");
    return 1;
}

var dovetailVersion = typeof(SegmentAttribute).Assembly.GetName().Version!;
var reportVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;

foreach (var compilation in compilations)
{
    var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length > 0)
    {
        Console.Error.WriteLine($"'{compilation.AssemblyName}' has {errors.Length} compile error(s); fix these before generating a report:");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"  {error}");
        }

        return 1;
    }

    if (GetReferencedDovetailVersion(compilation) is { } referencedVersion && !referencedVersion.Equals(dovetailVersion))
    {
        Console.Error.WriteLine(
            $"warning: '{compilation.AssemblyName}' references Dovetail {referencedVersion}, but dovetail-report " +
            $"was built against Dovetail {dovetailVersion}. The report may not reflect that version's behavior."
        );
    }
}

var graphs = new List<PipelineGraphModel>();

foreach (var compilation in compilations)
{
    var candidateTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    foreach (var syntaxTree in compilation.SyntaxTrees)
    {
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        foreach (var typeDeclaration in syntaxTree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol { IsAbstract: false } candidateType
                && candidateType.TypeKind is TypeKind.Class or TypeKind.Struct
            )
            {
                candidateTypes.Add(candidateType);
            }
        }
    }

    foreach (var candidateType in candidateTypes)
    {
        var members = PipelineSourceGenerator.FindSegmentMembers(candidateType);
        if (members.IsEmpty)
        {
            continue;
        }

        if (PipelineSourceGenerator.TryBuildGraph(members[0].ContainingType, members, static _ => { }, out var graph))
        {
            graphs.Add(graph!.Value);
        }
    }
}

if (Directory.Exists(outputPath))
{
    Directory.Delete(outputPath, recursive: true);
}

Directory.CreateDirectory(outputPath);

var vendorPath = Path.Combine(outputPath, "vendor");

Directory.CreateDirectory(vendorPath);

WriteEmbeddedAsset("Dovetail.Report.Assets.mermaid.min.js", Path.Combine(vendorPath, "mermaid.min.js"));
WriteEmbeddedAsset("Dovetail.Report.Assets.pico.indigo.min.css", Path.Combine(vendorPath, "pico.indigo.min.css"));

var sortedGraphs = graphs
    .OrderBy(static g => Render.GetFullyQualifiedName(g.ContainingType), StringComparer.Ordinal)
    .ToArray();

var pipelineLinks = sortedGraphs
    .Select(static g => (Name: Render.GetFullyQualifiedName(g.ContainingType), FileName: Render.GetPageFileName(g.ContainingType)))
    .ToArray();

var sourcePath = solutionPath ?? projectPath!;
var projectName = Path.GetFileNameWithoutExtension(sourcePath);
var sourceLabel = solutionPath is not null ? "Source solution" : "Source project";

File.WriteAllText(
    Path.Combine(outputPath, "index.html"),
    Render.RenderIndexPage(projectName, sourceLabel, Path.GetFileName(sourcePath), sortedGraphs, dovetailVersion, reportVersion, DateTimeOffset.UtcNow)
);

foreach (var graph in sortedGraphs)
{
    var pageFileName = Render.GetPageFileName(graph.ContainingType);
    File.WriteAllText(Path.Combine(outputPath, pageFileName), Render.RenderPipelinePage(projectName, graph, pipelineLinks));
}

Console.WriteLine($"Wrote report for {sortedGraphs.Length} pipeline(s) to {outputPath}");

return 0;

static Version? GetReferencedDovetailVersion(Compilation compilation) =>
    compilation.References
        .Select(compilation.GetAssemblyOrModuleSymbol)
        .OfType<IAssemblySymbol>()
        .FirstOrDefault(static assembly => assembly.Name == "Dovetail")
        ?.Identity.Version;

static void WriteEmbeddedAsset(string logicalName, string destinationPath)
{
    using var resourceStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName) ?? throw new InvalidOperationException($"Missing embedded resource: {logicalName}");
    using var fileStream = File.Create(destinationPath);

    resourceStream.CopyTo(fileStream);
}
