using System.Diagnostics;

namespace TypeSafeAI;

/// <summary>Names used for tracing so hosts can subscribe with OpenTelemetry.</summary>
public static class TypeSafeDiagnostics
{
    /// <summary>The <see cref="ActivitySource"/> name; add it to your tracer provider to capture spans.</summary>
    public const string ActivitySourceName = "TypeSafeAI";

    internal static ActivitySource Source { get; } = new(ActivitySourceName, SdkHeaders.Version);
}
