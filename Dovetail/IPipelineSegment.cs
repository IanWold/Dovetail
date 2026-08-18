namespace Dovetail;

/// <summary>
/// Marks a type as a pipeline segment. Implemented automatically by every
/// generic <c>IPipelineSegment</c> variant; not intended to be implemented
/// directly.
/// </summary>
public interface IPipelineSegment
{
}

/// <summary>
/// A pipeline segment that consumes no inputs and produces
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">Type of the result this segment produces. No other segment
/// in the same pipeline may produce this type.</typeparam>
public interface IPipelineSegment<TResult> : IPipelineSegment
{
    /// <summary>
    /// Executes this segment. Called at most once per pipeline
    /// execution, even when several segments depend on its result.
    /// </summary>
    /// <param name="token">Cancelled when the pipeline is cancelled or when
    /// another segment in the same execution faults.</param>
    /// <returns>The result this segment contributes to the pipeline.</returns>
    Task<TResult> RunAsync(CancellationToken token);
}
/// <summary>
/// A pipeline segment that consumes one input and produces
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="TResult">Type of the result this segment produces. No other segment
/// in the same pipeline may produce this type.</typeparam>
public interface IPipelineSegment<T1, TResult> : IPipelineSegment
{
    /// <summary>
    /// Executes this segment. Called at most once per pipeline
    /// execution, even when several segments depend on its result.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or when
    /// another segment in the same execution faults.</param>
    /// <returns>The result this segment contributes to the pipeline.</returns>
    Task<TResult> RunAsync(T1 arg1, CancellationToken token);
}
/// <summary>
/// A pipeline segment that consumes two inputs and produces
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T2">Type of the second input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="TResult">Type of the result this segment produces. No other segment
/// in the same pipeline may produce this type.</typeparam>
public interface IPipelineSegment<T1, T2, TResult> : IPipelineSegment
{
    /// <summary>
    /// Executes this segment. Called at most once per pipeline
    /// execution, even when several segments depend on its result.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or when
    /// another segment in the same execution faults.</param>
    /// <returns>The result this segment contributes to the pipeline.</returns>
    Task<TResult> RunAsync(T1 arg1, T2 arg2, CancellationToken token);
}
/// <summary>
/// A pipeline segment that consumes three inputs and produces
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T2">Type of the second input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T3">Type of the third input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="TResult">Type of the result this segment produces. No other segment
/// in the same pipeline may produce this type.</typeparam>
public interface IPipelineSegment<T1, T2, T3, TResult> : IPipelineSegment
{
    /// <summary>
    /// Executes this segment. Called at most once per pipeline
    /// execution, even when several segments depend on its result.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or when
    /// another segment in the same execution faults.</param>
    /// <returns>The result this segment contributes to the pipeline.</returns>
    Task<TResult> RunAsync(T1 arg1, T2 arg2, T3 arg3, CancellationToken token);
}
/// <summary>
/// A pipeline segment that consumes four inputs and produces
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T2">Type of the second input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T3">Type of the third input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T4">Type of the fourth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="TResult">Type of the result this segment produces. No other segment
/// in the same pipeline may produce this type.</typeparam>
public interface IPipelineSegment<T1, T2, T3, T4, TResult> : IPipelineSegment
{
    /// <summary>
    /// Executes this segment. Called at most once per pipeline
    /// execution, even when several segments depend on its result.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or when
    /// another segment in the same execution faults.</param>
    /// <returns>The result this segment contributes to the pipeline.</returns>
    Task<TResult> RunAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, CancellationToken token);
}
/// <summary>
/// A pipeline segment that consumes five inputs and produces
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T2">Type of the second input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T3">Type of the third input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T4">Type of the fourth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T5">Type of the fifth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="TResult">Type of the result this segment produces. No other segment
/// in the same pipeline may produce this type.</typeparam>
public interface IPipelineSegment<T1, T2, T3, T4, T5, TResult> : IPipelineSegment
{
    /// <summary>
    /// Executes this segment. Called at most once per pipeline
    /// execution, even when several segments depend on its result.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="arg5">The fifth input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or when
    /// another segment in the same execution faults.</param>
    /// <returns>The result this segment contributes to the pipeline.</returns>
    Task<TResult> RunAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, CancellationToken token);
}
/// <summary>
/// A pipeline segment that consumes six inputs and produces
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T2">Type of the second input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T3">Type of the third input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T4">Type of the fourth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T5">Type of the fifth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T6">Type of the sixth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="TResult">Type of the result this segment produces. No other segment
/// in the same pipeline may produce this type.</typeparam>
public interface IPipelineSegment<T1, T2, T3, T4, T5, T6, TResult> : IPipelineSegment
{
    /// <summary>
    /// Executes this segment. Called at most once per pipeline
    /// execution, even when several segments depend on its result.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="arg5">The fifth input value.</param>
    /// <param name="arg6">The sixth input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or when
    /// another segment in the same execution faults.</param>
    /// <returns>The result this segment contributes to the pipeline.</returns>
    Task<TResult> RunAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, CancellationToken token);
}
/// <summary>
/// A pipeline segment that consumes seven inputs and produces
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T2">Type of the second input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T3">Type of the third input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T4">Type of the fourth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T5">Type of the fifth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T6">Type of the sixth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T7">Type of the seventh input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="TResult">Type of the result this segment produces. No other segment
/// in the same pipeline may produce this type.</typeparam>
public interface IPipelineSegment<T1, T2, T3, T4, T5, T6, T7, TResult> : IPipelineSegment
{
    /// <summary>
    /// Executes this segment. Called at most once per pipeline
    /// execution, even when several segments depend on its result.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="arg5">The fifth input value.</param>
    /// <param name="arg6">The sixth input value.</param>
    /// <param name="arg7">The seventh input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or when
    /// another segment in the same execution faults.</param>
    /// <returns>The result this segment contributes to the pipeline.</returns>
    Task<TResult> RunAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, CancellationToken token);
}
/// <summary>
/// A pipeline segment that consumes eight inputs and produces
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T1">Type of the first input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T2">Type of the second input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T3">Type of the third input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T4">Type of the fourth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T5">Type of the fifth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T6">Type of the sixth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T7">Type of the seventh input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="T8">Type of the eighth input. Must be produced by exactly one other
/// segment in the pipeline, or supplied by the pipeline request.
/// </typeparam>
/// <typeparam name="TResult">Type of the result this segment produces. No other segment
/// in the same pipeline may produce this type.</typeparam>
public interface IPipelineSegment<T1, T2, T3, T4, T5, T6, T7, T8, TResult> : IPipelineSegment
{
    /// <summary>
    /// Executes this segment. Called at most once per pipeline
    /// execution, even when several segments depend on its result.
    /// </summary>
    /// <param name="arg1">The first input value.</param>
    /// <param name="arg2">The second input value.</param>
    /// <param name="arg3">The third input value.</param>
    /// <param name="arg4">The fourth input value.</param>
    /// <param name="arg5">The fifth input value.</param>
    /// <param name="arg6">The sixth input value.</param>
    /// <param name="arg7">The seventh input value.</param>
    /// <param name="arg8">The eighth input value.</param>
    /// <param name="token">Cancelled when the pipeline is cancelled or when
    /// another segment in the same execution faults.</param>
    /// <returns>The result this segment contributes to the pipeline.</returns>
    Task<TResult> RunAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, CancellationToken token);
}