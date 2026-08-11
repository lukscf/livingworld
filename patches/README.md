# Core patches

LivingWorld is a layer, not a fork — but it is not zero-touch either. It needs
**four** files in the stock ServUO tree patched, roughly 20 lines total.

Three of them exist for one reason: making **NPC guild warfare** work. Without them,
rival guilds brawling in town register as crime, guards intervene, and the whole
faction layer collapses into a guard-wall. The fourth is an economy knob.

| File | Lines | Why |
|---|---|---|
| `Scripts/Mobiles/PlayerMobile.cs` | 2 | Feeds the navigation mesh |
| `Scripts/Misc/Notoriety.cs` | ~11 | Rival NPC guilds are enemies, not criminals |
| `Scripts/Regions/GuardedRegion.cs` | ~7 | Guards ignore NPC guild members |
| `Scripts/VendorInfo/GenericSell.cs` | 1 | Vendor markup as a gold sink |

Verified with `git grep` across the whole tree: no other core file references
LivingWorld. `BaseCreature.cs`, `BaseAI.cs`, and `Server/Mobile.cs` are untouched.

---

## 1. `Scripts/Mobiles/PlayerMobile.cs` — navigation

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

Skip this one and the rest of LivingWorld still compiles and runs, but the in-town
mesh never learns anything new — NPCs stay limited to whatever is already in
`livingworld_recorded_paths.txt`, or to nothing at all on a fresh install.

## 2. `Scripts/Misc/Notoriety.cs` — rival guilds are enemies

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

**Why:** it flags rival guild members orange to each other so their fights are legal
war. Note the pet resolution — a summoned creature inherits its `ControlMaster`'s
guild, so a mage's pet fighting for them counts as the mage.

Skip it and rival guilds read as innocent to one another; the guild rivalry in
`LWGuild` becomes cosmetic.

## 3. `Scripts/Regions/GuardedRegion.cs` — guards stand down

In `CheckVendorAccess`/`IsGuardCandidate` (the method returning whether `m` is a guard
target), after the existing early-out block:

```csharp
// NPC guild members (and their pets) are NEVER guard targets: guild fights are
// "war", not crime. Even if an area spell splashes a civilian and turns them
// gray, the guard should not kill them.
if (m is Server.Custom.LivingWorld.LWCitizen lw && lw.GuildId >= 0)
    return false;
if (m is BaseCreature pbc && pbc.ControlMaster is Server.Custom.LivingWorld.LWCitizen pcm && pcm.GuildId >= 0)
    return false;
```

**Why:** it is the companion to patch 2. Even with the notoriety fix, an area spell
splashing a bystander turns the caster gray and the town guards execute them. This
keeps guards out of NPC business entirely.

## 4. `Scripts/VendorInfo/GenericSell.cs` — vendor markup

```csharp
// LivingWorld: vendor markup = player-facing gold sink.
// Affects ONLY players buying from the vendor gump; bots buy straight from
// BuyPack and never pass through here.
// Tune VendorMarkup to calibrate the drain (1.00 = no fee, 1.12 = +12%).
public const double VendorMarkup = 1.12;

public int GetBuyPriceFor(Item item, BaseVendor vendor)
{
    return (int)(1.90 * VendorMarkup * GetSellPriceFor(item, vendor));
}
```

**Why:** an economy where NPCs continuously produce goods needs somewhere for gold to
leave. This is the simplest sink, and it is player-facing only.

**This one is optional.** Set `VendorMarkup = 1.00` — or skip the patch — if you do
not want the fee. Nothing else in LivingWorld depends on it.
