using Dovetail.DependencyInjection;

namespace Dovetail;

internal readonly record struct RegisteredTypeInfo(string FullyQualifiedName, bool IsPipeline, int Arity, bool IsValueType, ServiceLifetime Lifetime);
