using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Dovetail.Diagnostics;

namespace Dovetail;

[Generator(LanguageNames.CSharp)]
internal class PipelineSourceGenerator : IIncrementalGenerator
{
    private const string SegmentAttributeFullName = "Dovetail.SegmentAttribute";
    private const string ActivitySourceMetadataName = "System.Diagnostics.ActivitySource";
    internal const string SegmentParametersTrackingName = "SegmentParameters";
    private const string InputSeparator = "";

    private static readonly Regex _qualifiedTypeNamePattern = new(@"global::(?:\w+\.)*(\w+)", RegexOptions.Compiled);

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

    private static SegmentParameterInfo? GetSegmentParameter(GeneratorAttributeSyntaxContext context) =>
        context.TargetSymbol is IParameterSymbol parameterSymbol
        ? BuildSegmentParameterInfo(parameterSymbol, (ParameterSyntax)context.TargetNode)
        : null;

    private static SegmentParameterInfo? BuildSegmentParameterInfo(IParameterSymbol parameterSymbol, ParameterSyntax parameterSyntax)
    {
        if (parameterSymbol.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { } containingType })
        {
            return null;
        }

        var parameterLocation = parameterSyntax.GetLocation();
        var containingTypeLocation = containingType.Locations.FirstOrDefault();

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

