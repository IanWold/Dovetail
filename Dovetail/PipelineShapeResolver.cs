using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Dovetail;

internal static class PipelineShapeResolver
{
    internal static readonly SymbolDisplayFormat TypeNameFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    internal static readonly SymbolDisplayFormat DisplayNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
    );

    internal static bool TryGetPipelineShape(INamedTypeSymbol pipelineType, out ImmutableArray<string> inputTypeNames, out string resultTypeName) =>
        TryGetShape(pipelineType, "IPipeline", out inputTypeNames, out resultTypeName, out _);

    internal static bool TryGetSegmentShape(INamedTypeSymbol segmentType, out ImmutableArray<string> inputTypeNames, out string resultTypeName) =>
        TryGetShape(segmentType, "IPipelineSegment", out inputTypeNames, out resultTypeName, out _);

    internal static bool TryGetSegmentInterface(INamedTypeSymbol segmentType, out string interfaceTypeName) =>
        TryGetShape(segmentType, "IPipelineSegment", out _, out _, out interfaceTypeName);

    private static bool TryGetShape(INamedTypeSymbol type, string interfaceName, out ImmutableArray<string> inputTypeNames, out string resultTypeName, out string interfaceTypeName)
    {
        inputTypeNames = ImmutableArray<string>.Empty;
        resultTypeName = "";
        interfaceTypeName = "";

        var candidateInterfaces = type.TypeKind == TypeKind.Interface
            ? type.AllInterfaces.Insert(0, type)
            : type.AllInterfaces;

        var matches = candidateInterfaces
            .Where(i =>
                i.Arity is >= 1 and <= 9
                && i.Name == interfaceName
                && i.ContainingNamespace.ToDisplayString() == "Dovetail"
            )
            .ToImmutableArray();

        if (matches.Length != 1)
        {
            return false;
        }

        var typeArguments = matches[0].TypeArguments;
        inputTypeNames = typeArguments
            .Take(typeArguments.Length - 1)
            .Select(static t => t.ToDisplayString(TypeNameFormat))
            .ToImmutableArray();
        resultTypeName = typeArguments[typeArguments.Length - 1].ToDisplayString(TypeNameFormat);
        interfaceTypeName = matches[0].ToDisplayString(TypeNameFormat);

        return true;
    }
}
