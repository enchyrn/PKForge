# Roadmap

Brainstorm so far, as decisions and open ideas. Nothing here is built yet unless it says so. For the fork itself see [README.md](README.md).

## Principle: organic, not cheating

Companion features should make the game more convenient and more alive, not skip it.

- Rewards are earned by playing, and scaled to what Emerald itself gives: its Pickup lists, the Berry Master, daily limits.
- They're gated by story progress (badges and so on).
- Events, such as the Eon Ticket, arrive in story order.

The daycare pace change follows the same idea.

## Target

PokeDaisy is the hub. The plan is to fork PokeDaisy (GPLv3; it embeds upstream mGBA and melonDS as submodules, with one small melonDS patch) and add features as modules in that fork, behind a small host interface:

- read saves
- read live memory, through PokeDaisy's `MemoryReader`
- write on pack-up
- add companion screens

Keep the modules clean enough to become separate-app plugins later. PKForge stays a design reference, not a code dependency: PKHeX.Core is C#, and the organic features don't need it.

## Order

1. PokeDaisy fork and the module host.
2. Bank and transfers. Both directions are allowed, a deliberate break from the real games.
3. Camp.
4. Poképark.
5. Cobblemon bridge.
6. A modernized Emerald hack (pokeemerald-expansion), as a separate, later project.

## Camp

- Set up from the companion on the current route, any time, including during play.
- The party gains friendship.
- Pickup-style finds come from Emerald's own lists, are badge-gated and daily-capped.
- Route Pokémon visit, which shows what lives there.
- Leaving the route pauses the game and asks "Pack up" or "Stay a while". Pack-up is when rewards are written.
- Secret Bases can be developed later.

## Setup

- Phone: Redmi Note 15 Pro 5G (HyperOS 3, Android 16) with a Nova Lite controller.
- The game runs on the Mac through AirSync's native desktop mode (a scrcpy virtual display), with the touch screen and companion on the phone.
- A telescopic controller later.

## RetroArch option

RetroArch's Android build gained network commands (`READ_CORE_MEMORY`, UDP 55355) on 2026-03-25. They are in nightly builds only, after 1.22.2. A companion could use them to read a game running in RetroArch. PokeDaisy's action features (Fly, item use) call game code in-process, so they wouldn't work that way.

## Contributions

Forks only for now; contributions upstream can come later.
