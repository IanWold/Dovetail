using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct PendingCollision(
    string ConsumerParameterName,
    int BindingIndex,
    string InputType,
    int PipelineInputIndex,
    string ProviderParameterName,
    Location? ConsumerLocation
);
