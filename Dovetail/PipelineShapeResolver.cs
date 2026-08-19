using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Dovetail;

internal static class PipelineShapeResolver
{
    internal static readonly SymbolDisplayFormat TypeNameFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    internal static bool TryGetPipelineShape(INamedTypeSymbol pipelineType, out string? inputTypeName, out string? resultTypeName)
    {
        inputTypeName = null;
        resultTypeName = null;

        var matches =
            pipelineType.AllInterfaces
            .Where(static i =>
                i.Arity is 1 or 2
                && i.Name == "IPipeline"
                && i.ContainingNamespace.ToDisplayString() == "Dovetail"
            )
            .ToImmutableArray();

        if (matches.Length != 1)
        {
            return false;
        }

        var pipelineInterface = matches[0];
        if (pipelineInterface.Arity == 1)
        {
            resultTypeName = pipelineInterface.TypeArguments[0].ToDisplayString(TypeNameFormat);
        }
        else
        {
            inputTypeName = pipelineInterface.TypeArguments[0].ToDisplayString(TypeNameFormat);
            resultTypeName = pipelineInterface.TypeArguments[1].ToDisplayString(TypeNameFormat);
        }

        return true;
    }

    internal static bool TryGetSegmentShape(INamedTypeSymbol segmentType, out ImmutableArray<string> inputTypeNames, out string resultTypeName)
    {
        inputTypeNames = ImmutableArray<string>.Empty;
        resultTypeName = "";

        var matches =
            segmentType.AllInterfaces
            .Where(static i =>
                i.Arity is >= 1 and <= 9
                && i.Name == "IPipelineSegment"
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

        return true;
    }
}
