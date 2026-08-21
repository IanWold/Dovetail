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
    private const string ActivitySourceMetadataName = "System.Diagnostics.ActivitySource";
    internal const string SegmentParametersTrackingName = "SegmentParameters";
    private const string InputSeparator = "";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var segmentParameters = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SegmentAttributeFullName,
                predicate: static (node, _) => node is ParameterSyntax or MethodDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetNode is MethodDeclarationSyntax ? GetSegmentMethod(ctx) : GetSegmentParameter(ctx)
            )
            .Where(static parameter => parameter is not null)
            .Select(static (parameter, _) => parameter!.Value)
            .Collect()
            .WithTrackingName(SegmentParametersTrackingName);

        var hasActivitySource = context.CompilationProvider.Select(static (compilation, _) => compilation.GetTypeByMetadataName(ActivitySourceMetadataName) is not null);

        context.RegisterSourceOutput(segmentParameters.Combine(hasActivitySource), static (spc, data) =>
        {
            var (parameters, hasActivitySource) = data;
            var groups = parameters.GroupBy(static parameter => parameter.ContainingType).ToImmutableArray();
            var generatedAnyPipeline = false;

            foreach (var group in groups)
            {
                if (Execute(group.Key, group.ToImmutableArray(), spc, hasActivitySource))
                {
                    generatedAnyPipeline = true;
                }
            }

            if (hasActivitySource && generatedAnyPipeline)
            {
                spc.AddSource("DovetailActivitySource.g.cs", GenerateActivitySource());
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

        var parameterSyntax = (ParameterSyntax)context.TargetNode;
        var isPrimaryConstructorParameter = parameterSyntax.Parent?.Parent is TypeDeclarationSyntax;
        var parameterTypeName = parameterSymbol.Type.ToDisplayString(PipelineShapeResolver.DisplayNameFormat);

        string? valueAccessor;
        var backingMemberAmbiguous = false;

        if (isPrimaryConstructorParameter)
        {
            valueAccessor = parameterSymbol.Name;
        }
        else
        {
            var matchingMembers = containingType.GetMembers()
                .Where(static member => !member.IsStatic && !member.IsImplicitlyDeclared)
                .Where(member => GetMemberType(member) is { } memberType && SymbolEqualityComparer.Default.Equals(memberType, parameterSymbol.Type))
                .ToImmutableArray();

            valueAccessor = matchingMembers.Length == 1 ? matchingMembers[0].Name : null;
            backingMemberAmbiguous = matchingMembers.Length > 1;
        }

        var isPartial = containingType.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        var containingNamespace = containingType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : containingType.ContainingNamespace.ToDisplayString();

        string? pipelineInputsJoined = null;
        string? pipelineResultTypeName = null;

        if (PipelineShapeResolver.TryGetPipelineShape(containingType, out var pipelineInputTypeNames, out var resolvedPipelineResultTypeName))
        {
            pipelineInputsJoined = string.Join(InputSeparator, pipelineInputTypeNames);
            pipelineResultTypeName = resolvedPipelineResultTypeName;
        }

        string? segmentTypeName = null;
        string? segmentInputsJoined = null;
        string? segmentResultTypeName = null;

        if (parameterSymbol.Type is INamedTypeSymbol segmentType
            && PipelineShapeResolver.TryGetSegmentShape(segmentType, out var segmentInputTypeNames, out var resolvedResultTypeName)
        )
        {
            segmentTypeName = segmentType.ToDisplayString(PipelineShapeResolver.DisplayNameFormat);
            segmentInputsJoined = string.Join(InputSeparator, segmentInputTypeNames);
            segmentResultTypeName = resolvedResultTypeName;
        }

        return new SegmentParameterInfo(
            new TypeDeclarationModel(containingNamespace, containingType.Name, isPartial),
            pipelineInputsJoined,
            pipelineResultTypeName,
            parameterSymbol.Name,
            parameterTypeName,
            valueAccessor,
            backingMemberAmbiguous,
            segmentTypeName,
            segmentInputsJoined,
            segmentResultTypeName,
            parameterLocation,
            containingTypeLocation,
            IsStaticSegmentMethod: false,
            StaticSegmentMethodProblem: StaticSegmentMethodProblem.None,
            SegmentIsAsync: true,
            SegmentAcceptsCancellationToken: true
        );
    }

    private static ITypeSymbol? GetMemberType(ISymbol member) => member switch
    {
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        _ => null
    };

    private static SegmentParameterInfo? GetSegmentMethod(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IMethodSymbol { ContainingType: { } containingType } methodSymbol)
        {
            return null;
        }

        var methodLocation = context.TargetNode.GetLocation();
        var containingTypeLocation = containingType.Locations.FirstOrDefault();

        var isPartial = containingType.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        var containingNamespace = containingType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : containingType.ContainingNamespace.ToDisplayString();

        string? pipelineInputsJoined = null;
        string? pipelineResultTypeName = null;

        if (PipelineShapeResolver.TryGetPipelineShape(containingType, out var pipelineInputTypeNames, out var resolvedPipelineResultTypeName))
        {
            pipelineInputsJoined = string.Join(InputSeparator, pipelineInputTypeNames);
            pipelineResultTypeName = resolvedPipelineResultTypeName;
        }

        var containingTypeModel = new TypeDeclarationModel(containingNamespace, containingType.Name, isPartial);

        if (!methodSymbol.IsStatic)
        {
            return new SegmentParameterInfo(
                containingTypeModel,
                pipelineInputsJoined,
                pipelineResultTypeName,
                methodSymbol.Name,
                "",
                null,
                false,
                null,
                null,
                null,
                methodLocation,
                containingTypeLocation,
                IsStaticSegmentMethod: true,
                StaticSegmentMethodProblem: StaticSegmentMethodProblem.NotStatic,
                SegmentIsAsync: false,
                SegmentAcceptsCancellationToken: false
            );
        }

        var parameters = methodSymbol.Parameters;
        var acceptsCancellationToken = parameters.Length > 0
            && parameters[parameters.Length - 1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Threading.CancellationToken";

        var dataParameters = acceptsCancellationToken ? parameters.RemoveAt(parameters.Length - 1) : parameters;
        var segmentInputTypeNames = dataParameters.Select(static p => p.Type.ToDisplayString(PipelineShapeResolver.TypeNameFormat)).ToImmutableArray();

        var isAsyncTask = methodSymbol.ReturnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 1 } taskType
            && taskType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";

        var returnsNothing =
            methodSymbol.ReturnsVoid
            || (methodSymbol.ReturnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 0 } voidTask
                && voidTask.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
            );

        if (returnsNothing)
        {
            return new SegmentParameterInfo(
                containingTypeModel,
                pipelineInputsJoined,
                pipelineResultTypeName,
                methodSymbol.Name,
                "",
                null,
                false,
                null,
                null,
                null,
                methodLocation,
                containingTypeLocation,
                IsStaticSegmentMethod: true,
                StaticSegmentMethodProblem: StaticSegmentMethodProblem.NoReturnValue,
                SegmentIsAsync: false,
                SegmentAcceptsCancellationToken: false
            );
        }

        var segmentResultTypeName = isAsyncTask
            ? ((INamedTypeSymbol)methodSymbol.ReturnType).TypeArguments[0].ToDisplayString(PipelineShapeResolver.TypeNameFormat)
            : methodSymbol.ReturnType.ToDisplayString(PipelineShapeResolver.TypeNameFormat);

        return new SegmentParameterInfo(
            containingTypeModel,
            pipelineInputsJoined,
            pipelineResultTypeName,
            methodSymbol.Name,
            "",
            methodSymbol.Name,
            false,
            methodSymbol.Name,
            string.Join(InputSeparator, segmentInputTypeNames),
            segmentResultTypeName,
            methodLocation,
            containingTypeLocation,
            IsStaticSegmentMethod: true,
            StaticSegmentMethodProblem: StaticSegmentMethodProblem.None,
            SegmentIsAsync: isAsyncTask,
            SegmentAcceptsCancellationToken: acceptsCancellationToken
        );
    }

    private static bool Execute(TypeDeclarationModel containingType, ImmutableArray<SegmentParameterInfo> parameters, SourceProductionContext context, bool hasActivitySource)
    {
        var containingTypeLocation = parameters[0].ContainingTypeLocation ?? Location.None;

        if (!containingType.IsPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(ContainingTypeMustBePartial, containingTypeLocation, containingType.Name));
            return false;
        }

        var pipelineResultTypeName = parameters[0].PipelineResultTypeName;
        if (pipelineResultTypeName is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(ContainingTypeMustImplementPipeline, containingTypeLocation, containingType.Name));
            return false;
        }

        var pipelineInputTypeNames = string.IsNullOrEmpty(parameters[0].PipelineInputTypeNamesJoined)
            ? ImmutableArray<string>.Empty
            : parameters[0].PipelineInputTypeNamesJoined!.Split(new[] { InputSeparator }, StringSplitOptions.None).ToImmutableArray();
        var hasErrors = false;

        foreach (var duplicateInputType in pipelineInputTypeNames.GroupBy(static t => t).Where(static g => g.Count() > 1).Select(static g => g.Key))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicatePipelineInput, containingTypeLocation, containingType.Name, duplicateInputType));
            hasErrors = true;
        }

        if (hasErrors)
        {
            return false;
        }

        foreach (var parameter in parameters)
        {
            if (parameter.IsStaticSegmentMethod)
            {
                if (parameter.StaticSegmentMethodProblem == StaticSegmentMethodProblem.NotStatic)
                {
                    context.ReportDiagnostic(Diagnostic.Create(SegmentMethodMustBeStatic, parameter.ParameterLocation ?? Location.None, containingType.Name, parameter.ParameterName));
                    hasErrors = true;
                }
                else if (parameter.StaticSegmentMethodProblem == StaticSegmentMethodProblem.NoReturnValue)
                {
                    context.ReportDiagnostic(Diagnostic.Create(SegmentMethodMustReturnAValue, parameter.ParameterLocation ?? Location.None, containingType.Name, parameter.ParameterName));
                    hasErrors = true;
                }

                continue;
            }

            if (parameter.SegmentResultTypeName is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(SegmentTypeMustImplementPipelineSegment, parameter.ParameterLocation ?? Location.None, parameter.ParameterName));
                hasErrors = true;
            }

            if (parameter.ValueAccessor is null)
            {
                var descriptor = parameter.BackingMemberAmbiguous ? AmbiguousSegmentBackingMember : SegmentBackingMemberNotFound;
                context.ReportDiagnostic(Diagnostic.Create(descriptor, parameter.ParameterLocation ?? Location.None, containingType.Name, parameter.ParameterName, parameter.ParameterTypeName));
                hasErrors = true;
            }
        }

        if (hasErrors)
        {
            return false;
        }

        var segments = parameters
            .Select(static p => new SegmentModel(
                p.ParameterName,
                p.ValueAccessor!,
                p.SegmentTypeName!,
                string.IsNullOrEmpty(p.SegmentInputTypeNamesJoined)
                    ? ImmutableArray<string>.Empty
                    : p.SegmentInputTypeNamesJoined!.Split(new[] { InputSeparator }, StringSplitOptions.None).ToImmutableArray(),
                p.SegmentResultTypeName!,
                p.ParameterLocation,
                p.IsStaticSegmentMethod,
                p.SegmentIsAsync,
                p.SegmentAcceptsCancellationToken
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
            return false;
        }

        var terminal = byResultType[pipelineResultTypeName].SingleOrDefault();
        if (terminal.ParameterName is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingTerminalSegment, containingTypeLocation, containingType.Name, pipelineResultTypeName));
            return false;
        }

        var resultProviders = segments.ToDictionary(static s => s.ResultTypeName, static s => s.ParameterName);
        var dependencies = new Dictionary<string, ImmutableArray<DependencyBinding>>();

        foreach (var segment in segments)
        {
            var bindings = ImmutableArray.CreateBuilder<DependencyBinding>(segment.InputTypeNames.Length);
            foreach (var inputType in segment.InputTypeNames)
            {
                var pipelineInputIndex = pipelineInputTypeNames.IndexOf(inputType);
                var matchesInput = pipelineInputIndex >= 0;
                var matchesSegment = resultProviders.TryGetValue(inputType, out var providerName);

                if (matchesInput && matchesSegment)
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnresolvedDependency, segment.ParameterLocation ?? Location.None, segment.ParameterName, inputType, $"it matches both a pipeline input and segment '{providerName}'"));
                    hasErrors = true;
                }
                else if (matchesInput)
                {
                    bindings.Add(new DependencyBinding(SegmentParameterName: null, PipelineInputIndex: pipelineInputIndex));
                }
                else if (matchesSegment)
                {
                    bindings.Add(new DependencyBinding(SegmentParameterName: providerName, PipelineInputIndex: null));
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnresolvedDependency, segment.ParameterLocation ?? Location.None, segment.ParameterName, inputType, "no segment produces it and it does not match any of the pipeline's input types"));
                    hasErrors = true;
                }
            }

            dependencies[segment.ParameterName] = bindings.Count == segment.InputTypeNames.Length
                ? bindings.ToImmutable()
                : ImmutableArray<DependencyBinding>.Empty;
        }

        if (hasErrors)
        {
            return false;
        }

        if (TryFindCycle(segments, dependencies, out var cycleDescription))
        {
            context.ReportDiagnostic(Diagnostic.Create(DependencyCycle, containingTypeLocation, containingType.Name, cycleDescription));
            return false;
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
            return false;
        }

        var source = GenerateSource(containingType, pipelineInputTypeNames, pipelineResultTypeName, segments, dependencies, terminal.ParameterName, hasActivitySource);
        context.AddSource($"{containingType.Name}.g.cs", source);
        return true;
    }

    private static bool TryFindCycle(ImmutableArray<SegmentModel> segments, Dictionary<string, ImmutableArray<DependencyBinding>> dependencies, out string? cycleDescription)
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

            foreach (var binding in dependencies[parameterName])
            {
                if (binding.SegmentParameterName is string providerName && !Visit(providerName))
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

    private static HashSet<string> ComputeReachableFrom(string terminalParameterName, Dictionary<string, ImmutableArray<DependencyBinding>> dependencies)
    {
        var reachable = new HashSet<string> { terminalParameterName };
        var stack = new Stack<string>();
        stack.Push(terminalParameterName);

        while (stack.Count > 0)
        {
            foreach (var binding in dependencies[stack.Pop()])
            {
                if (binding.SegmentParameterName is string providerName && reachable.Add(providerName))
                {
                    stack.Push(providerName);
                }
            }
        }

        return reachable;
    }

    private static ImmutableArray<SegmentModel> SortByDependency(ImmutableArray<SegmentModel> segments, Dictionary<string, ImmutableArray<DependencyBinding>> dependencies)
    {
        var byName = new Dictionary<string, SegmentModel>();
        foreach (var segment in segments)
        {
            byName[segment.ParameterName] = segment;
        }

        var visited = new HashSet<string>();
        var sorted = ImmutableArray.CreateBuilder<SegmentModel>(segments.Length);

        void Visit(string parameterName)
        {
            if (!visited.Add(parameterName))
            {
                return;
            }

            foreach (var binding in dependencies[parameterName])
            {
                if (binding.SegmentParameterName is string providerName)
                {
                    Visit(providerName);
                }
            }

            sorted.Add(byName[parameterName]);
        }

        foreach (var segment in segments)
        {
            Visit(segment.ParameterName);
        }

        return sorted.ToImmutable();
    }

    private static string GenerateSource(TypeDeclarationModel containingType, ImmutableArray<string> pipelineInputTypeNames, string pipelineResultTypeName, ImmutableArray<SegmentModel> segments, Dictionary<string, ImmutableArray<DependencyBinding>> dependencies, string terminalParameterName, bool hasActivitySource)
    {
        segments = SortByDependency(segments, dependencies);

        var builder = new StringBuilder()
            .AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .AppendLine();

        if (containingType.Namespace.Length > 0)
        {
            builder.AppendLine($"namespace {containingType.Namespace};").AppendLine();
        }

        var inputParameterList = string.Join(", ", pipelineInputTypeNames.Select(
            (typeName, index) => $"{typeName} {GetPipelineInputParameterName(index, pipelineInputTypeNames.Length)}"));
        var inputParameter = inputParameterList.Length == 0 ? "" : $"{inputParameterList}, ";
        var fullyQualifiedPipelineName = containingType.Namespace.Length > 0
            ? $"{containingType.Namespace}.{containingType.Name}"
            : containingType.Name;

        builder
            .AppendLine($"partial class {containingType.Name}")
            .AppendLine("{")
            .AppendLine($"    public async global::System.Threading.Tasks.Task<{pipelineResultTypeName}> ExecuteAsync({inputParameter}global::System.Threading.CancellationToken token)")
            .AppendLine("    {");

        if (hasActivitySource)
        {
            builder
                .AppendLine($"        using var activity = global::Dovetail.DovetailActivitySource.Instance.StartActivity(\"{containingType.Name}.ExecuteAsync\");")
                .AppendLine($"        activity?.SetTag(\"dovetail.pipeline\", \"{fullyQualifiedPipelineName}\");")
                .AppendLine();
        }

        builder.AppendLine("        using var cts = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token);")
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
            .AppendLine(hasActivitySource ? "        catch (global::System.Exception ex)" : "        catch")
            .AppendLine("        {");

        if (hasActivitySource)
        {
            builder.AppendLine("            activity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, ex.Message);");
        }

        builder.AppendLine("            cts.Cancel();")
            .AppendLine();

        var siblingTasks = string.Join(", ", segments
            .Where(s => s.ParameterName != terminalParameterName)
            .Select(s => $"{s.ParameterName}Task"));

        if (siblingTasks.Length > 0)
        {
            builder
                .AppendLine($"            try {{ await global::System.Threading.Tasks.Task.WhenAll({siblingTasks}).ConfigureAwait(false); }}")
                .AppendLine("            catch { }")
                .AppendLine();
        }

        builder
            .AppendLine("            throw;")
            .AppendLine("        }")
            .AppendLine();

        foreach (var segment in segments)
        {
            var bindings = dependencies[segment.ParameterName];
            var args = new List<string>(bindings.Length + 1);
            foreach (var binding in bindings)
            {
                args.Add(binding.PipelineInputIndex is int pipelineInputIndex
                    ? GetPipelineInputParameterName(pipelineInputIndex, pipelineInputTypeNames.Length)
                    : $"await {binding.SegmentParameterName}Task.ConfigureAwait(false)");
            }

            if (segment.AcceptsCancellationToken)
            {
                args.Add("linkedToken");
            }

            var argList = string.Join(", ", args);
            var invocation = segment.IsStaticMethod ? $"{segment.ValueAccessor}({argList})" : $"{segment.ValueAccessor}.ExecuteAsync({argList})";
            var callExpression = segment.IsAsync ? $"await {invocation}.ConfigureAwait(false)" : invocation;

            if (hasActivitySource)
            {
                builder
                    .AppendLine($"        async global::System.Threading.Tasks.Task<{segment.ResultTypeName}> {ToPascalCase(segment.ParameterName)}Async()")
                    .AppendLine("        {")
                    .AppendLine($"            using var segmentActivity = global::Dovetail.DovetailActivitySource.Instance.StartActivity(\"{containingType.Name}.{segment.ParameterName}\");")
                    .AppendLine($"            segmentActivity?.SetTag(\"dovetail.pipeline\", \"{fullyQualifiedPipelineName}\");")
                    .AppendLine($"            segmentActivity?.SetTag(\"dovetail.segment\", \"{segment.ParameterName}\");")
                    .AppendLine($"            segmentActivity?.SetTag(\"dovetail.segment.type\", \"{segment.SegmentTypeName}\");")
                    .AppendLine("            try")
                    .AppendLine("            {")
                    .AppendLine($"                return {callExpression};")
                    .AppendLine("            }")
                    .AppendLine("            catch (global::System.Exception ex)")
                    .AppendLine("            {")
                    .AppendLine("                segmentActivity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, ex.Message);")
                    .AppendLine("                throw;")
                    .AppendLine("            }")
                    .AppendLine("        }")
                    .AppendLine();
            }
            else
            {
                builder
                    .AppendLine($"        async global::System.Threading.Tasks.Task<{segment.ResultTypeName}> {ToPascalCase(segment.ParameterName)}Async() =>")
                    .AppendLine($"            {callExpression};")
                    .AppendLine();
            }
        }

        builder
            .AppendLine("    }")
            .AppendLine("}");

        return builder.ToString();
    }

    private static string GenerateActivitySource() =>
        new StringBuilder()
            .AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .AppendLine()
            .AppendLine("namespace Dovetail;")
            .AppendLine()
            .AppendLine("internal static class DovetailActivitySource")
            .AppendLine("{")
            .AppendLine("    internal static readonly global::System.Diagnostics.ActivitySource Instance = new global::System.Diagnostics.ActivitySource(\"Dovetail\");")
            .AppendLine("}")
            .ToString();

    private static string ToPascalCase(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

    private static string GetPipelineInputParameterName(int index, int totalCount) =>
        totalCount == 1 ? "input" : $"input{index + 1}";
}
