# Roadmap

The project's main roadmap, its brainstorm and the research behind it live in the PokeDaisy fork, the hub since 2026-10-26: [ROADMAP.md](https://github.com/enchyrn/pokedaisy/blob/main/docs/fork/ROADMAP.md) (start at "Where things stand") and [RESEARCH.md](https://github.com/enchyrn/pokedaisy/blob/main/docs/fork/RESEARCH.md). This page is PKForge's part of it. For what the fork has changed so far see [README.md](README.md).

## PKForge's role

- **A design reference, not a code dependency.** The Poképark, its daycare (with the pace setting), the Bank and the offline journal are the ideas PokeDaisy's modules borrow. PKHeX.Core stays here.
- **Camp belongs to PokeDaisy.** Its state will live in a camp journal beside the save. A later PKForge step can read that journal and make camps walkable in the Poképark, queueing anything done there for PokeDaisy to apply. PKForge never edits the save for camp.
- **The daycare follows the Pokémon, not its bytes.** It writes to the same individual (trainer ID and PID) however it was played since, so EXP from PokeDaisy or a battle in between adds up with the stay's.

## Done

- 3.1.1-fork.1: signed fork releases, own app ID, daycare pace, `pokedaisy-backups` skipped.
- fork.2 to fork.4: Poképark identity (no refused pickups after a friendship tick, no duplicate residents), leave-and-return memories, CFRU residents known by their own record, ROM-hack sends fixed.
- fork.5: stays checked in with the save closed keep their Pokémon; refused pickups keep the stay.
- fork.6: Unbound write-back on its own growth curves (EXP only); Blissey's notebook for stays that couldn't be written; Unbound's party commit keeps each save slot's footer.

## Next

- **Try fork.6 on the phone:** an Unbound pickup with its save open (Tyran), a pickup with a save closed going into the notebook, settling it.
- **Radical Red and GS Chronicles write-back** through `CfruEngineSession`, on their own growth curves. Their stays wait in the notebook meanwhile.
- **The cross-game EXP rule** (open): when a Pokémon crosses into a game with another growth curve, keep its level and reset its progress, or keep half the gap. Decide before Bank transfers between the owner's games are built.
- **A legal twin for a hack Pokémon** (idea): a legal official-game Pokémon carrying a hack Pokémon's nickname, nature, gender, level and legal moves, a new individual. See PokeDaisy's ROADMAP work items.
- **Heart & Soul save engine,** only if the owner moves to Heart & Soul.
- **Reading PokeDaisy's camp journal** in the Poképark, once camp exists.

## Working here

- Tests: `mise x dotnet@10 -- dotnet test tests/PKForge.Engine.Tests -c Release`, and the same for `PKForge.Domain.Tests`. The MAUI app can't be built on this Mac: CI compile-checks it on every push, so push and read the build before tagging.
- Two daycare tests use the owner's own Unbound save, kept out of git in `.local-testdata/unbound-fork-tyran.srm`; they skip without it (as on CI). Upstream's Unbound ground-truth tests expect upstream's own `.local-testdata/unbound-v2111.srm` and fail on the owner's save.
- Releasing: README.md, "Releases and signing". Manual save edits: `scripts/fork/save-tools/`.
