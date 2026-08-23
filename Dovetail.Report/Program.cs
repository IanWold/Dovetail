using Dovetail;
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

    WarnIfDovetailVersionMismatch(compilation);
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

Console.WriteLine($"Found {graphs.Count} pipeline(s):");

foreach (var graph in graphs.OrderBy(static g => g.ContainingType.Namespace).ThenBy(static g => g.ContainingType.Name))
{
    var fullName = graph.ContainingType.Namespace.Length > 0
        ? $"{graph.ContainingType.Namespace}.{graph.ContainingType.Name}"
        : graph.ContainingType.Name;

    Console.WriteLine($"- {fullName}");
    Console.WriteLine($"    inputs: {(graph.PipelineInputTypeNames.IsEmpty ? "(none)" : string.Join(", ", graph.PipelineInputTypeNames))}");
    Console.WriteLine($"    result: {graph.PipelineResultTypeName}");
    Console.WriteLine($"    segments ({graph.Segments.Length}): {string.Join(", ", graph.Segments.Select(static s => s.ParameterName))}");

    if (graph.MaxConcurrency is int maxConcurrency)
    {
        Console.WriteLine($"    maxConcurrency: {maxConcurrency}");
    }
}

Console.WriteLine($"(output path resolved to: {outputPath} — Phase 4 writes the report there)");

return 0;

static void WarnIfDovetailVersionMismatch(Compilation compilation)
{
    var referencedDovetail = compilation.References
        .Select(compilation.GetAssemblyOrModuleSymbol)
        .OfType<IAssemblySymbol>()
        .FirstOrDefault(static assembly => assembly.Name == "Dovetail");

    if (referencedDovetail is null)
    {
        return;
    }

    var referencedVersion = referencedDovetail.Identity.Version;
    var toolDovetailVersion = typeof(SegmentAttribute).Assembly.GetName().Version;

    if (!referencedVersion.Equals(toolDovetailVersion))
    {
        Console.Error.WriteLine($"warning: '{compilation.AssemblyName}' references Dovetail {referencedVersion}, but dovetail-report was built against Dovetail {toolDovetailVersion}. The report may not reflect that version's behavior.");
    }
}
