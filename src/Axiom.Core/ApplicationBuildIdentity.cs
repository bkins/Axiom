using System.Reflection;

namespace Axiom.Core;

public sealed record ApplicationBuildIdentity(
    string ProductVersion,
    string InformationalVersion,
    string Provenance)
{
    public string DisplayText => $"Axiom {ProductVersion} ({Provenance})";

    public static ApplicationBuildIdentity FromAssembly(Assembly assembly)
    {
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Parse(informationalVersion, assembly.GetName().Version);
    }

    public static ApplicationBuildIdentity Parse(string? informationalVersion, Version? assemblyVersion)
    {
        var fallback = assemblyVersion is null
            ? "0.0.0"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(assemblyVersion.Build, 0)}";
        var normalized = string.IsNullOrWhiteSpace(informationalVersion) ? fallback + "+dev" : informationalVersion.Trim();
        var separator = normalized.IndexOf('+');
        var productVersion = separator < 0 ? normalized : normalized[..separator];
        var provenance = separator < 0 || separator == normalized.Length - 1 ? "dev" : normalized[(separator + 1)..];
        return new ApplicationBuildIdentity(productVersion, normalized, provenance);
    }
}