        var (isPartial, ownKeyword) = GetPartialityAndKeyword(containingType);

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
            new TypeDeclarationModel(containingNamespace, containingType.Name, isPartial, GetContainingTypes(containingType), GetTypeParameterList(containingType), ownKeyword),
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
            SegmentAcceptsCancellationToken: true,
            GetMaxConcurrency(containingType)
        );
    }

    private static ITypeSymbol? GetMemberType(ISymbol member) => member switch
    {
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        _ => null
    };

    private static ImmutableArray<ContainingTypeModel> GetContainingTypes(INamedTypeSymbol type)
    {
        var chain = new List<ContainingTypeModel>();
        var current = type.ContainingType;

        while (current is not null)
        {
            var syntax = current.DeclaringSyntaxReferences
                .Select(static reference => reference.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault();

            var isPartial = syntax?.Modifiers.Any(SyntaxKind.PartialKeyword) ?? false;
            var keyword = syntax is null ? "class" : GetTypeKindKeyword(syntax);

            chain.Add(new ContainingTypeModel(current.Name, keyword, isPartial, current.Arity > 0));
            current = current.ContainingType;
        }

        chain.Reverse();
        return chain.ToImmutableArray();
    }

    private static string GetTypeKindKeyword(TypeDeclarationSyntax syntax) => syntax switch
    {
        RecordDeclarationSyntax record => record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        _ => "class"
    };

    private static string GetTypeParameterList(INamedTypeSymbol type) =>
        type.Arity != 0
        ? $"<{string.Join(", ", type.TypeParameters.Select(static t => t.Name))}>"
        : "";

    private static (bool IsPartial, string Keyword) GetPartialityAndKeyword(INamedTypeSymbol type)
    {
        var declarations = type.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .ToImmutableArray();

        var isPartial = declarations.Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
        var keyword = declarations.Length > 0 ? GetTypeKindKeyword(declarations[0]) : "class";

        return (isPartial, keyword);
    }

    private static int? GetMaxConcurrency(INamedTypeSymbol containingType) =>
        containingType.GetAttributes()
        .FirstOrDefault(a =>
            a.AttributeClass is { Name: nameof(MaxConcurrencyAttribute) } attributeClass
            && attributeClass.ContainingNamespace.ToDisplayString() == "Dovetail"
            && a.ConstructorArguments.Length == 1
        )
        ?.ConstructorArguments[0].Value as int?;

    private static SegmentParameterInfo? GetSegmentMethod(GeneratorAttributeSyntaxContext context) =>
        context.TargetSymbol is IMethodSymbol methodSymbol ? BuildSegmentMethodInfo(methodSymbol) : null;

    private static SegmentParameterInfo? BuildSegmentMethodInfo(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ContainingType is not { } containingType)
        {
            return null;
        }

        var methodLocation = methodSymbol.Locations.FirstOrDefault();
        var containingTypeLocation = containingType.Locations.FirstOrDefault();

        var (isPartial, ownKeyword) = GetPartialityAndKeyword(containingType);

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

        var containingTypeModel = new TypeDeclarationModel(containingNamespace, containingType.Name, isPartial, GetContainingTypes(containingType), GetTypeParameterList(containingType), ownKeyword);
        var maxConcurrency = GetMaxConcurrency(containingType);

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
                SegmentAcceptsCancellationToken: false,
                maxConcurrency
            );
        }

        if (methodSymbol.IsGenericMethod)
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
                StaticSegmentMethodProblem: StaticSegmentMethodProblem.HasOwnTypeParameters,
                SegmentIsAsync: false,
                SegmentAcceptsCancellationToken: false,
                maxConcurrency
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
                SegmentAcceptsCancellationToken: false,
                maxConcurrency
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
            SegmentAcceptsCancellationToken: acceptsCancellationToken,
            maxConcurrency
        );
    }

    internal static ImmutableArray<SegmentParameterInfo> FindSegmentMembers(INamedTypeSymbol candidateType)
    {
        var builder = ImmutableArray.CreateBuilder<SegmentParameterInfo>();

        foreach (var constructor in candidateType.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (!HasSegmentAttribute(parameter.GetAttributes())
                    || parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not ParameterSyntax parameterSyntax
                )
                {
                    continue;
                }

                if (BuildSegmentParameterInfo(parameter, parameterSyntax) is { } parameterInfo)
                {
                    builder.Add(parameterInfo);
                }
            }
        }

        foreach (var member in candidateType.GetMembers())
        {
            if (member is not IMethodSymbol method || !HasSegmentAttribute(method.GetAttributes()))
            {
                continue;
            }

            if (BuildSegmentMethodInfo(method) is { } methodInfo)
            {
                builder.Add(methodInfo);
            }
        }

        return builder.ToImmutable();
    }

    private static bool HasSegmentAttribute(ImmutableArray<AttributeData> attributes) =>
        attributes.Any(a => a.AttributeClass is { Name: nameof(SegmentAttribute) } attributeClass && attributeClass.ContainingNamespace.ToDisplayString() == "Dovetail");

    private static bool Execute(TypeDeclarationModel containingType, ImmutableArray<SegmentParameterInfo> parameters, SourceProductionContext context, bool hasActivitySource)
    {
        if (!TryBuildGraph(containingType, parameters, context.ReportDiagnostic, out var graph))
        {
            return false;
        }

        var model = graph!.Value;
        var source = GenerateSource(model.ContainingType, model.PipelineInputTypeNames, model.PipelineResultTypeName, model.Segments, model.Dependencies, model.TerminalParameterName, hasActivitySource, model.MaxConcurrency);
        
        context.AddSource($"{model.ContainingType.Name}.g.cs", source);
        
        return true;
    }

    internal static bool TryBuildGraph(TypeDeclarationModel containingType, ImmutableArray<SegmentParameterInfo> parameters, Action<Diagnostic> reportDiagnostic, out PipelineGraphModel? graph)
    {
        graph = null;
        var containingTypeLocation = parameters[0].ContainingTypeLocation ?? Location.None; 

        if (!containingType.IsPartial)
        {
            reportDiagnostic(Diagnostic.Create(ContainingTypeMustBePartial, containingTypeLocation, containingType.Name));
            return false;
        }

        foreach (var ancestor in containingType.ContainingTypes)
        {
            if (ancestor.IsGeneric)
            {
                reportDiagnostic(Diagnostic.Create(NestedInGenericType, containingTypeLocation, containingType.Name, ancestor.Name));
                return false;
            }

            if (!ancestor.IsPartial)
            {
                reportDiagnostic(Diagnostic.Create(ContainingAncestorMustBePartial, containingTypeLocation, containingType.Name, ancestor.Name));
                return false;
            }
        }

        var pipelineResultTypeName = parameters[0].PipelineResultTypeName;
        if (pipelineResultTypeName is null)
        {
            reportDiagnostic(Diagnostic.Create(ContainingTypeMustImplementPipeline, containingTypeLocation, containingType.Name));
            return false;
        }

        var pipelineInputTypeNames = string.IsNullOrEmpty(parameters[0].PipelineInputTypeNamesJoined)
            ? ImmutableArray<string>.Empty
            : parameters[0].PipelineInputTypeNamesJoined!.Split(new[] { InputSeparator }, StringSplitOptions.None).ToImmutableArray();
        var hasErrors = false;

        foreach (var duplicateInputType in pipelineInputTypeNames.GroupBy(static t => t).Where(static g => g.Count() > 1).Select(static g => g.Key))
        {
            reportDiagnostic(Diagnostic.Create(DuplicatePipelineInput, containingTypeLocation, containingType.Name, duplicateInputType));
            hasErrors = true;
        }

        if (hasErrors)
        {
            return false;
        }

        var maxConcurrency = parameters[0].MaxConcurrency;
        if (maxConcurrency is <= 0)
        {
            reportDiagnostic(Diagnostic.Create(InvalidMaxConcurrency, containingTypeLocation, containingType.Name, maxConcurrency));
            return false;
        }

        foreach (var parameter in parameters)
        {
            if (parameter.IsStaticSegmentMethod)
            {
                if (parameter.StaticSegmentMethodProblem == StaticSegmentMethodProblem.NotStatic)
                {
                    reportDiagnostic(Diagnostic.Create(SegmentMethodMustBeStatic, parameter.ParameterLocation ?? Location.None, containingType.Name, parameter.ParameterName));
                    hasErrors = true;
                }
                else if (parameter.StaticSegmentMethodProblem == StaticSegmentMethodProblem.NoReturnValue)
                {
                    reportDiagnostic(Diagnostic.Create(SegmentMethodMustReturnAValue, parameter.ParameterLocation ?? Location.None, containingType.Name, parameter.ParameterName));
                    hasErrors = true;
                }
                else if (parameter.StaticSegmentMethodProblem == StaticSegmentMethodProblem.HasOwnTypeParameters)
                {
                    reportDiagnostic(Diagnostic.Create(SegmentMethodCannotHaveOwnTypeParameters, parameter.ParameterLocation ?? Location.None, containingType.Name, parameter.ParameterName));
                    hasErrors = true;
                }

                continue;
            }

            if (parameter.SegmentResultTypeName is null)
            {
                reportDiagnostic(Diagnostic.Create(SegmentTypeMustImplementPipelineSegment, parameter.ParameterLocation ?? Location.None, parameter.ParameterName));
                hasErrors = true;
            }

            if (parameter.ValueAccessor is null)
            {
                var descriptor = parameter.BackingMemberAmbiguous ? AmbiguousSegmentBackingMember : SegmentBackingMemberNotFound;
                reportDiagnostic(Diagnostic.Create(descriptor, parameter.ParameterLocation ?? Location.None, containingType.Name, parameter.ParameterName, parameter.ParameterTypeName));
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
        var resultProviders = new Dictionary<string, ChainCandidates>();

        foreach (var group in byResultType)
        {
            var candidates = group.ToImmutableArray();
            if (candidates.Length == 1)
            {
                resultProviders[group.Key] = new ChainCandidates(candidates[0], Origin: null);
                continue;
            }

            var endomorphisms = candidates.Where(c => c.InputTypeNames.Contains(group.Key)).ToImmutableArray();
            if (candidates.Length == 2 && endomorphisms.Length == 1)
            {
                var sink = endomorphisms[0];
                var origin = candidates[0].ParameterName == sink.ParameterName ? candidates[1] : candidates[0];

                resultProviders[group.Key] = new ChainCandidates(sink, origin);

                continue;
            }

            var names = string.Join(", ", candidates.Select(static s => $"'{s.ParameterName}'"));
            var firstLocation = candidates[0].ParameterLocation ?? Location.None;

            reportDiagnostic(candidates.Length == 2 && endomorphisms.Length == 0
                ? Diagnostic.Create(DuplicateSegmentResult, firstLocation, names, group.Key)
                : Diagnostic.Create(AmbiguousResultChain, firstLocation, names, group.Key));

            hasErrors = true;
        }

        if (hasErrors)
        {
            return false;
        }

        if (!resultProviders.TryGetValue(pipelineResultTypeName, out var terminalResolution))
        {
            reportDiagnostic(Diagnostic.Create(MissingTerminalSegment, containingTypeLocation, containingType.Name, pipelineResultTypeName));
            return false;
        }

        var terminalParameterName = terminalResolution.Sink.ParameterName;
        var dependencies = new Dictionary<string, ImmutableArray<DependencyBinding>>();
        var pendingCollisions = new List<PendingCollision>();

        foreach (var segment in segments)
        {
            var bindings = ImmutableArray.CreateBuilder<DependencyBinding>(segment.InputTypeNames.Length);
            foreach (var inputType in segment.InputTypeNames)
            {
                var pipelineInputIndex = pipelineInputTypeNames.IndexOf(inputType);
                var matchesInput = pipelineInputIndex >= 0;
                var matchesSegment = TryResolveSegmentProvider(resultProviders, inputType, segment.ParameterName, out var providerName, out var providerIsEndomorphism);

                if (matchesSegment && providerIsEndomorphism)
                {
                    bindings.Add(new DependencyBinding(SegmentParameterName: providerName, PipelineInputIndex: null));
                }
                else if (matchesInput && matchesSegment)
                {
                    pendingCollisions.Add(new PendingCollision(segment.ParameterName, bindings.Count, inputType, pipelineInputIndex, providerName!, segment.ParameterLocation));
                    bindings.Add(default);
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
                    reportDiagnostic(Diagnostic.Create(UnresolvedDependency, segment.ParameterLocation ?? Location.None, segment.ParameterName, inputType));
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

        if (pendingCollisions.Count > 0 && !TryResolvePendingCollisions(segments, pendingCollisions, dependencies, reportDiagnostic))
        {
            return false;
        }

        if (TryFindCycle(segments, dependencies, out var cycleDescription))
        {
            reportDiagnostic(Diagnostic.Create(DependencyCycle, containingTypeLocation, containingType.Name, cycleDescription));
            return false;
        }

        var reachable = ComputeReachableFrom(terminalParameterName, dependencies);
        foreach (var segment in segments)
        {
            if (!reachable.Contains(segment.ParameterName))
            {
                reportDiagnostic(Diagnostic.Create(UnreachableSegment, segment.ParameterLocation ?? Location.None, segment.ParameterName, pipelineResultTypeName));
                hasErrors = true;
            }
        }

        if (hasErrors)
        {
            return false;
        }

        segments = SortByDependency(segments, dependencies);
        graph = new PipelineGraphModel(containingType, pipelineInputTypeNames, pipelineResultTypeName, segments, dependencies, terminalParameterName, maxConcurrency);

        return true;
    }

    private static bool TryResolvePendingCollisions(ImmutableArray<SegmentModel> segments, List<PendingCollision> pendingCollisions, Dictionary<string, ImmutableArray<DependencyBinding>> dependencies, Action<Diagnostic> reportDiagnostic)
    {
        var hasErrors = false;
        var pendingConsumerNames = new HashSet<string>(pendingCollisions.Select(static p => p.ConsumerParameterName));

        foreach (var pending in pendingCollisions)
        {
            if (pendingConsumerNames.Contains(pending.ProviderParameterName))
            {
                var providerLocation = segments.First(s => s.ParameterName == pending.ProviderParameterName).ParameterLocation;
                var additionalLocations = providerLocation is { } location ? new[] { location } : null;
                
                reportDiagnostic(Diagnostic.Create(InterdependentAmbiguousDependency, pending.ConsumerLocation ?? Location.None, additionalLocations, pending.ConsumerParameterName, pending.InputType, pending.ProviderParameterName));
                
                hasErrors = true;
                
                continue;
            }

            if (ComputeReachableFrom(pending.ProviderParameterName, dependencies).Contains(pending.ConsumerParameterName))
            {
                dependencies[pending.ConsumerParameterName] =
                    dependencies[pending.ConsumerParameterName]
                    .SetItem(pending.BindingIndex, new DependencyBinding(SegmentParameterName: null, PipelineInputIndex: pending.PipelineInputIndex));
            }
            else
            {
                reportDiagnostic(Diagnostic.Create(AmbiguousDependency, pending.ConsumerLocation ?? Location.None, pending.ConsumerParameterName, pending.InputType, pending.ProviderParameterName));
                hasErrors = true;
            }
        }

        return !hasErrors;
    }

    private static bool TryResolveSegmentProvider(Dictionary<string, ChainCandidates> resultProviders, string inputType, string consumerParameterName, out string? providerName, out bool providerIsEndomorphism)
    {
        providerName = null;
        providerIsEndomorphism = false;

        if (!resultProviders.TryGetValue(inputType, out var candidates))
        {
            return false;
        }

        var resolved = candidates.Origin is { } origin && consumerParameterName == candidates.Sink.ParameterName
            ? origin
            : candidates.Sink;

        if (resolved.ParameterName == consumerParameterName)
        {
            return false;
        }

        providerName = resolved.ParameterName;
        providerIsEndomorphism = resolved.InputTypeNames.Contains(inputType);
        
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

            if (dependencies[parameterName].Any(d => d.SegmentParameterName is string providerName && !Visit(providerName)))
            {
                return false;
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

            foreach (var binding in dependencies[parameterName].Where(d => d.SegmentParameterName is not null))
            {
                Visit(binding.SegmentParameterName!);
            }

            sorted.Add(byName[parameterName]);
        }

        foreach (var segment in segments)
        {
            Visit(segment.ParameterName);
        }

        return sorted.ToImmutable();
    }

    private static string GenerateSource(TypeDeclarationModel containingType, ImmutableArray<string> pipelineInputTypeNames, string pipelineResultTypeName, ImmutableArray<SegmentModel> segments, Dictionary<string, ImmutableArray<DependencyBinding>> dependencies, string terminalParameterName, bool hasActivitySource, int? maxConcurrency)
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

        foreach (var ancestor in containingType.ContainingTypes)
        {
            builder
                .AppendLine($"partial {ancestor.Keyword} {ancestor.Name}")
                .AppendLine("{");
        }

        var inputParameterList = string.Join(", ", pipelineInputTypeNames.Select(
            (typeName, index) => $"{typeName} {GetPipelineInputParameterName(index, pipelineInputTypeNames.Length)}"));
        var inputParameter = inputParameterList.Length == 0 ? "" : $"{inputParameterList}, ";
        var fullyQualifiedPipelineName = (containingType.Namespace.Length > 0
            ? $"{containingType.Namespace}.{containingType.Name}"
            : containingType.Name) + containingType.TypeParameterList;

        var mermaidDiagram = GenerateMermaidDiagram(pipelineInputTypeNames, segments, dependencies, terminalParameterName);

        builder
            .AppendLine($"partial {containingType.Keyword} {containingType.Name}{containingType.TypeParameterList}")
            .AppendLine( "{")
            .AppendLine( "    /// <summary>")
            .AppendLine( "    /// Fan-out/fan-in pipeline generated by Dovetail.")
            .AppendLine( "    /// </summary>")
            .AppendLine( "    /// <remarks>")
            .AppendLine( "    /// <para>Segment dependency graph, in Mermaid flowchart syntax:</para>")
            .AppendLine( "    /// <code lang=\"mermaid\">")
            .AppendLine( "    /// <![CDATA[");

        foreach (var line in mermaidDiagram.Split('\n'))
        {
            builder.AppendLine($"    /// {line}");
        }

        builder
            .AppendLine("    /// ]]>")
            .AppendLine("    /// </code>");

        if (maxConcurrency is int concurrencyNote)
        {
            builder.AppendLine($"    /// <para>No more than {concurrencyNote} of this pipeline's segments run at once (<c>[MaxConcurrency({concurrencyNote})]</c>).</para>");
        }

        builder
            .AppendLine( "    /// </remarks>")
            .AppendLine($"    public async global::System.Threading.Tasks.Task<{pipelineResultTypeName}> ExecuteAsync({inputParameter}global::System.Threading.CancellationToken token)")
            .AppendLine( "    {");

        if (hasActivitySource)
        {
            builder
                .AppendLine($"        using var activity = global::Dovetail.DovetailActivitySource.Instance.StartActivity(\"{containingType.Name}.ExecuteAsync\");")
                .AppendLine($"        activity?.SetTag(\"dovetail.pipeline\", \"{fullyQualifiedPipelineName}\");")
                .AppendLine();
        }

        builder
            .AppendLine("        using var cts = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token);")
            .AppendLine("        var linkedToken = cts.Token;")
            .AppendLine();

        if (maxConcurrency is int concurrencyLimit)
        {
            builder.AppendLine($"        using var concurrencyGate = new global::System.Threading.SemaphoreSlim({concurrencyLimit});")
                .AppendLine();
        }

        var isValueTypePipeline = containingType.Keyword is "struct" or "record struct";

        if (isValueTypePipeline)
        {
            foreach (var segment in segments)
            {
                if (!segment.IsStaticMethod)
                {
                    builder.AppendLine($"        var {segment.ParameterName}_ = {segment.ValueAccessor};");
                }
            }

            builder.AppendLine();
        }

        foreach (var segment in segments)
        {
            builder.AppendLine($"        var {segment.ParameterName}Task = {ToPascalCase(segment.ParameterName)}Async();");
        }

        var siblingTasks = string.Join(", ", segments
            .Where(s => s.ParameterName != terminalParameterName)
            .Select(s => $"{s.ParameterName}Task"));

        builder.AppendLine()
            .AppendLine( "        try")
            .AppendLine( "        {")
            .AppendLine($"            return await {terminalParameterName}Task.ConfigureAwait(false);")
            .AppendLine( "        }");

        if (hasActivitySource)
        {
            builder
                .AppendLine("        catch (global::System.OperationCanceledException) when (token.IsCancellationRequested)")
                .AppendLine("        {")
                .AppendLine("            activity?.SetTag(\"dovetail.canceled\", true);")
                .AppendLine("            cts.Cancel();")
                .AppendLine();

            AppendSiblingDrain(builder, siblingTasks, "            ");

            builder
                .AppendLine("            throw;")
                .AppendLine("        }")
                .AppendLine("        catch (global::System.Exception ex)")
                .AppendLine("        {")
                .AppendLine("            activity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, ex.Message);");

            AppendExceptionEvent(builder, "            ", "activity");

            builder
                .AppendLine("            cts.Cancel();")
                .AppendLine();

            AppendSiblingDrain(builder, siblingTasks, "            ");

            builder
                .AppendLine("            throw;")
                .AppendLine("        }")
                .AppendLine();
        }
        else
        {
            builder
                .AppendLine("        catch")
                .AppendLine("        {")
                .AppendLine("            cts.Cancel();")
                .AppendLine();

            AppendSiblingDrain(builder, siblingTasks, "            ");

            builder
                .AppendLine("            throw;")
                .AppendLine("        }")
                .AppendLine();
        }

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
            var invocationTarget = isValueTypePipeline && !segment.IsStaticMethod ? $"{segment.ParameterName}_" : segment.ValueAccessor;
            var invocation = segment.IsStaticMethod ? $"{invocationTarget}({argList})" : $"{invocationTarget}.ExecuteAsync({argList})";
            var callExpression = segment.IsAsync ? $"await {invocation}.ConfigureAwait(false)" : invocation;

            if (maxConcurrency is not null && hasActivitySource)
            {
                builder
                    .AppendLine($"        async global::System.Threading.Tasks.Task<{segment.ResultTypeName}> {ToPascalCase(segment.ParameterName)}Async()")
                    .AppendLine( "        {")
                    .AppendLine( "            await concurrencyGate.WaitAsync(linkedToken).ConfigureAwait(false);")
                    .AppendLine( "            try")
                    .AppendLine( "            {")
                    .AppendLine($"                using var segmentActivity = global::Dovetail.DovetailActivitySource.Instance.StartActivity(\"{containingType.Name}.{segment.ParameterName}\");")
                    .AppendLine($"                segmentActivity?.SetTag(\"dovetail.pipeline\", \"{fullyQualifiedPipelineName}\");")
                    .AppendLine($"                segmentActivity?.SetTag(\"dovetail.segment\", \"{segment.ParameterName}\");")
                    .AppendLine($"                segmentActivity?.SetTag(\"dovetail.segment.type\", \"{segment.SegmentTypeName}\");")
                    .AppendLine( "                try")
                    .AppendLine( "                {")
                    .AppendLine($"                    return {callExpression};")
                    .AppendLine( "                }")
                    .AppendLine( "                catch (global::System.OperationCanceledException) when (linkedToken.IsCancellationRequested)")
                    .AppendLine( "                {")
                    .AppendLine( "                    segmentActivity?.SetTag(\"dovetail.segment.canceled\", true);")
                    .AppendLine( "                    throw;")
                    .AppendLine( "                }")
                    .AppendLine( "                catch (global::System.Exception ex)")
                    .AppendLine( "                {")
                    .AppendLine( "                    segmentActivity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, ex.Message);");

                AppendExceptionEvent(builder, "                    ", "segmentActivity");

                builder
                    .AppendLine( "                    throw;")
                    .AppendLine( "                }")
                    .AppendLine( "            }")
                    .AppendLine( "            finally")
                    .AppendLine( "            {")
                    .AppendLine( "                concurrencyGate.Release();")
                    .AppendLine( "            }")
                    .AppendLine( "        }")
                    .AppendLine();
            }
            else if (maxConcurrency is not null)
            {
                builder
                    .AppendLine($"        async global::System.Threading.Tasks.Task<{segment.ResultTypeName}> {ToPascalCase(segment.ParameterName)}Async()")
                    .AppendLine( "        {")
                    .AppendLine( "            await concurrencyGate.WaitAsync(linkedToken).ConfigureAwait(false);")
                    .AppendLine( "            try")
                    .AppendLine( "            {")
                    .AppendLine($"                return {callExpression};")
                    .AppendLine( "            }")
                    .AppendLine( "            finally")
                    .AppendLine( "            {")
                    .AppendLine( "                concurrencyGate.Release();")
                    .AppendLine( "            }")
                    .AppendLine( "        }")
                    .AppendLine();
            }
            else if (hasActivitySource)
            {
                builder
                    .AppendLine($"        async global::System.Threading.Tasks.Task<{segment.ResultTypeName}> {ToPascalCase(segment.ParameterName)}Async()")
                    .AppendLine( "        {")
                    .AppendLine($"            using var segmentActivity = global::Dovetail.DovetailActivitySource.Instance.StartActivity(\"{containingType.Name}.{segment.ParameterName}\");")
                    .AppendLine($"            segmentActivity?.SetTag(\"dovetail.pipeline\", \"{fullyQualifiedPipelineName}\");")
                    .AppendLine($"            segmentActivity?.SetTag(\"dovetail.segment\", \"{segment.ParameterName}\");")
                    .AppendLine($"            segmentActivity?.SetTag(\"dovetail.segment.type\", \"{segment.SegmentTypeName}\");")
                    .AppendLine( "            try")
                    .AppendLine( "            {")
                    .AppendLine($"                return {callExpression};")
                    .AppendLine( "            }")
                    .AppendLine( "            catch (global::System.OperationCanceledException) when (linkedToken.IsCancellationRequested)")
                    .AppendLine( "            {")
                    .AppendLine( "                segmentActivity?.SetTag(\"dovetail.segment.canceled\", true);")
                    .AppendLine( "                throw;")
                    .AppendLine( "            }")
                    .AppendLine( "            catch (global::System.Exception ex)")
                    .AppendLine( "            {")
                    .AppendLine( "                segmentActivity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, ex.Message);");

                AppendExceptionEvent(builder, "                ", "segmentActivity");

                builder
                    .AppendLine( "                throw;")
                    .AppendLine( "            }")
                    .AppendLine( "        }")
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

        for (var i = 0; i < containingType.ContainingTypes.Length; i++)
        {
            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    internal static string GenerateMermaidDiagram(ImmutableArray<string> pipelineInputTypeNames, ImmutableArray<SegmentModel> segments, Dictionary<string, ImmutableArray<DependencyBinding>> dependencies, string terminalParameterName)
    {
        var lines = new List<string> { "flowchart TD" };

        for (var i = 0; i < pipelineInputTypeNames.Length; i++)
        {
            var inputName = GetPipelineInputParameterName(i, pipelineInputTypeNames.Length);
            var label = EscapeMermaidLabel($"{inputName}: {SimplifyTypeNameForDiagram(pipelineInputTypeNames[i])}");
            lines.Add($"    in_{i}([\"{label}\"])");
        }

        foreach (var segment in segments)
        {
            var label = EscapeMermaidLabel($"{segment.ParameterName}: {SimplifyTypeNameForDiagram(segment.ResultTypeName)}");
            lines.Add(segment.ParameterName == terminalParameterName
                ? $"    seg_{segment.ParameterName}(\"{label}\")"
                : $"    seg_{segment.ParameterName}[\"{label}\"]");
        }

        foreach (var segment in segments)
        {
            foreach (var binding in dependencies[segment.ParameterName])
            {
                var from = binding.PipelineInputIndex is int inputIndex
                    ? $"in_{inputIndex}"
                    : $"seg_{binding.SegmentParameterName}";
                lines.Add($"    {from} --> seg_{segment.ParameterName}");
            }
        }

        return string.Join("\n", lines);
    }

    internal static string EscapeMermaidLabel(string text) =>
        text.Replace("<", "#lt;").Replace(">", "#gt;");

    internal static string SimplifyTypeNameForDiagram(string typeName) =>
        _qualifiedTypeNamePattern.Replace(typeName, static match => match.Groups[1].Value);

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

    private static void AppendExceptionEvent(StringBuilder builder, string indent, string activityVariableName)
    {
        builder
            .AppendLine($"{indent}{activityVariableName}?.AddEvent(new global::System.Diagnostics.ActivityEvent(")
            .AppendLine($"{indent}    \"exception\",")
            .AppendLine($"{indent}    tags: new global::System.Diagnostics.ActivityTagsCollection")
            .AppendLine($"{indent}    {{")
            .AppendLine($"{indent}        [\"exception.type\"] = ex.GetType().FullName,")
            .AppendLine($"{indent}        [\"exception.message\"] = ex.Message,")
            .AppendLine($"{indent}        [\"exception.stacktrace\"] = ex.ToString(),")
            .AppendLine($"{indent}    }}));");
    }

    private static void AppendSiblingDrain(StringBuilder builder, string siblingTasks, string indent)
    {
        if (siblingTasks.Length == 0)
        {
            return;
        }

        builder
            .AppendLine($"{indent}try {{ await global::System.Threading.Tasks.Task.WhenAll({siblingTasks}).ConfigureAwait(false); }}")
            .AppendLine($"{indent}catch {{ }}")
            .AppendLine();
    }

    private static string ToPascalCase(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

    private static string GetPipelineInputParameterName(int index, int totalCount) =>
        totalCount == 1 ? "input" : $"input{index + 1}";
}
