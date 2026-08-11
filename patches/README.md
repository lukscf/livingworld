# Core patches

LivingWorld is a layer, not a fork — but it is not zero-touch either.

## How this list was produced

Earlier versions of this document said "one core file", then "four", then "six". All
three were wrong, and all three came from the same broken method: **grepping the tree
for `LivingWorld`**. That only finds files that *name* LW types. It cannot see a
`virtual` added to a signature, a `<Reference>` in an XML project file, an expansion
changed in a `.cfg`, or a one-line lock change.

The correct method is a diff, and it is mechanical:

1. `Server/AssemblyInfo.cs` pins the upstream tag — here, `AssemblyVersion("57.4")`.
2. Fetch that exact tag and compare the whole tree, normalizing CRLF.
3. Cross-check against the next tag to separate upstream drift from local edits.

Result against **ServUO 57.4**: 6,737 files, **6,641 identical, 36 modified**, 60
ours alone (the 59 in `Scripts/Custom/` plus `makefile`), 8 upstream files absent.

If you are porting LivingWorld into your own shard, do the diff. Do not grep.

---

## Prerequisites

**ServUO 57.4.** Other revisions may work, but every line number and block below was
verified against 57.4.

**`Config/Expansion.cfg` → `CurrentExpansion=UOR`.** Stock 57.4 ships `EJ` (Endless
Journey). LivingWorld is written for T2A / pre-AOS: skills, loot tables, creature
stats, and faction rules all differ under EJ. This single line is the difference
between the world behaving as designed and quietly behaving as something else.

**`Scripts/Scripts.csproj` → add `<Reference Include="System.Net.Http" />`.** Only
needed if you install `shard/OptusNPC.cs`, which uses `HttpClient`. Without it the
scripts assembly does not build.

---

## Group A — patches that reference LivingWorld types

A grep for `LivingWorld` finds these four.

| File | Lines | Why |
|---|---|---|
| `Scripts/Mobiles/PlayerMobile.cs` | 2 | Feeds the navigation mesh |
| `Scripts/Misc/Notoriety.cs` | 13 | Rival NPC guilds are enemies, not criminals |
| `Scripts/Regions/GuardedRegion.cs` | 7 | Guards ignore NPC guild members |
| `Scripts/VendorInfo/GenericSell.cs` | 2 | Vendor markup gold sink *(optional)* |

### A1. `Scripts/Mobiles/PlayerMobile.cs` — navigation

```csharp
using Server.Custom.LivingWorld;
```

In `OnLocationChange`, as the **first** statement:

```csharp
protected override void OnLocationChange(Point3D oldLocation)
{
    LWPassiveNav.OnPlayerMoved(this, oldLocation);   // <-- add this
    CheckLightLevels(false);
    ...
```

`LWPassiveNav` records where players actually walk inside city limits. That recording
is the raw material for `LWLocalNav`, the mesh NPCs use to move around towns. Skip it
and the mesh never learns anything new.

### A2. `Scripts/Misc/Notoriety.cs` — rival guilds are enemies

In `MobileNotoriety`, after the `target == null` guard, before `if (Core.AOS)`:

```csharp
// LivingWorld: rival NPC guilds are ENEMIES to each other (orange). This makes
// a town brawl "lawful war" — not a crime, and it does not call the guards.
// NPC vs NPC only; players still see guild members as innocent (protected).
{
    var sgm = source as Server.Custom.LivingWorld.LWCitizen;
    if (sgm == null && source is Server.Mobiles.BaseCreature sbc && sbc.ControlMaster is Server.Custom.LivingWorld.LWCitizen scm) sgm = scm; // pet resolves to owner
    var tgm = target as Server.Custom.LivingWorld.LWCitizen;
    if (tgm == null && target is Server.Mobiles.BaseCreature tbc && tbc.ControlMaster is Server.Custom.LivingWorld.LWCitizen tcm) tgm = tcm;
    if (sgm != null && tgm != null && sgm.GuildId >= 0 && tgm.GuildId >= 0
        && sgm.GuildId != tgm.GuildId
        && Server.Custom.LivingWorld.LWGuilds.AreRivals(sgm.GuildId, tgm.GuildId))
        return Notoriety.Enemy;
}
```

Skip it and the rivalry in `LWGuild` becomes cosmetic.

### A3. `Scripts/Regions/GuardedRegion.cs` — guards stand down

In `IsGuardCandidate(Mobile m)`, after the existing early-out block:

```csharp
// NPC guild members (and their pets) are NEVER guard targets: guild fights are
// "war", not crime. Even if an area spell splashes a civilian and turns them
// gray, the guard should not kill them.
if (m is Server.Custom.LivingWorld.LWCitizen lw && lw.GuildId >= 0)
    return false;
if (m is BaseCreature pbc && pbc.ControlMaster is Server.Custom.LivingWorld.LWCitizen pcm && pcm.GuildId >= 0)
    return false;
```

Companion to A2 — without it, an area spell splashing a bystander gets the caster
executed by town guards.

### A4. `Scripts/VendorInfo/GenericSell.cs` — vendor markup *(optional)*

Two edits: the constant **and** the `return` that uses it. The constant alone does
nothing.

```csharp
public const double VendorMarkup = 1.12;   // 1.00 = no fee

public int GetBuyPriceFor(Item item, BaseVendor vendor)
{
    return (int)(1.90 * VendorMarkup * GetSellPriceFor(item, vendor));   // <-- markup here
}
```

Player-facing gold sink. Genuinely optional.

---

## Group B — core behavior LivingWorld assumes

