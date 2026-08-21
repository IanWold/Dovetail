namespace Dovetail.Example.Infrastructure;

/// <summary>
/// Stands in for a real network hop, so segments that don't depend on each other visibly overlap instead of finishing suspiciously instantly.
/// </summary>
internal static class SimulatedLatency
{
    public static Task Delay(CancellationToken ct, int minMs = 30, int maxMs = 150) =>
        Task.Delay(Random.Shared.Next(minMs, maxMs), ct);
}
