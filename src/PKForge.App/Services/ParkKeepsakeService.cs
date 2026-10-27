using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// The park keepsake (<see cref="ParkKeepsakeFormat"/>): the Poképark's preferences and the Bank's own files, written to
/// the first linked saves folder whenever the park closes or the app goes to the background (at most once a minute),
/// so a reinstall, a new phone or a new build can bring them back. Restore is only offered to an app with no park and
/// no Bank, before the park fills itself for the first time.
/// </summary>
public sealed class ParkKeepsakeService(IFolderFileAccess files, IWatchedRootStore roots, IBankService bank)
{
    /// <summary>Set once the park has filled itself; before that, a park with no roster is a fresh one.</summary>
    private const string RosterKey = "pkforge.pokepark.roster.v1";
    private static string BankDirectory => Path.Combine(FileSystem.AppDataDirectory, "bank");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastWritten;

    private static int Residents()
    {
        var roster = Preferences.Default.Get<string?>(RosterKey, null);
        if (string.IsNullOrWhiteSpace(roster)) return 0;
        try { return System.Text.Json.JsonDocument.Parse(roster).RootElement.GetArrayLength(); }
        catch (System.Text.Json.JsonException) { return 0; }
    }

    /// <summary>Writes a copy when one is due, or now when <paramref name="forced"/>. Returns what happened, for the player.</summary>
    public async Task<string> WriteAsync(bool forced = false)
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return "A keepsake is already being written.";
        try
        {
            var now = DateTimeOffset.Now;
            if (!forced && !ParkKeepsakeFormat.IsDue(_lastWritten, now)) return "A keepsake was written less than a minute ago.";
            var (residents, entries) = (Residents(), bank.GetAll().Count);
            if (!ParkKeepsakeFormat.WorthKeeping(residents, entries)) return "There's no park or Bank to keep yet.";
            if (roots.GetRoots() is not [var root, ..]) return "Link a saves folder first: the keepsake is kept there.";
            var preferences = Platforms.Android.AndroidParkPreferences.ReadPark();
            // A park with residents whose preferences read as none means the store wasn't the one MAUI uses: write
            // nothing rather than a keepsake that would restore an empty park.
            if (residents > 0 && preferences.Count == 0) { AppLog.Warn("keepsake", "Park preferences read as empty; nothing written."); return "The park couldn't be read, so no keepsake was written."; }
            var keepsake = new ParkKeepsake(ParkKeepsakeFormat.CurrentVersion, now, AppInfo.Current.VersionString,
                preferences, ReadBankFiles(), residents, entries);
            var slot = ParkKeepsakeFormat.NextSlot(await ExistingAsync(root.TreeId).ConfigureAwait(false));
            await files.WriteFileAsync(root.TreeId, slot, ParkKeepsakeFormat.Write(keepsake)).ConfigureAwait(false);
            _lastWritten = now;
            return $"Kept {residents} residents and {entries} Bank entries in {root.DisplayName} ({slot}).";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Java.Lang.Exception)
        {
            AppLog.Warn("keepsake", $"Writing the keepsake: {error.Message}");
            return $"The keepsake couldn't be written: {error.Message}";
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<(string Name, DateTimeOffset? SavedAt)>> ExistingAsync(string treeId)
    {
        var found = new List<(string, DateTimeOffset?)>();
        foreach (var doc in await files.ListFilesAsync(treeId).ConfigureAwait(false))
            if (ParkKeepsakeFormat.IsKeepsake(doc.DisplayName))
                found.Add((doc.DisplayName, ParkKeepsakeFormat.Read((await files.ReadFileAsync(doc.DocumentId).ConfigureAwait(false)).ToArray())?.SavedAt));
        return found;
    }

    private static IReadOnlyList<KeepsakeFile> ReadBankFiles()
    {
        if (!Directory.Exists(BankDirectory)) return [];
        return Directory.EnumerateFiles(BankDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new KeepsakeFile(Path.GetRelativePath(BankDirectory, path).Replace('\\', '/'), Convert.ToBase64String(File.ReadAllBytes(path))))
            .ToArray();
    }

    /// <summary>The newest readable keepsake in any linked folder, when the app is empty enough to take it.</summary>
    public async Task<ParkKeepsake?> FindForEmptyAppAsync()
    {
        if (!ParkKeepsakeFormat.CanRestore(Residents(), bank.GetAll().Count)) return null;
        var copies = new List<ParkKeepsake?>();
        try
        {
            foreach (var root in roots.GetRoots())
                foreach (var doc in await files.ListFilesAsync(root.TreeId).ConfigureAwait(false))
                    if (ParkKeepsakeFormat.IsKeepsake(doc.DisplayName))
                        copies.Add(ParkKeepsakeFormat.Read((await files.ReadFileAsync(doc.DocumentId).ConfigureAwait(false)).ToArray()));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Java.Lang.Exception)
        {
            AppLog.Warn("keepsake", $"Looking for a keepsake: {error.Message}");
        }
        return ParkKeepsakeFormat.Newest(copies);
    }

    /// <summary>Puts the keepsake back: the park's preferences, then the Bank's files. The app must restart after it,
    /// since the Bank and the park read their data when they start.</summary>
    public static void Restore(ParkKeepsake keepsake)
    {
        Platforms.Android.AndroidParkPreferences.Write(keepsake.Preferences);
        var root = Path.GetFullPath(BankDirectory);
        foreach (var file in keepsake.Bank)
        {
            var target = Path.GetFullPath(Path.Combine(root, file.Path));
            // Never write outside the Bank's folder, whatever a keepsake says.
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, Convert.FromBase64String(file.Base64));
        }
    }
}
