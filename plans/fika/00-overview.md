# Zero Menu Time with Fika and headless: high-level plan

Status: research done 2026-11-20, nothing built. This file is the map; each stage has its own file
in this folder. Stage files are written to be turned into detailed plans one at a time.

## 1. What was read

| Subject | Version read | Where |
|---|---|---|
| Fika client plugin | v2.4.6 (main) | github.com/project-fika/Fika-Plugin |
| Fika server (C#, for SPT 4.x) | v2.4.2 (main) | github.com/project-fika/Fika-Server-CSharp |
| Fika headless | v1.5.3 (main) | github.com/project-fika/Fika-Headless |
| Fika modding docs | "Updated for 4.0" | wiki.project-fika.com, `Modding-Fika.md` |
| Vagabond | 0.8.3 (installed copy, decompiled) | github.com/MrEliasen/SPT-Vagabond |
| This mod | 1.2.4 | this repository |

Nothing was run: Fika is not installed on the test machine. Every Fika fact below is from source,
not from a test. Stage 0 exists to turn the risky ones into tested facts before anything is built.

## 2. How Fika works, in the parts that matter here

- **One game client is the host; the rest are clients.** The host owns bots, loot and inventory
  operations (a client asks, the host performs and tells everyone). Each player owns their own health.
  The host pushes the time of day.
- **A headless host is a full game client with no rendering, no sound and no local player.** It is
  always the host. Client plugins load on it. A player asks for a headless raid from the menu and
  then joins it as a client like everyone else.
- **Other mods talk through Fika's network manager**: register a packet type, send it reliable,
  optionally broadcast. Events tell a mod when the game, the network manager and the raid start
  and end, and when a peer connects or leaves. The usual shape is a separate add-on DLL that
  depends on `com.fika.core`.
- **Nothing a mod creates is synced by itself.** An item must exist with the same id on the host
  and every client or inventory operations on it fail. Items laid in the world, custom containers
  and custom interactables each need a packet.
- **Death today**: the dead player spectates teammates; the raid goes on; they leave when they
  press the extract key. There is a built-in "downed and revive" system (off by default), which
  works by never letting the player truly die. **There is no respawn after a true death**: the
  body is synced as a corpse and that profile is never spawned again in the raid.
- **Joining a raid in progress does not exist.** Once the raid has started the host refuses every
  connection except a reconnect of a player who was already in it.
- **The host cannot leave while others are connected.** There is no host migration. A headless
  host stays until the last player leaves.
- **Map-to-map transits are supported**: all living players must stand in the same transit point;
  the network session survives; the host makes the new map's loot.
- **Server side**: `MatchService` knows each match, its host session and its players, and
  `HeadlessService` knows which player asked for a headless raid. Fika already overrides the raid
  start and raid end handlers this mod also patches.

## 3. How Vagabond behaves under Fika

- It supports Fika and headless already, by reflection, with no packets of its own.
- **The player who starts the raid decides the map, the spawn point and the loot.** For a headless
  raid that is the player who asked for it. Each player's own Vagabond state is still updated at
  their own raid end.
- Travel between maps is the game's transit system: raid end with a transit flag, then a new raid.
- Its README lists a known problem: **a dead teammate plus a transit leaves the game stuck.** The
  rules in section 5 need exactly that case to work, so this plan has to solve it (stage 7).
- The headless must have Vagabond's client plugin too.

## 4. What in this mod assumes one player

Every in-raid feature reads the one local player, keeps one static state per machine, and calls
the server as its own session. The parts that will not survive several players as they are:

| Area | Today | Problem with several players |
|---|---|---|
| World clock | Each profile has its own; pushed into the raid at start; sleep and wait jump it | Several clocks pushed into one raid; one player's sleep moves time for all |
| "The raid stands still" | All bots switched off while the player is in the hideout | One player in the hideout freezes the raid for the others |
| Trader rooms, hideout interior | Built and loaded locally, hideout parked when not in use | Must be at the same place on every machine; cannot be parked while anyone is inside |
| Items on tables | Thrown into the world locally | Not known to the host: nobody else sees them, picking up fails |
| Trading, stash, crafting, repair | Items created and moved on the local player directly | The host owns inventory operations; unknown items are refused |
| Hideout state | One hideout per profile on the server | Whose hideout does the group stand in? |
| Death | Raid ends, profile restored from the save by death mode | Raid must not end; restore cannot wait for raid end |
| Save and go to menu | Extracts the player | A player host cannot leave while others are in |
| Main menu | Continue starts a local raid directly | Bypasses Fika's matchmaking; breaks the headless, which drives menus itself |
| Raid end hooks | Patch on the single-player game class | Fika uses its own game classes |
| Headless | Not considered | No local player, no UI: most code would throw |

## 5. The rules asked for (the target behaviour)

1. A player who dies spectates the others. The raid does not stop until the whole group is dead.
2. Players can join a raid in progress and spawn on that location.
3. A dead teammate can be revived at a trader for 100,000 roubles, or at the hideout's medical
   station for free.
4. A revived player appears at the spawn location, without gear. The gear stays where they died
   and has to be fetched.
5. When a player saves and exits and comes back to the raid, dead teammates can spawn there as if
   they had died on that location.
6. When the group transits to another location, dead teammates respawn there as if they had died
   there, and the death setting applies.

## 6. Design decisions this plan takes

**D1. A separate add-on DLL.** `ZeroMenuTime.Fika.dll` depends on `com.fika.core` and holds every
Fika type. The main plugin gets a small "multiplayer" seam (roles, send, receive, list of human
players) with a do-nothing single-player implementation, so the mod without Fika stays as it is.

**D2. One world, owned by the raid owner.** The raid owner is the hosting player, or for a
headless raid the player who asked for it (the same rule Vagabond uses). The owner's profile
supplies the world clock and the hideout (stations, power, crafts). The server maps every session
in a match to its owner through Fika's `MatchService`, by reflection.

**D3. The host decides, everyone builds.** Rooms, doors and the hideout interior are built on
every machine from one list the host sends, so they sit at the same coordinates everywhere. The
host (player or headless) is the only one that creates world items, moves the clock and freezes
bots.

**D4. Death is "never truly die".** A true death cannot be undone in Fika. The mod intercepts the
kill the way Fika's own downed system and one existing respawn mod do, and puts the player in its
own dead state: gear moved to a body container at that spot, the player hidden, harmless and
spectating. Revival and respawn are then a teleport and a health reset. A side effect that helps:
to Fika the player is still alive, which is what makes transits with dead teammates workable.
Fika's own revive option must be off, or be detected and refused, while this is on.

**D5. The raid does not stand still any more.** Bots are frozen only while every living player is
inside the hideout or a trader's room. Sleep and "wait until morning" need every living player to
agree.

**D6. Headless is the recommended way to play a persistent group world.** With a player as host,
that player cannot save and exit while others play, and if they crash the raid is gone. With a
headless host any player can leave and come back. Both are supported; the limits of player
hosting are stated in the docs and in the UI.

**D7. Death settings move into the raid.** Today the death mode is applied by restoring the
profile at raid end. In a group the same three modes are applied at the moment of respawn, as
changes to what the player carries, because the raid has not ended.

## 7. The stages

| # | File | What it delivers | Depends on |
|---|---|---|---|
| 0 | `01-stage0-test-bench-and-spikes.md` | Fika, two clients and a headless running locally; four risky ideas proven or dropped | none |
| 1 | `02-stage1-foundations.md` | Add-on DLL, roles, owner mapping on the server, safe on headless, raid lifecycle through Fika | 0 |
| 2 | `03-stage2-shared-world.md` | One clock, the same rooms and doors for all, the new freeze rule | 1 |
| 3 | `04-stage3-trading-and-tables.md` | Trading, deliveries, quest rewards and repairs that the host accepts and all can see | 2 |
| 4 | `05-stage4-hideout.md` | The owner's hideout shared by the group, stash, crafting, sleep by agreement | 2, 3 |
| 5 | `06-stage5-death-and-revive.md` | Dead state, spectating, body with gear, revive at trader and medical station, group wipe | 2, 4 |
| 6 | `07-stage6-join-in-progress.md` | Joining a running raid and spawning on the location | 1, 5 |
| 7 | `08-stage7-save-exit-transit.md` | Save and exit, coming back, transits with dead teammates, death settings on respawn, menu flow | 5, 6 |
| 8 | `09-stage8-headless-and-release.md` | Headless hardening, test matrix, docs, release | all |

Stages 2 to 4 make the mod playable in co-op with Fika's stock death (die, spectate, leave).
That is a releasable first version ("co-op beta"). Stages 5 to 7 add the rules in section 5.

## 7a. What stage 0 has shown so far (see `spike-findings.md`)

- **Starting and joining from this mod's menu works** with one Fika call before the game's start.
- **The dead state works** on host and client; it needs a solid pre-built holding room.
- **Items made by this mod's own packet are ordinary loot to Fika**; either side can create them.
- **Join in progress works** by opening two checks on the host; no change to Fika's sync code.
- Memory: two players on Customs fill this 32 GB machine; test on small maps.

## 8. The biggest risks

1. **Join in progress (stage 6).** *Stage 0 result: it works with two small host-side patches;
   the risk is now the untested cases, not the approach.* As first assessed: Fika refuses it by design; the reconnect path only restores a
   player the host already knows. It needs a patch inside Fika's connection handling and a full
   world sync for a new player. If the spike fails, the fallback is: join at the next transit or
   when the group next leaves a trader save, never mid-raid.
2. **Dead state (stage 5).** It leans on the same trick as Fika's downed system. It has to hold
   against every way of dying, against bots that keep shooting, and against a Fika update.
3. **Stash and trading under host-owned inventory (stages 3, 4).** Every item the mod creates
   has to be created on the host too. This touches most of the mod's item code.
4. **Fika changes.** v2.4.7 and a 5.0 branch are in development. Everything is kept behind the
   add-on DLL and Fika's public events and packets where possible; patches into Fika's own classes
   are listed per stage so they can be re-checked on each Fika release.
5. **Two patches on the same server methods.** Fika replaces raid start and raid end handling;
   this mod and Vagabond patch them too. Order and effect are checked in stage 1.

## 9. Decisions

Answered by the author on 2026-11-20:

1. **Stash in a group**: one shared stash (the owner's), unless a stash per player turns out
   technically easier; spike S3 decides, and the result is recorded in `spike-findings.md`.
2. **Hideout**: the raid owner's. Only the owner builds and pays (proposed default, not objected to).
4. **Revive at a trader**: the reviver pays the 100,000.
5. **Spawn location for a revived player**: where the group entered the map.
6. **"Keep items" after a transit**: gear is restored from the save. The other two settings lose
   what was on the body.
9. **Player host chooses save and exit**: the raid ends for everyone.

Still open, proposed defaults stand until the author says otherwise:

3. **Sleep and wait**: every living player must agree.
7. **The body after a revive in the same raid**: gear always stays on the body whatever the
   death setting.
8. **Group wipe**: the raid ends and each player's death setting is applied as today; next start
   at the owner's bed or trader save.
10. **Fika version to target first**: 2.4.6 as released.
