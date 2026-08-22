using System.Collections.Immutable;
using System.Text;
using Dovetail.DependencyInjection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Dovetail.Diagnostics;

namespace Dovetail;

[Generator(LanguageNames.CSharp)]
internal sealed class ServiceCollectionExtensionsGenerator : IIncrementalGenerator
{
    private const string ServiceCollectionMetadataName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string InterfaceSeparator = "\x1f";

    internal const string RegisteredTypesTrackingName = "RegisteredTypes";

    private static readonly SymbolDisplayFormat BaseNameFormat = PipelineShapeResolver.TypeNameFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { BaseList.Types.Count: > 0 },
                transform: static (ctx, _) => GetCandidate(ctx)
            )
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!.Value)
            .Collect()
            .WithTrackingName(RegisteredTypesTrackingName);

        var hasServiceCollection = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName(ServiceCollectionMetadataName) is not null);

        context.RegisterSourceOutput(candidates.Combine(hasServiceCollection), static (spc, data) =>
        {
            var (types, hasServiceCollection) = data;
            if (!hasServiceCollection)
            {
                return;
            }

            var distinctTypes = types.Distinct().ToImmutableArray();
            if (distinctTypes.IsEmpty)
            {
                return;
            }

            var segmentInterfacePairs = distinctTypes
                .Where(static t => !t.IsPipeline && t.SegmentInterfaceTypeNamesJoined is not null)
                .SelectMany(static t => t.SegmentInterfaceTypeNamesJoined!
                    .Split(new[] { InterfaceSeparator }, StringSplitOptions.None)
                    .Select(interfaceTypeName => (Segment: t, InterfaceTypeName: interfaceTypeName))
                )
                .ToImmutableArray();

            var hasErrors = false;
            foreach (var duplicates in segmentInterfacePairs.ToLookup(static p => p.InterfaceTypeName).Where(static g => g.Count() > 1))
            {
                var names = string.Join(", ", duplicates.Select(static p => $"'{p.Segment.FullyQualifiedName}'"));
                var location = duplicates.Select(static p => p.Segment.Location).FirstOrDefault(static l => l is not null) ?? Location.None;

                spc.ReportDiagnostic(Diagnostic.Create(DuplicateSegmentInterfaceImplementation, location, names, duplicates.Key));
                hasErrors = true;
            }

            if (hasErrors)
            {
                return;
            }

            spc.AddSource("DovetailServiceCollectionExtensions.g.cs", GenerateSource(distinctTypes));
        });
    }

    private static RegisteredTypeInfo? GetCandidate(GeneratorSyntaxContext context)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol { IsAbstract: false, TypeKind: TypeKind.Class or TypeKind.Struct } symbol)
        {
            return null;
        }

        var fullyQualifiedName = symbol.ToDisplayString(BaseNameFormat);
        var lifetime = GetLifetime(symbol);
        var location = symbol.Locations.FirstOrDefault();

        var segmentInterfaces = PipelineShapeResolver.GetSegmentInterfaces(symbol);
        if (segmentInterfaces.Length > 0)
        {
            var interfaceTypeNamesJoined = symbol.Arity == 0
                ? string.Join(InterfaceSeparator, segmentInterfaces)
                : null;

            return new RegisteredTypeInfo(fullyQualifiedName, IsPipeline: false, symbol.Arity, symbol.IsValueType, lifetime, interfaceTypeNamesJoined, location);
        }

        if (PipelineShapeResolver.TryGetPipelineShape(symbol, out _, out _))
        {
            return new RegisteredTypeInfo(fullyQualifiedName, IsPipeline: true, symbol.Arity, symbol.IsValueType, lifetime, SegmentInterfaceTypeNamesJoined: null, location);
        }

        return null;
    }

    private static DependencyLifetime GetLifetime(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: nameof(LifetimeAttribute) } attributeClass
                || attributeClass.ContainingNamespace.ToDisplayString() != "Dovetail.DependencyInjection"
            )
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int rawValue)
            {
                return (DependencyLifetime)rawValue;
            }
        }

        return DependencyLifetime.Transient;
    }

    private static string GenerateSource(ImmutableArray<RegisteredTypeInfo> types)
    {
        var builder = new StringBuilder()
            .AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .AppendLine()
            .AppendLine("namespace Microsoft.Extensions.DependencyInjection;")
            .AppendLine()
            .AppendLine("public static class DovetailServiceCollectionExtensions")
            .AppendLine("{")
            .AppendLine("    /// <summary>Registers every Dovetail pipeline segment and pipeline found in this compilation, at the lifetime given by [Lifetime(...)] (transient by default).</summary>")
            .AppendLine("    public static IServiceCollection AddPipelines(this IServiceCollection services)")
            .AppendLine("    {");

        foreach (var segment in types.Where(static t => !t.IsPipeline).OrderBy(static t => t.FullyQualifiedName, StringComparer.Ordinal))
        {
            builder.AppendLine($"        {GetRegistrationExpression(segment)};");

            if (segment.SegmentInterfaceTypeNamesJoined is { } interfaceTypeNamesJoined)
            {
                foreach (var interfaceTypeName in interfaceTypeNamesJoined.Split(new[] { InterfaceSeparator }, StringSplitOptions.None))
                {
                    builder.AppendLine($"        {GetInterfaceForwardingExpression(segment, interfaceTypeName)};");
                }
            }
        }

        foreach (var pipeline in types.Where(static t => t.IsPipeline).OrderBy(static t => t.FullyQualifiedName, StringComparer.Ordinal))
        {
            builder.AppendLine($"        {GetRegistrationExpression(pipeline)};");
        }

        builder.AppendLine("        return services;")
            .AppendLine("    }")
            .AppendLine("}");

        return builder.ToString();
    }

    private static string GetMethodName(DependencyLifetime lifetime) => lifetime switch
    {
        DependencyLifetime.Singleton => "AddSingleton",
        DependencyLifetime.Scoped => "AddScoped",
        _ => "AddTransient"
    };

    private static string GetRegistrationExpression(RegisteredTypeInfo type)
    {
        var methodName = GetMethodName(type.Lifetime);

        if (type.Arity == 0 && !type.IsValueType)
        {
            return $"services.{methodName}<{type.FullyQualifiedName}>()";
        }

        var typeExpression = type.Arity == 0
            ? type.FullyQualifiedName
            : $"{type.FullyQualifiedName}<{new string(',', type.Arity - 1)}>";

        return $"services.{methodName}(typeof({typeExpression}), typeof({typeExpression}))";
    }

    private static string GetInterfaceForwardingExpression(RegisteredTypeInfo segment, string interfaceTypeName)
    {
        var methodName = GetMethodName(segment.Lifetime);
        return $"services.{methodName}<{interfaceTypeName}>(sp => sp.GetRequiredService<{segment.FullyQualifiedName}>())";
    }
}
