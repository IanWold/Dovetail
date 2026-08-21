using System.Collections.Immutable;

namespace Dovetail;

internal readonly record struct TypeDeclarationModel(
    string Namespace,
    string Name,
    bool IsPartial,
    ImmutableArray<ContainingTypeModel> ContainingTypes,
    string TypeParameterList,
    string Keyword
);
