namespace Dovetail.DependencyInjection;

/// <summary>
/// The lifetime a pipeline or segment is registered with by <c>AddPipelines()</c>.
/// </summary>
public enum ServiceLifetime
{
    /// <summary>A new instance is created every time the service is requested.</summary>
    Transient,

    /// <summary>One instance is created per DI scope.</summary>
    Scoped,

    /// <summary>One instance is created and shared for the lifetime of the application.</summary>
    Singleton
}
