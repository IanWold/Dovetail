namespace Dovetail;

/// <summary>
/// A pipeline that produces a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">Type of the result this pipeline produces.</typeparam>
public interface IPipeline<TResult>
{
    /// <summary>
    /// Runs every segment in the pipeline, in parallel where their dependencies allow, and returns the result
    /// of the segment that produces <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="token">Cancelled when the pipeline is cancelled or fails.</param>
    /// <returns>The result produced by the pipeline's terminal segment.</returns>
    Task<TResult> ExecuteAsync(CancellationToken token);
}

/// <summary>
/// A pipeline that consumes a <typeparamref name="TInput"/> and produces a <typeparamref name="TResult"/>
/// </summary>
/// <typeparam name="TInput">Type of the input this pipeline consumes.</typeparam>
/// <typeparam name="TResult">Type of the result this pipeline produces.</typeparam>
public interface IPipeline<TInput, TResult>
{
    /// <summary>
    /// Runs every segment in the pipeline, in parallel where their dependencies allow, and returns the result
    /// of the segment that produces <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="input">The input for the pipeline.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or fails.</param>
    /// <returns>The result produced by the pipeline's terminal segment.</returns>
    Task<TResult> ExecuteAsync(TInput input, CancellationToken token);
}
