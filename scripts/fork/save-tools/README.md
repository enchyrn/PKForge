# Save tools

Small tools for adding EXP to a save by hand, when the Poképark daycare couldn't write it itself. Kept so the next session doesn't rebuild them. Use them only with the procedure in the PokeDaisy fork's [RESEARCH.md](https://github.com/enchyrn/pokedaisy/blob/main/docs/fork/RESEARCH.md) ("Manual save edits"): the game saved in-game and closed, PKForge swiped away, two backups first, a byte check and a read-back after.

- `gen3.py <save or state>...`: prints a Gen 3 save's newest slot (trainer, play time, map, party with nickname, species, EXP, level, friendship, PID). Its species and map names cover the early Emerald game only.
- `add_exp.py <in> <out> <pid hex> <add> <expected exp>`: adds EXP to the party Pokémon with that PID in an Emerald save's newest slot, fixing the Pokémon's checksum and the section's. It refuses if a checksum is already bad or the EXP isn't the expected value.
- `ubexp/` (`dotnet run -c Release -- <save> <nickname> <hours> [out]`): finds an Unbound Pokémon by nickname, works out a Gentle stay of that many hours on Unbound's own growth curve, and with `[out]` writes it through PKForge's Unbound session (`AddDaycareExperience`), then re-opens the result and checks every other party and PC slot is unchanged. The assembly is named `DexGen` only because the engine shows its internals to that name.

On 2026-10-27 Tyran's edit kept only the live save slot's party sector from `ubexp`'s output (before fork.6 the commit also rewrote the older slot's footer). Since fork.6 the commit leaves the older slot's footer alone, but copying only the live slot's sector is still the smallest change.
