# Roadmap

The brainstorm about where this goes next moved to the PokeDaisy fork on 2026-10-26, once PokeDaisy became the hub: [enchyrn/pokedaisy `docs/fork/ROADMAP.md`](https://github.com/enchyrn/pokedaisy/blob/main/docs/fork/ROADMAP.md). For this fork itself see [README.md](README.md).

What it means for PKForge:

- **PKForge is a design reference, not a code dependency.** The Poképark, its daycare (including the pace setting), the Bank and the offline journal are the ideas the PokeDaisy modules borrow from. PKHeX.Core stays here.
- **Camp is owned by the PokeDaisy fork.** Its state will live in a camp journal file beside the save. A later PKForge step can read that journal and make camps walkable in the Poképark, queueing anything done there for PokeDaisy to apply. PKForge never edits the save for camp.
- **Daycare write-back follows the Pokémon, not its bytes.** The Poképark daycare writes back to the same individual (trainer ID and PID) however it was played since, so EXP a PokeDaisy module or a battle gave it in between adds up with the stay's rather than blocking it.