**None of these mention LivingWorld.** They look like ordinary ServUO changes, so no
grep will surface them — and skipping them fails *silently*: no build error (except
B0, which fails loudly), no log line, just a shard that behaves worse for reasons
nothing points at.

### B0. `Server/Mobile.cs` — `AddItem` must be virtual *(build prerequisite)*

```diff
-public void AddItem(Item item)
+public virtual void AddItem(Item item)
```

Stock 57.4 declares it non-virtual. `BaseCreature.cs` (B1) declares
`public override void AddItem`. **Apply B1 without this and the build fails** — you
cannot override a non-virtual method. This one line is the reason the earlier
"six files" list was not actually installable.

### B1. `Scripts/Mobiles/Normal/BaseCreature.cs` — speed by visibility

**The most important patch here.** Without it a populated shard melts.

```csharp
private bool m_LastWatchedByPlayer; // detects the visible/not-visible transition

[CommandProperty(AccessLevel.GameMaster)]
public double CurrentSpeed
{
    get
    {
        // 0 delay (instant reaction) ONLY when a player can see this NPC
        // (36-tile radius). Out of sight, fixed at 0.2 -- this is what stops
        // 1000+ off-screen creatures from running their AI with no delay.
        //
        // The AI timer only recomputes its interval when something calls
        // OnCurrentSpeedChanged(). Since this value now changes on its own
        // with visibility, the TRANSITION has to be detected here and the
        // refresh fired immediately -- otherwise the timer stays stuck at 0.2.
        bool watching = IsPlayerWatching(36);
        if (watching != m_LastWatchedByPlayer)
        {
            m_LastWatchedByPlayer = watching;
            if (m_AI != null) m_AI.OnCurrentSpeedChanged();
        }

        return watching ? 0.0 : 0.2;
    }
    set { ... }
}
```

LivingWorld keeps thousands of citizens alive at once, nearly all off-screen. Stock
`CurrentSpeed` gives every one of them a zero AI delay.

Same file, optional: `AddItem` / `DenyArmor` — 40% of bots spawn without armor and
get civilian clothing on the same body layers instead of being naked. This is the
override that needs B0.

### B2. `Scripts/Mobiles/AI/BaseAI.cs` — navpoint following

Add `private int m_NavStuck;`, then in the navpoint branch:

1. **If an A\* route is already active for this same navpoint, keep following it**
   every tick instead of restarting. Restarting each call — the stock behavior —
   burns ticks re-failing the same step against the same obstacle. This is what
   actually gets NPCs around buildings.
2. Otherwise, try a direct step.
3. On failure, try a cheap 45°/90° sidestep (offsets `1, -1, 2, -2`) — handles
   crowds at no pathfinding cost.
4. Only after **3 consecutive stuck ticks**, start a real A\* (`MoveTo` creates the
   `PathFollower`), which step 1 then follows.
5. If even A\* cannot start, drop the point via `CheckNavPoint()`.

`CityLords` drives `NavPoints`/`CurrentNavPoint` directly and assumes this. Without
it, Lords and their escorts snag on walls.

### B3. `Scripts/Services/Harvest/Mining.cs` and `Lumberjacking.cs` — harvest lock

One line each:

```csharp
public override object GetLock(Mobile from, Item tool, HarvestDefinition def, object toHarvest) => this;
```

Changes harvest lock granularity. Matters precisely when many NPCs mine or chop
simultaneously — which is `LWEconomy` running every miner and lumberjack in the world.

---

## Group C — shard policy, not LivingWorld

Not required, but they shape the world LivingWorld was tuned for.

| File | What |
|---|---|
| `Scripts/Misc/CharacterCreation.cs` | Redirects Trammel → Felucca; T2A Felucca-only shard |
| `Spawns/trammel.xml` | ~7 spawners removed |
| `Config/Accounts.cfg` | `AccountsPerIp` 1→10, `ProtectPasswords` → `NewSecureCrypt` |
| `Config/Server.cfg`, `Config/DataPath.cfg` | Server identity and host paths — do not copy |

---

## The rest of the 36

Modified in the origin tree but **not** LivingWorld coupling. Listed so nothing is
hidden.

**Upstream bugfixes carried along** — worth cherry-picking, easy to lose in a
re-clone: `Bless.cs` (`AddBless(Caster→m)`), `Mannequin.cs` (`IsOwner(Owner→from)`),
`Sphynx.cs` (`Deposit→Withdraw` — it was *giving* 5000gp instead of charging),
`BaseWeapon.cs` (`/10 → /100` in a Throwing chance formula).

**Support:** `Server/Utility.cs` gains `ColorExtensions` (`Darken`/`Lighten`), which
`BarGraphRenderer.cs` uses. `Server/ScriptCompiler.cs` adds `-p:Platform=x64`.

**Not attributed** — these look like upstream refactors from after 57.4 rather than
local edits (auto-properties, methods becoming `static`), and I did not confirm
either way: `Server/Main.cs`, `Server/Item.cs`, `Server/Network/{Listener,Packets,
PacketHandlers,NetState,SendQueue}.cs`, `Scripts/Accounting/IPLimiter.cs`,
`Scripts/Misc/ServerList.cs`, `Scripts/Commands/SignParser.cs`, `BaseGalleon.cs`,
`Data/signs.cfg`. Four of them (`NetState.cs`, `SendQueue.cs`, `SignParser.cs`,
`signs.cfg`) stop differing at tag 57.4.1, which is good evidence they are drift and
not ours.

**Absent from the origin tree:** `Data/Binary/Bounds.bin` is not committed. Not
fatal — `Server/ItemBounds.cs` warns and continues — but a clean checkout boots
without item bounds.
