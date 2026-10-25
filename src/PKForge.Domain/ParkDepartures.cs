namespace PKForge.Domain;

/// <summary>
/// Residents who left the park, remembered so one sent back is the same resident again: its id,
/// and with it its care, bond, dialogue and friendships, which the park keeps by id. One memory
/// per resident (the latest), the newest <see cref="MostRemembered"/> only.
/// </summary>
public static class ParkDepartures
{
    public const int MostRemembered = 200;

    public static IReadOnlyList<T> Remember<T>(IReadOnlyList<T> departed, T leaving, Func<T, string> id) =>
        departed.Where(d => id(d) != id(leaving)).Append(leaving).TakeLast(MostRemembered).ToArray();

    public static IReadOnlyList<T> Forget<T>(IReadOnlyList<T> departed, string returningId, Func<T, string> id) =>
        departed.Where(d => id(d) != returningId).ToArray();
}
