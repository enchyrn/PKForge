using Android.Content;
using PKForge.Domain;

namespace PKForge.App.Platforms.Android;

/// <summary>
/// The Poképark's preferences, read and written straight from Android's store so keys built at run time
/// (a resident's birthday flag) come along, each with the type it was stored as. MAUI's <c>Preferences.Default</c>
/// is Android's default shared preferences, <c>&lt;package&gt;_preferences</c>.
/// </summary>
internal static class AndroidParkPreferences
{
// Android's default shared preferences (what PreferenceManager.GetDefaultSharedPreferences opens, now deprecated):
    // the file "<package>_preferences".
    private static ISharedPreferences Store
    {
        get
        {
            var context = global::Android.App.Application.Context;
            return context.GetSharedPreferences($"{context.PackageName}_preferences", FileCreationMode.Private)
                ?? throw new InvalidOperationException("Android's preferences are unavailable.");
        }
    }

    public static IReadOnlyList<KeepsakePreference> ReadPark()
    {
        if (Store.All is not { } all) return [];
        var keys = ParkKeepsakeFormat.ParkKeys(all.Keys);
        return keys.Select(key => Describe(key, all[key])).OfType<KeepsakePreference>().ToArray();
    }

    private static KeepsakePreference? Describe(string key, object? value) => value switch
    {
        string s => new(key, "string", s),
        Java.Lang.String s => new(key, "string", s.ToString()),
        int i => new(key, "int", i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        Java.Lang.Integer i => new(key, "int", i.IntValue().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        long l => new(key, "long", l.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        Java.Lang.Long l => new(key, "long", l.LongValue().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        bool b => new(key, "bool", b ? "true" : "false"),
        Java.Lang.Boolean b => new(key, "bool", b.BooleanValue() ? "true" : "false"),
        float f => new(key, "float", f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
        Java.Lang.Float f => new(key, "float", f.FloatValue().ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
        _ => null, // a string set: the park stores none
    };

    public static void Write(IReadOnlyList<KeepsakePreference> preferences)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        using var editor = Store.Edit() ?? throw new InvalidOperationException("Android's preferences can't be edited.");
        foreach (var p in preferences)
        {
            if (!p.Key.StartsWith(ParkKeepsakeFormat.ParkPrefix, StringComparison.Ordinal)) continue;
            _ = p.Type switch
            {
                "int" => editor.PutInt(p.Key, int.Parse(p.Value, inv)),
                "long" => editor.PutLong(p.Key, long.Parse(p.Value, inv)),
                "bool" => editor.PutBoolean(p.Key, p.Value == "true"),
                "float" => editor.PutFloat(p.Key, float.Parse(p.Value, inv)),
                _ => editor.PutString(p.Key, p.Value),
            };
        }
        if (!editor.Commit()) throw new IOException("Android didn't store the Poképark's preferences.");
    }
}
