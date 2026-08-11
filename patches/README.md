# Core patches

LivingWorld is a layer, not a fork — but it is not zero-touch either. It needs
**six** files in the stock ServUO tree patched.

They fall into two groups, and the second group is the one that bites.

### Group A — patches that reference LivingWorld types

Easy to find: grep the tree for `LivingWorld` and they show up.

| File | Lines | Why |
|---|---|---|
| `Scripts/Mobiles/PlayerMobile.cs` | 2 | Feeds the navigation mesh |
| `Scripts/Misc/Notoriety.cs` | ~13 | Rival NPC guilds are enemies, not criminals |
| `Scripts/Regions/GuardedRegion.cs` | 7 | Guards ignore NPC guild members |
| `Scripts/VendorInfo/GenericSell.cs` | 2 | Vendor markup as a gold sink (optional) |

### Group B — core behavior LivingWorld assumes

**These never mention LivingWorld.** They are ordinary-looking changes to stock
ServUO behavior, so a grep for `LivingWorld` will not find them — but LivingWorld
depends on both, and skipping them degrades the shard silently, with no error.

| File | What | Skip it and… |
|---|---|---|
| `Scripts/Mobiles/Normal/BaseCreature.cs` | `CurrentSpeed` returns 0.0 only when a player is watching | **~2,200 NPCs run their AI with zero delay off-screen.** This is the CPU fix that makes the population viable at all. |
| `Scripts/Mobiles/AI/BaseAI.cs` | Navpoint following: continue an active A* route, cheap 45°/90° sidestep, real A* only after 3 stuck ticks | `CityLords` patrols snag on walls — it drives `NavPoints`/`CurrentNavPoint` directly and relies on this. |

`Server/Mobile.cs` shows no locally-authored markers, but this tree is a snapshot of
a different ServUO revision than current master, so a diff against upstream is pure
noise and I could not prove it clean. Treat "untouched" as unverified.

---

## A1. `Scripts/Mobiles/PlayerMobile.cs` — navigation

Add the `using` at the top of the file:

```csharp
using Server.Custom.LivingWorld;
```

Then, in `OnLocationChange`, as the **first** statement:

```csharp
protected override void OnLocationChange(Point3D oldLocation)
{
    LWPassiveNav.OnPlayerMoved(this, oldLocation);   // <-- add this
    CheckLightLevels(false);
    ...
```

**Why:** `LWPassiveNav` passively records where players actually walk inside city
limits. That recording is the raw material for `LWLocalNav`, the last-mile navigation
mesh NPCs use to move around towns.

Skip it and the rest still compiles and runs, but the in-town mesh never learns
anything new — NPCs stay limited to whatever is already in
`livingworld_recorded_paths.txt`, or to nothing at all on a fresh install.

## A2. `Scripts/Misc/Notoriety.cs` — rival guilds are enemies

In `MobileNotoriety`, after the `target == null` guard and **before** the
`if (Core.AOS)` block:

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

**Why:** flags rival guild members orange to each other so their fights are legal war.
Note the pet resolution — a summon inherits its `ControlMaster`'s guild.

Skip it and rival guilds read as innocent to one another; the rivalry in `LWGuild`
becomes cosmetic.

## A3. `Scripts/Regions/GuardedRegion.cs` — guards stand down

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

**Why:** companion to A2. Even with the notoriety fix, an area spell splashing a
bystander turns the caster gray and town guards execute them.

## A4. `Scripts/VendorInfo/GenericSell.cs` — vendor markup *(optional)*

Two edits — the constant **and** the `return` that uses it. Adding only the constant
does nothing.

```csharp
// LivingWorld: vendor markup = player-facing gold sink.
// Affects ONLY players buying from the vendor gump; bots buy straight from
// BuyPack and never pass through here.
// Tune VendorMarkup to calibrate the drain (1.00 = no fee, 1.12 = +12%).
public const double VendorMarkup = 1.12;

public int GetBuyPriceFor(Item item, BaseVendor vendor)
{
    return (int)(1.90 * VendorMarkup * GetSellPriceFor(item, vendor));   // <-- markup added here
}
```

**Why:** an economy where NPCs continuously produce goods needs somewhere for gold to
leave. Genuinely optional — skip it, or set `1.00`, and nothing else changes.

---

## B1. `Scripts/Mobiles/Normal/BaseCreature.cs` — speed by visibility

**The most important patch in this document.** Without it a populated shard melts.

Add the field:

```csharp
private bool m_LastWatchedByPlayer; // detects the visible/not-visible transition
```

Then replace the `CurrentSpeed` getter:

```csharp
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
        // OnCurrentSpeedChanged() (normally just on state changes, e.g.
        // idle->combat). Since this value now changes on its own with
        // visibility, the TRANSITION has to be detected right here and the
        // refresh fired immediately -- otherwise the timer picks up the new
        // value only by coincidence, and stays stuck at 0.2.
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

**Why:** LivingWorld keeps thousands of citizens alive simultaneously, almost all of
them off-screen. Stock `CurrentSpeed` gives every one of them a zero AI delay. The
transition detection is not optional polish — without it the value gets stuck at 0.2
and on-screen NPCs feel sluggish.

### Also in this file: civilian clothing *(optional)*

40% of bots spawn with no armor, and get basic civilian clothing on the same body
layers instead of being naked. Cosmetic — see `AddItem` / `DenyArmor`.

## B2. `Scripts/Mobiles/AI/BaseAI.cs` — navpoint following

In the navpoint-following branch, plus a field:

```csharp
private int m_NavStuck; // consecutive ticks stuck following navpoints
```

The logic, in order:

1. **If an A\* route is already active for this same navpoint, keep following it**
   every tick instead of restarting. Restarting each call — the stock behavior —
   wastes ticks re-failing the same step against the same obstacle between A\* calls.
   This is what actually gets NPCs around buildings.
2. Otherwise try a direct step.
3. If that fails, try a cheap 45°/90° sidestep (offsets `1, -1, 2, -2`) — handles
   crowds at no pathfinding cost.
4. Only when **genuinely stuck for 3 consecutive ticks**, start a real A\* (`MoveTo`
   creates the `PathFollower`), which step 1 then follows.
5. If even A\* cannot start, give up on the point via `CheckNavPoint()`.

**Why:** stock ServUO's navpoint handling assumes short, unobstructed hops. City
patrols are neither. `CityLords` drives `NavPoints`/`CurrentNavPoint` directly and
assumes this behavior; without it, Lords and their escorts snag on walls.

---

## Verifying your own tree

If you are extracting LivingWorld from an existing shard, do **not** trust
`grep -r LivingWorld` to find what to port — Group B does not mention LivingWorld at
all. That mistake is why this document originally listed four files instead of six.
Diff against the ServUO revision your tree actually forked from.
