namespace Dovetail;

internal readonly record struct ContainingTypeModel(
    string Name,
    string Keyword,
    bool IsPartial,
    bool IsGeneric
);
