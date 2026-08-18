namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill required for C# 9+ <c>init</c> accessors (and thus <c>record</c>/<c>record struct</c>)
/// to compile on netstandard2.0, which predates this type.
/// </summary>
internal static class IsExternalInit;
