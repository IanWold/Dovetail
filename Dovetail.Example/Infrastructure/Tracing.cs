using System.Diagnostics;

namespace Dovetail.Example.Infrastructure;

/// <summary>
/// Dovetail only emits dovetail.pipeline, dovetail.segment, and dovetail.segment.type activities when something is actually listening.
/// This is that something, printed to the console instead of shipped to a real collector so the example stays offline.
/// </summary>
internal static class Tracing
{
    public static void Enable()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Dovetail",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                var indent = activity.Parent is not null ? "    " : "";
                var status = activity.Status == ActivityStatusCode.Error ? $" [ERROR: {activity.StatusDescription}]" : "";
                Console.WriteLine($"{indent}[dovetail] {activity.OperationName} — {activity.Duration.TotalMilliseconds:F0}ms{status}");
            }
        };

        ActivitySource.AddActivityListener(listener);
    }
}
