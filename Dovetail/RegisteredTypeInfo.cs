using Dovetail.DependencyInjection;
using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct RegisteredTypeInfo(
    string FullyQualifiedName,
    bool IsPipeline,
    int Arity,
    bool IsValueType,
    ServiceLifetime Lifetime,
    string? SegmentInterfaceTypeNamesJoined,
    Location? Location
);
