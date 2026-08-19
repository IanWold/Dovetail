using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Dovetail.Diagnostics;

namespace Dovetail;

[Generator(LanguageNames.CSharp)]
internal sealed class PipelineSourceGenerator : IIncrementalGenerator
{
    private const string SegmentAttributeFullName = "Dovetail.SegmentAttribute";
    private const string InputSeparator = "";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var segmentParameters = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SegmentAttributeFullName,
                predicate: static (node, _) => node is ParameterSyntax,
                transform: static (ctx, _) => GetSegmentParameter(ctx)
            )
            .Where(static parameter => parameter is not null)
            .Select(static (parameter, _) => parameter!.Value)
            .Collect();

        context.RegisterSourceOutput(segmentParameters, static (spc, parameters) =>
        {
            foreach (var group in parameters.GroupBy(static parameter => parameter.ContainingType))
            {
                Execute(group.Key, group.ToImmutableArray(), spc);
            }
        });
    }

    private static SegmentParameterInfo? GetSegmentParameter(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { } containingType }} parameterSymbol)
        {
            return null;
        }

        var parameterLocation = context.TargetNode.GetLocation();
        var containingTypeLocation = containingType.Locations.FirstOrDefault();

        var isPartial = containingType.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        var containingNamespace = containingType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : containingType.ContainingNamespace.ToDisplayString();

        PipelineShapeResolver.TryGetPipelineShape(containingType, out var pipelineInputTypeName, out var pipelineResultTypeName);

        string? segmentInputsJoined = null;
        string? segmentResultTypeName = null;

        if (parameterSymbol.Type is INamedTypeSymbol segmentType
            && PipelineShapeResolver.TryGetSegmentShape(segmentType, out var segmentInputTypeNames, out var resolvedResultTypeName)
        )
        {
            segmentInputsJoined = string.Join(InputSeparator, segmentInputTypeNames);
            segmentResultTypeName = resolvedResultTypeName;
        }

        return new SegmentParameterInfo(
            new TypeDeclarationModel(containingNamespace, containingType.Name, isPartial),
            pipelineInputTypeName,
            pipelineResultTypeName,
            parameterSymbol.Name,
            segmentInputsJoined,
            segmentResultTypeName,
            parameterLocation,
            containingTypeLocation
        );
    }

    private static void Execute(TypeDeclarationModel containingType, ImmutableArray<SegmentParameterInfo> parameters, SourceProductionContext context)
    {
        var containingTypeLocation = parameters[0].ContainingTypeLocation ?? Location.None;

        if (!containingType.IsPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(ContainingTypeMustBePartial, containingTypeLocation, containingType.Name));
            return;
        }

        var pipelineResultTypeName = parameters[0].PipelineResultTypeName;
        if (pipelineResultTypeName is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(ContainingTypeMustImplementPipeline, containingTypeLocation, containingType.Name));
            return;
        }

        var pipelineInputTypeName = parameters[0].PipelineInputTypeName;
        var hasErrors = false;

        foreach (var parameter in parameters)
        {
            if (parameter.SegmentResultTypeName is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(SegmentTypeMustImplementPipelineSegment, parameter.ParameterLocation ?? Location.None, parameter.ParameterName));
                hasErrors = true;
            }
        }

        if (hasErrors)
        {
            return;
        }

        var segments = parameters
            .Select(static p => new SegmentModel(
                p.ParameterName,
                string.IsNullOrEmpty(p.SegmentInputTypeNamesJoined)
                    ? ImmutableArray<string>.Empty
                    : p.SegmentInputTypeNamesJoined!.Split(new[] { InputSeparator }, StringSplitOptions.None).ToImmutableArray(),
                p.SegmentResultTypeName!,
                p.ParameterLocation
            ))
            .ToImmutableArray();

        var byResultType = segments.ToLookup(static s => s.ResultTypeName);
        foreach (var duplicates in byResultType.Where(static g => g.Count() > 1))
        {
            var names = string.Join(", ", duplicates.Select(static s => $"'{s.ParameterName}'"));
            var firstLocation = duplicates.First().ParameterLocation ?? Location.None;

            context.ReportDiagnostic(Diagnostic.Create(DuplicateSegmentResult, firstLocation, names, duplicates.Key));
            hasErrors = true;
        }

        if (hasErrors)
        {
            return;
        }

        var terminal = byResultType[pipelineResultTypeName].SingleOrDefault();
        if (terminal.ParameterName is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingTerminalSegment, containingTypeLocation, containingType.Name, pipelineResultTypeName));
            return;
        }

        var resultProviders = segments.ToDictionary(static s => s.ResultTypeName, static s => s.ParameterName);
        var dependencies = new Dictionary<string, ImmutableArray<string>>();

        foreach (var segment in segments)
        {
            var providers = ImmutableArray.CreateBuilder<string>(segment.InputTypeNames.Length);
            foreach (var inputType in segment.InputTypeNames)
            {
                var matchesInput = pipelineInputTypeName is not null && inputType == pipelineInputTypeName;
                var matchesSegment = resultProviders.TryGetValue(inputType, out var providerName);

                if (matchesInput && matchesSegment)
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnresolvedDependency, segment.ParameterLocation ?? Location.None, segment.ParameterName, inputType, $"it matches both the pipeline input and segment '{providerName}'"));
                    hasErrors = true;
                }
                else if (matchesInput)
                {
                    providers.Add(string.Empty);
                }
                else if (matchesSegment)
                {
                    providers.Add(providerName!);
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnresolvedDependency, segment.ParameterLocation ?? Location.None, segment.ParameterName, inputType, "no segment produces it and it does not match the pipeline input"));
                    hasErrors = true;
                }
            }

            dependencies[segment.ParameterName] = providers.Count == segment.InputTypeNames.Length
                ? providers.ToImmutable()
                : ImmutableArray<string>.Empty;
        }

        if (hasErrors)
        {
            return;
        }

        if (TryFindCycle(segments, dependencies, out var cycleDescription))
        {
            context.ReportDiagnostic(Diagnostic.Create(DependencyCycle, containingTypeLocation, containingType.Name, cycleDescription));
            return;
        }

        var reachable = ComputeReachableFrom(terminal.ParameterName, dependencies);
        foreach (var segment in segments)
        {
            if (!reachable.Contains(segment.ParameterName))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnreachableSegment, segment.ParameterLocation ?? Location.None, segment.ParameterName, pipelineResultTypeName));
                hasErrors = true;
            }
        }

        if (hasErrors)
        {
            return;
        }

        var source = GenerateSource(containingType, pipelineInputTypeName, pipelineResultTypeName, segments, dependencies, terminal.ParameterName);
        context.AddSource($"{containingType.Name}.g.cs", source);
    }

    private static bool TryFindCycle(ImmutableArray<SegmentModel> segments, Dictionary<string, ImmutableArray<string>> dependencies, out string? cycleDescription)
    {
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();
        var path = new List<string>();
        string? found = null;

        bool Visit(string parameterName)
        {
            if (visited.Contains(parameterName))
            {
                return true;
            }

            if (!visiting.Add(parameterName))
            {
                var cycleStart = path.IndexOf(parameterName);
                found = string.Join(" -> ", path.Skip(cycleStart)) + " -> " + parameterName;
                return false;
            }

            path.Add(parameterName);

            foreach (var provider in dependencies[parameterName])
            {
                if (provider.Length > 0 && !Visit(provider))
                {
                    return false;
                }
            }

            path.RemoveAt(path.Count - 1);
            visiting.Remove(parameterName);
            visited.Add(parameterName);
            return true;
        }

        foreach (var segment in segments)
        {
            if (!Visit(segment.ParameterName))
            {
                cycleDescription = found;
                return true;
            }
        }

        cycleDescription = null;
        return false;
    }

    private static HashSet<string> ComputeReachableFrom(string terminalParameterName, Dictionary<string, ImmutableArray<string>> dependencies)
    {
        var reachable = new HashSet<string> { terminalParameterName };
        var stack = new Stack<string>();
        stack.Push(terminalParameterName);

        while (stack.Count > 0)
        {
            foreach (var provider in dependencies[stack.Pop()])
            {
                if (provider.Length > 0 && reachable.Add(provider))
                {
                    stack.Push(provider);
                }
            }
        }

        return reachable;
    }

    private static string GenerateSource(TypeDeclarationModel containingType, string? pipelineInputTypeName, string pipelineResultTypeName, ImmutableArray<SegmentModel> segments, Dictionary<string, ImmutableArray<string>> dependencies, string terminalParameterName)
    {
        var builder = new StringBuilder()
            .AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .AppendLine();

        if (containingType.Namespace.Length > 0)
        {
            builder.AppendLine($"namespace {containingType.Namespace};").AppendLine();
        }

        var inputParameter = pipelineInputTypeName is null ? "" : $"{pipelineInputTypeName} input, ";

        builder.AppendLine($"partial class {containingType.Name}")
            .AppendLine("{")
            .AppendLine($"    public async global::System.Threading.Tasks.Task<{pipelineResultTypeName}> ExecuteAsync({inputParameter}global::System.Threading.CancellationToken token)")
            .AppendLine("    {")
            .AppendLine("        using var cts = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token);")
            .AppendLine("        var linkedToken = cts.Token;")
            .AppendLine();

        foreach (var segment in segments)
        {
            builder.AppendLine($"        var {segment.ParameterName}Task = {ToPascalCase(segment.ParameterName)}Async();");
        }

        builder.AppendLine()
            .AppendLine("        try")
            .AppendLine("        {")
            .AppendLine($"            return await {terminalParameterName}Task.ConfigureAwait(false);")
            .AppendLine("        }")
            .AppendLine("        catch")
            .AppendLine("        {")
            .AppendLine("            cts.Cancel();")
            .AppendLine();

        var siblingTasks = string.Join(", ", segments
            .Where(s => s.ParameterName != terminalParameterName)
            .Select(s => $"{s.ParameterName}Task"));

        if (siblingTasks.Length > 0)
        {
            builder.AppendLine($"            try {{ await global::System.Threading.Tasks.Task.WhenAll({siblingTasks}).ConfigureAwait(false); }}")
                .AppendLine("            catch { }")
                .AppendLine();
        }

        builder.AppendLine("            throw;")
            .AppendLine("        }")
            .AppendLine();

        foreach (var segment in segments)
        {
            var providers = dependencies[segment.ParameterName];
            var args = new List<string>(providers.Length + 1);
            foreach (var provider in providers)
            {
                args.Add(provider.Length == 0 ? "input" : $"await {provider}Task.ConfigureAwait(false)");
            }

            args.Add("linkedToken");

            builder.AppendLine($"        async global::System.Threading.Tasks.Task<{segment.ResultTypeName}> {ToPascalCase(segment.ParameterName)}Async() =>")
                .AppendLine($"            await {segment.ParameterName}.ExecuteAsync({string.Join(", ", args)}).ConfigureAwait(false);")
                .AppendLine();
        }

        builder.AppendLine("    }")
            .AppendLine("}");

        return builder.ToString();
    }

    private static string ToPascalCase(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
}
