using Dovetail.DependencyInjection;
using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct RegisteredTypeInfo(
    string FullyQualifiedName,
    bool IsPipeline,
    int Arity,
    bool IsValueType,
    DependencyLifetime Lifetime,
    string? SegmentInterfaceTypeNamesJoined,
    Location? Location,
    bool IsAccessibleForRegistration
);
