using System.Reflection;
using PKForge.Engine.Unbound;
// ubexp <save> <nickname> <hours> [out]: finds the Pokémon, works out a Gentle daycare stay on Unbound's own
// growth curve, and with [out] writes it through PKForge's Unbound session, then re-opens and checks the result.
// Named DexGen (ubexp.csproj) only because the engine shows its internals to that assembly. See ../README.md.
var (path, nick, hours, outPath) = (args[0], args[1], double.Parse(args[2]), args.ElementAtOrDefault(3));
var bytes = File.ReadAllBytes(path);
var session = new UnboundEngineSession(bytes);
var tryMon = typeof(UnboundEngineSession).GetMethod("TryMon", BindingFlags.NonPublic | BindingFlags.Instance)!;
UnboundMon? Mon(UnboundEngineSession s, int box, int slot) { try { return (UnboundMon?)tryMon.Invoke(s, [box, slot]); } catch { return null; } }
IEnumerable<(int Box, int Slot)> Slots() { for (var i = 0; i < 6; i++) yield return (-1, i); for (var b = 0; b < 25; b++) for (var i = 0; i < 30; i++) yield return (b, i); }
var hits = Slots().Select(p => (p, m: Mon(session, p.Box, p.Slot))).Where(x => x.m is { LooksValid: true } && x.m.Nickname == nick).ToList();
if (hits.Count != 1) { Console.WriteLine($"found {hits.Count} named {nick}"); return 1; }
var ((box, slot), mon) = (hits[0].p, hits[0].m!);
var rate = UnboundData.GrowthRateFor(mon.Species);
uint At(int l) => UnboundData.ExperienceAtLevel(rate, l);
double left = hours, total = 0, at = mon.Experience;
for (var level = UnboundData.LevelForExperience(mon.Species, mon.Experience); level < 100 && left > 0; level++)
{
    double start = At(level), next = At(level + 1), need = next - at, h = 8.0 * need / (next - start);
    if (left < h) { total += left * (next - start) / 8.0; break; }
    total += need; at = next; left -= h;
}
var add = (uint)Math.Floor(total);
var after = mon.Experience + add;
Console.WriteLine($"{nick}: box {box} slot {slot}, species {mon.Species}, growth rate {rate}, EXP {mon.Experience} (level {UnboundData.LevelForExperience(mon.Species, mon.Experience)}), {hours} h Gentle = +{add} -> {after} (level {UnboundData.LevelForExperience(mon.Species, after)})");
if (outPath is null) return 0;
var others = Slots().Where(p => p != (box, slot)).Select(p => (p, raw: Mon(session, p.Box, p.Slot)?.Let())).ToList();
// The daycare's own write (PKForge Fork 3.1.1-fork.6 on): EXP only on Unbound's curve, committed like an editor change.
session.AddDaycareExperience(box, slot, add);
var written = session.Serialize().ToArray();
File.WriteAllBytes(outPath, written);
var check = new UnboundEngineSession(written);
var back = Mon(check, box, slot)!;
if (back.Experience != after || back.Nickname != nick) { Console.WriteLine("CHECK FAILED: Tyran didn't read back"); return 2; }
var changed = others.Count(o => !(Mon(check, o.p.Box, o.p.Slot)?.Let() ?? []).SequenceEqual(o.raw ?? []));
Console.WriteLine(changed == 0 ? $"re-opened: EXP {back.Experience}, every other slot unchanged" : $"CHECK FAILED: {changed} other slots changed");
return changed == 0 ? 0 : 2;
static class Ext { public static byte[] Let(this UnboundMon m) => m.Buffer.AsSpan(m.Offset, m.Size).ToArray(); }
