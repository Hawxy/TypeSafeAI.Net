using System.Reflection;
using System.Runtime.InteropServices;

namespace TypeSafeAI;

internal static class SdkHeaders
{
    public const string RequestIdHeader = "x-typesafe-request-id";

    public static string Version { get; } = ResolveVersion();

    public static string UserAgent { get; } =
        $"TypeSafeAI/{Version} ({RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription})";

    public static string SdkHeader { get; } = $"dotnet/{Version}";

    public static string RuntimeHeader { get; } = RuntimeInformation.FrameworkDescription;

    private static string ResolveVersion()
    {
        var informational = typeof(SdkHeaders).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return typeof(SdkHeaders).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        // Drop the source revision suffix that reproducible builds append.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? informational[..plus] : informational;
    }
}
