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

/// <summary>
/// A pipeline that consumes two inputs and produces a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input this pipeline consumes.</typeparam>
/// <typeparam name="T2">Type of the second input this pipeline consumes.</typeparam>
/// <typeparam name="TResult">Type of the result this pipeline produces.</typeparam>
public interface IPipeline<T1, T2, TResult>
{
    /// <summary>
    /// Runs every segment in the pipeline, in parallel where their dependencies allow, and returns the result
    /// of the segment that produces <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or fails.</param>
    /// <returns>The result produced by the pipeline's terminal segment.</returns>
    Task<TResult> ExecuteAsync(T1 arg1, T2 arg2, CancellationToken token);
}

/// <summary>
/// A pipeline that consumes three inputs and produces a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input this pipeline consumes.</typeparam>
/// <typeparam name="T2">Type of the second input this pipeline consumes.</typeparam>
/// <typeparam name="T3">Type of the third input this pipeline consumes.</typeparam>
/// <typeparam name="TResult">Type of the result this pipeline produces.</typeparam>
public interface IPipeline<T1, T2, T3, TResult>
{
    /// <summary>
    /// Runs every segment in the pipeline, in parallel where their dependencies allow, and returns the result
    /// of the segment that produces <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or fails.</param>
    /// <returns>The result produced by the pipeline's terminal segment.</returns>
    Task<TResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, CancellationToken token);
}

/// <summary>
/// A pipeline that consumes four inputs and produces a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input this pipeline consumes.</typeparam>
/// <typeparam name="T2">Type of the second input this pipeline consumes.</typeparam>
/// <typeparam name="T3">Type of the third input this pipeline consumes.</typeparam>
/// <typeparam name="T4">Type of the fourth input this pipeline consumes.</typeparam>
/// <typeparam name="TResult">Type of the result this pipeline produces.</typeparam>
public interface IPipeline<T1, T2, T3, T4, TResult>
{
    /// <summary>
    /// Runs every segment in the pipeline, in parallel where their dependencies allow, and returns the result
    /// of the segment that produces <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or fails.</param>
    /// <returns>The result produced by the pipeline's terminal segment.</returns>
    Task<TResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, CancellationToken token);
}

/// <summary>
/// A pipeline that consumes five inputs and produces a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input this pipeline consumes.</typeparam>
/// <typeparam name="T2">Type of the second input this pipeline consumes.</typeparam>
/// <typeparam name="T3">Type of the third input this pipeline consumes.</typeparam>
/// <typeparam name="T4">Type of the fourth input this pipeline consumes.</typeparam>
/// <typeparam name="T5">Type of the fifth input this pipeline consumes.</typeparam>
/// <typeparam name="TResult">Type of the result this pipeline produces.</typeparam>
public interface IPipeline<T1, T2, T3, T4, T5, TResult>
{
    /// <summary>
    /// Runs every segment in the pipeline, in parallel where their dependencies allow, and returns the result
    /// of the segment that produces <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="arg5">The fifth input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or fails.</param>
    /// <returns>The result produced by the pipeline's terminal segment.</returns>
    Task<TResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, CancellationToken token);
}

/// <summary>
/// A pipeline that consumes six inputs and produces a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input this pipeline consumes.</typeparam>
/// <typeparam name="T2">Type of the second input this pipeline consumes.</typeparam>
/// <typeparam name="T3">Type of the third input this pipeline consumes.</typeparam>
/// <typeparam name="T4">Type of the fourth input this pipeline consumes.</typeparam>
/// <typeparam name="T5">Type of the fifth input this pipeline consumes.</typeparam>
/// <typeparam name="T6">Type of the sixth input this pipeline consumes.</typeparam>
/// <typeparam name="TResult">Type of the result this pipeline produces.</typeparam>
public interface IPipeline<T1, T2, T3, T4, T5, T6, TResult>
{
    /// <summary>
    /// Runs every segment in the pipeline, in parallel where their dependencies allow, and returns the result
    /// of the segment that produces <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="arg5">The fifth input value.</param>
    /// <param name="arg6">The sixth input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or fails.</param>
    /// <returns>The result produced by the pipeline's terminal segment.</returns>
    Task<TResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, CancellationToken token);
}

/// <summary>
/// A pipeline that consumes seven inputs and produces a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input this pipeline consumes.</typeparam>
/// <typeparam name="T2">Type of the second input this pipeline consumes.</typeparam>
/// <typeparam name="T3">Type of the third input this pipeline consumes.</typeparam>
/// <typeparam name="T4">Type of the fourth input this pipeline consumes.</typeparam>
/// <typeparam name="T5">Type of the fifth input this pipeline consumes.</typeparam>
/// <typeparam name="T6">Type of the sixth input this pipeline consumes.</typeparam>
/// <typeparam name="T7">Type of the seventh input this pipeline consumes.</typeparam>
/// <typeparam name="TResult">Type of the result this pipeline produces.</typeparam>
public interface IPipeline<T1, T2, T3, T4, T5, T6, T7, TResult>
{
    /// <summary>
    /// Runs every segment in the pipeline, in parallel where their dependencies allow, and returns the result
    /// of the segment that produces <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="arg5">The fifth input value.</param>
    /// <param name="arg6">The sixth input value.</param>
    /// <param name="arg7">The seventh input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or fails.</param>
    /// <returns>The result produced by the pipeline's terminal segment.</returns>
    Task<TResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, CancellationToken token);
}

/// <summary>
/// A pipeline that consumes eight inputs and produces a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input this pipeline consumes.</typeparam>
/// <typeparam name="T2">Type of the second input this pipeline consumes.</typeparam>
/// <typeparam name="T3">Type of the third input this pipeline consumes.</typeparam>
/// <typeparam name="T4">Type of the fourth input this pipeline consumes.</typeparam>
/// <typeparam name="T5">Type of the fifth input this pipeline consumes.</typeparam>
/// <typeparam name="T6">Type of the sixth input this pipeline consumes.</typeparam>
/// <typeparam name="T7">Type of the seventh input this pipeline consumes.</typeparam>
/// <typeparam name="T8">Type of the eighth input this pipeline consumes.</typeparam>
/// <typeparam name="TResult">Type of the result this pipeline produces.</typeparam>
public interface IPipeline<T1, T2, T3, T4, T5, T6, T7, T8, TResult>
{
    /// <summary>
    /// Runs every segment in the pipeline, in parallel where their dependencies allow, and returns the result
    /// of the segment that produces <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="arg5">The fifth input value.</param>
    /// <param name="arg6">The sixth input value.</param>
    /// <param name="arg7">The seventh input value.</param>
    /// <param name="arg8">The eighth input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or fails.</param>
    /// <returns>The result produced by the pipeline's terminal segment.</returns>
    Task<TResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, CancellationToken token);
}
