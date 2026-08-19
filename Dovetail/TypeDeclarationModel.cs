namespace Dovetail;

internal readonly record struct TypeDeclarationModel(
    string Namespace,
    string Name,
    bool IsPartial
);
