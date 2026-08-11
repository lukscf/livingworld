# LivingWorld

A living world for [ServUO](https://github.com/ServUO/ServUO) shards in the T2A /
pre-AOS era.

This is not a spawner pack. It is a persistent simulation: ~2,200 citizens running
their own routines, an economy that produces and consumes real items, long-term
memory about each player, territory that changes hands, and an invisible GM that
injects narrative when it detects a crisis or a power imbalance in the world state.

**~27,500 lines of C# across 59 files, 22 in-game commands.** No LLM — all dialogue
is lexical and runs locally.

> ServUO is the emulator. This repository is only what was built **on top** of it.
> LivingWorld is not a fork; it installs over a clean **ServUO 57.4** tree and
> patches a handful of core files (see [Installation](#installation)).

---

## What makes the world alive

These are simulation systems, not spawn tables. Each runs continuously and persists
state to disk.

| System | What it does |
|---|---|
| **LWManager** (249 KB) | The brain. Holds the target population and pulses each citizen's state machine: work, travel, hunt, patrol, ambush, duel. Simulates events **off-screen** — deaths, rumors — so the world keeps happening where nobody is watching. |
| **LWCitizen** (270 KB) | The individual: profession, inventory, skills, social and survival state. |
| **LWEconomy** (60 KB) | A pre-AOS economy across every profession, matching the real vendors. Each profession **crafts or gathers its actual item** — no abstract numbers. |
| **LWDialogue** (89 KB) | Conversation engine, fully lexical. ~22 topics with synonyms, slang, and tolerance for **misspellings**. No network calls. |
| **LWTerritory** | STALKER-style A-Life: map control shifts over time **and with the player**. A PK camp you cleared does not come back the same. |
| **LWPlayerMemory** | Persistent world memory about each player (Nemesis + Radiant AI flavored), on disk, keyed by `Serial`. |
| **LWNarrativeEngine** | Invisible GM: reads world state and injects coherent events when it detects crises or one faction running away with it. |
| **LWLore** | Persistent server history — what happened becomes legend that NPCs retell **months later**. |
| **LWSurvivalMemory** | One bot per type learns about its own category, then replicates that knowledge to the rest. |
| **LWSocialMemory** | NPC-to-NPC relationship memory: who fought whom stays fought. |
| **LWFame** | Emergent fame — reputation grows out of what an NPC actually did, not a pre-authored slot. |
| **LWSagaEvents** (43 KB) | Large-scale themed world events. |
| **LWWild** | Fauna with predators genuinely hunting prey; undead that only come out at night. |
| **LWWeather** | Weather with mechanical effect, not flavor text. |

### Navigation — the hard part

In-town pathfinding is where shards with roaming NPCs usually break. The approach
here is not a better algorithm, it is **observed data**:

- **LWPassiveNav** passively records where any player walks inside city limits.
- **LWLocalNav** builds the last-mile mesh entirely from segments somebody **actually walked** — it is the sole source of in-town walkability.
- **LWPathEdit** is the in-game visual editor for that mesh (GM).
- **LWNavHealth** is telemetry plus self-diagnosis: it detects and classifies stuck spots.
- **LWNavGump** lists those spots and teleports the GM straight to each one.

### Interface

- **LWGump** — control panel, 5 tabs: Status / Systems / Events / Nav / Admin.
- **LWDialogGump** — Skyrim-style clickable dialogue in 3 modes: questions, trade, quests.
- **LWSpeech** + **LWChatter** — 500+ ambient lines per bot type.

> Ambient and dialogue lines ship in Brazilian Portuguese, written without accents
> (a UO chat client limitation). They live in `LWSpeech.cs` and `LWChatter.cs` and are
> plain string arrays — translate or replace them without touching any other system.

---

## Addons (`src/Addons/`)

Built on top of LivingWorld, each independent of the others:

| | |
|---|---|
| **CityLords** | One Lord per guarded city: 1000 HP, a mask dyed in the city's color, patrolling bank/tavern/forge/moongate. Escorted by 5 guards and 4 Nightmares bound via `ControlOrder.Guard` — reusing ServUO's native `DoOrderGuard`, the same mechanism behind any player pet. |
| **GateFavors** | Shouting `Gate to Britain please!` in public chat has a 10% chance someone nearby obliges — or that a voice with no owner answers, if nobody is around. |
| **VendorMalls** | Recreates the classic "gate at the bank to my vendor": 4 themed shops plus a crier beside the bank. |
| **WorldEvents** | Random world events with **no fixed schedule** — an internal timer rolls for them. |
| **FactionWar** | The 4 factions (Minax, Council of Mages, True Britannians, Shadowlords) at open war in Felucca. |

---

## Installation

Over a clean **ServUO 57.4** tree.

**0.** Set `Config/Expansion.cfg` → `CurrentExpansion=UOR`. Stock ships `EJ` (Endless
Journey); LivingWorld is written for T2A / pre-AOS, where skills, loot, creature
stats, and faction rules all differ. Miss this and the world quietly behaves as
something it was not designed for.

**1.** Copy the contents of `src/` into `Scripts/Custom/`:

```bash
cp -r src/LivingWorld src/Addons/*.cs /path/to/servuo/Scripts/Custom/
```

ServUO compiles `Scripts/` recursively — the folder layout does not affect the build,
only your own organization.

**2.** Apply the core patches — see [`patches/README.md`](patches/README.md).

Four of them reference LivingWorld types and are easy to spot. The rest **never
mention LivingWorld**, and those are the ones that matter most:

- `Server/Mobile.cs` — make `AddItem` virtual. A build prerequisite: `BaseCreature`
  overrides it. This one fails loudly.
- `BaseCreature.cs` — `CurrentSpeed` returns 0 delay only when a player is watching.
  Without it, ~2,200 off-screen NPCs run their AI at zero delay and the shard melts.
- `BaseAI.cs` — navpoint following that continues an active A\* route instead of
  restarting it. Without it, `CityLords` patrols snag on walls.
- `Mining.cs` / `Lumberjacking.cs` — harvest lock granularity, which is what
  `LWEconomy` hits when every miner in the world works at once.

Except for the build prerequisite, skipping any of these degrades the shard
**silently** — no error, no log line.

**3.** Build and boot. On first startup `LWManager` populates the world; run `[lw` to
open the control panel.

### The `legacy/` dependency

`LWLegacy.cs` exists to retire the previous generation of static bots: their classes
still compile (old savefiles reference them), but nothing spawns them anymore and a
purge removes surviving instances at boot.

It does this with a list of 26 `typeof()` entries, so **`src/LivingWorld/` will not
compile without the 6 old bot files** in `legacy/` (`WorldNPCs`, `MoreNPCs`,
`PlayerBots`, `FactionNPCs`, `BankScene`, `WarNPCs`). Two ways out:

- **Clean install:** copy those 6 from `legacy/` as well. Nothing will be spawned.
- **Drop the legacy tail:** delete `LWLegacy.cs`, or replace the `typeof()` entries
  with name-based lookup via `ScriptCompiler.FindTypeByName`. You only need it if
  you are migrating a shard that already ran the old bots.

`LWLegacy.cs` is the **only** one of the 34 core files that touches those types. The
other 33 have no dependency outside stock ServUO.

---

## Commands

| Command | Level | |
|---|---|---|
| `[lw` | GM | Control panel (5 tabs) |
| `[lwwild` | GM | Wildlife |
| `[lords` | GM | City Lords |
| `[malls` | GM | Vendor Malls |
| `[we` | GM | World Events |
| `[factionwar` | GM | Faction war |
| `[gonpc` / `[findnpcs` / `[listnpcs` | GM | NPC diagnostics |
| `[repopulate` / `[repopulate2` / `[repopbanks` / `[repopwarzones` | GM | Repopulation |
| `[placechamps` / `[activatechamps` / `[listchamps` | Admin | T2A champion spawns |
| `[spawncamps` | Admin | Wilderness camps |
| `[cleanghosts` / `[resyncall` | GM | Clear entities stuck in `Map.Internal`, force client resync |
| `[gobritain` | Player | Quick trip to Britain |
| `[tpplayer` | Admin | Teleport a player |

## Persistence

State survives restarts in plain-text files at the server root:

```
livingworld_player_memory.txt      memory about each player
livingworld_territory.txt          territory control
livingworld_lore.txt               server history / legends
livingworld_fame.txt               emergent fame
livingworld_social_memory.txt      NPC-to-NPC relationships
livingworld_survival_knowledge.txt per-category learning
livingworld_recorded_paths.txt     recorded navigation mesh
livingworld_learned_paths.txt      consolidated paths
livingworld_deleted_via_points.txt points removed in the editor
livingworld_settings.txt           configuration
livingworld_status.txt             current status
livingworld_debug.txt              debug
```

## Repository layout

```
src/LivingWorld/   34 files — the simulation core
src/Addons/         5 files — CityLords, GateFavors, VendorMalls, WorldEvents, FactionWar
legacy/            17 files — retired bots (6, required by LWLegacy)
                              + world setup and spawners from the previous generation (11)
shard/              3 files — specific to the origin shard, read before installing
patches/                    — the four core patches
```

`shard/` holds `OptusNPC.cs` (an HTTP bridge to an external bot at `127.0.0.1:5050`),
`SpawnOptusCmd.cs`, and `AutoFixPosition.cs` (a temporary hook with a hardcoded
character name). They are committed for completeness but are not part of the package.

---

## License

[GPL-2.0](LICENSE) — the same as [ServUO](https://github.com/ServUO/ServUO), of which
this code is a derivative work: it inherits from the emulator's classes
(`BaseCreature`, `BaseAI`, `PlayerMobile`) and compiles into the same server binary.

Free for everyone, on purpose. Use it, study it, modify it, redistribute it. The one
obligation is reciprocity: if you distribute a modified version, it ships under
GPL-2.0 too, source included. Nobody gets to close this world.

Copyright © 2026 Lucas Cruz Ferreira. Licensing is not signing away authorship — the
license is what keeps the work everyone's.
