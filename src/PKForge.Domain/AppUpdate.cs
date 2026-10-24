namespace PKForge.Domain;

/// <summary>The latest installable release discovered by the app.</summary>
public sealed record AvailableAppUpdate(
    string Version,
    string ReleaseUrl,
    string DownloadUrl,
    long SizeBytes);

/// <summary>Result of comparing the installed app with GitHub's latest release.</summary>
public sealed record AppUpdateCheck(
    bool IsAvailable,
    AvailableAppUpdate? Update,
    string Message);

public enum AppUpdateInstallResult
{
    InstallerOpened,
    InstallPermissionRequired,
    ReleasePageOpened,
}

/// <summary>Pure release-channel rules. Network and platform services stay in the app layer.</summary>
public static class AppUpdateRules
{
    /// <summary>Release tags are vMAJOR.MINOR.PATCH with an optional SemVer prerelease (such as -fork.1); build metadata is ignored and a prerelease sorts below its release.</summary>
    public static bool IsNewerVersion(string installed, string candidate)
    {
        if (!TryParse(installed, out var current) || !TryParse(candidate, out var next))
            return false;
        var core = next.Core.CompareTo(current.Core);
        if (core != 0)
            return core > 0;
        if (next.Prerelease.Length == 0 || current.Prerelease.Length == 0)
            return next.Prerelease.Length == 0 && current.Prerelease.Length != 0;
        return ComparePrerelease(next.Prerelease, current.Prerelease) > 0;
    }

    /// <summary>SemVer precedence for prerelease tags such as fork.9 vs fork.10: dot-separated
    /// identifiers compare left to right, numeric ones as numbers (and below alphanumeric ones),
    /// and a shorter run loses to a longer one that starts the same.</summary>
    private static int ComparePrerelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = ulong.TryParse(a[i], out var aNumber);
            var bNumeric = ulong.TryParse(b[i], out var bNumber);
            var order = (aNumeric, bNumeric) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i]),
            };
            if (order != 0)
                return order;
        }
        return a.Length.CompareTo(b.Length);
    }

    /// <summary>Don't-remind-me applies to one release. A newer release always asks again.</summary>
    public static bool ShouldPromptAutomatically(string installed, string candidate, string skipped) =>
        IsNewerVersion(installed, candidate) && !string.Equals(candidate, skipped, StringComparison.OrdinalIgnoreCase);

    private static bool TryParse(string value, out ReleaseVersion version)
    {
        var prerelease = "";
        var text = value.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            text = text[1..];
        var plus = text.IndexOf('+');
        if (plus >= 0)
            text = text[..plus];
        // Split off the prerelease (fork tags carry one, e.g. 3.1.1-fork.1); it is kept
        // and compared by IsNewerVersion rather than discarded.
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
        }
        if (!Version.TryParse(text, out var core))
        {
            version = default;
            return false;
        }
        version = new ReleaseVersion(core, prerelease);
        return true;
    }

    private readonly record struct ReleaseVersion(Version Core, string Prerelease);
}
