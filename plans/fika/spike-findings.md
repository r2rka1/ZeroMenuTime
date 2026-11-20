# Stage 0 findings

Running notes. Facts here were seen on the test machine, unlike the overview, which is from source.

## Test bench (done 2026-11-20)

- **Layout**: `F:\SPT-Fika\host`, `client`, `headless`. Each has its own BepInEx, plugins,
  settings and logs; all three share the single-player test game's data folder through a
  directory link (`EscapeFromTarkov_Data` -> `F:\SPT\EscapeFromTarkov_Data`), so the set costs
  about 3 GB of disk, not 180. `F:\SPT` (single player, port 6969) is untouched and has no Fika.
- **Versions installed**: Fika.Core 2.4.6 (all three), Fika server 2.4.2 (host's server),
  Fika.Headless 1.5.3 (headless), Vagabond 0.8.3, this mod 1.2.4. Game build 0.16.9.40743,
  which is what Fika 2.4.6 names as compatible.
- **Server**: one server for the set, in `host\SPT_Runtime`, on `127.0.0.1:6970`. Fika overrides
  the server's own address settings with those in its `fika.jsonc`; the port has to be set there.
  One headless profile is generated (`headless.profiles.amount = 1`).
- **Profiles**: `Test` (host), `Archee` (client), one `headless_...` profile.
- **Video**: lowest settings, windowed 1280x720, 40 fps cap, on host and client.
- **Dev kit**: `zmt --on host|client|headless <command>`; each copy has `zmt.NAME.conf`.
  Processes are told apart by the path of their program, so starting or stopping one copy does
  not touch another, nor the single-player game. The harness works per copy (each plugin folder
  has its own test folder); it has to be switched on in each copy's config.
- **Headless launch**: the game program with `-batchmode -nographics --enable-console true` and
  the headless profile as token. No extra launcher is needed.

## Memory (important)

- Machine: 32 GB RAM. Fika's own guide asks for 64 GB to run a headless next to a player.
- At the main menu: host 7.5 GB, client 7.3 GB, 5.5 GB free. Headless at the menu: 2.9 GB.
- This mod's menu backdrop (the hideout shown behind the menu) is part of that; it should be
  off on the test copies and must never load on the headless.
- Expect two players in a raid to fit only on small maps (Factory, Ground Zero). Host + client +
  headless at once is doubtful; headless + one client is the realistic headless test.

## Baseline with this mod as it is (1.2.4) and Fika

1. **All three copies start and reach the menu with no exception from this mod.** Fika lists the
   plugin and does not block it; the headless's plugin check passes.
2. **Continue does not work under Fika.** The mod starts a local raid directly; Fika then tries
   to attach, finds no match ("No ServerId found, deleting CoopHandler") and the raid does not
   start. Confirms the overview: the menu flow must go through Fika (spike S4, stage 7), and
   until then Continue has to hand over to Fika's own screens.
3. **The headless runs this mod's menu code**: it builds the main menu, opens the guide, loads
   the hideout backdrop and asks the server for the hideout and quests as if it were a player.
   None of that should run there (stage 1).
4. One patch does not apply in these copies (`UiFixesInRaidPatch`): UI Fixes is not installed
   there. Harmless.

## First two-player raid (2026-11-20, spike branch `spike/fika-stage0`)

Host and client were put into one Fika raid on Customs from the harness, with this mod loaded.

**How a raid is started (spike S4: answered, yes).**
- Fika needs one call before the game's own raid start: on the host, its "create match"
  (`FikaBackendUtils.CreateMatch(profileId, name, raidSettings)`); on a client, its "join match"
  (`MatchMakerUIScript.JoinMatch(profileId, hostProfileId, null, false)`). Fika's `Profile` field
  has to be set first; its pre-raid screen normally does that.
- With that call in front of this mod's existing start (`method_27`), Continue starts a real Fika
  raid as host, and a second game joins it. Fika's own screens are not needed.
- The host then sits in Fika's waiting room until "start raid"; that is the game controller's
  `RaidStarted` flag. The harness sets it (`fika start`).
- So the menu of stage 7 can stay this mod's own: Continue = create match + start; Join = pick a
  friend's match + join + start. What is still unknown: asking the headless for a raid from here
  (Fika's "start headless" request), and listing friends' matches.
- Harness: `fika` (roles as Fika sees them), `fika host`, `fika join HOSTPROFILEID`, `fika start`.
  All by reflection, in `FikaSpike.cs`, not for release.

**What worked with no changes.**
- Both games reach the raid; Fika reports host and client correctly; each has its local player.
- Trader door and hideout door are placed on both machines at the same coordinates.
- Both players went through Skier's door and stood in the same room at the same place.
- No exception from this mod on either machine.

**What is broken, as predicted.**
- **Items the mod makes exist on one machine only.** The client's deliveries and a purchase lay
  on the table on the client (10 items); the host saw none. Confirms the item rule of stage 3.
- **The harness could not pick up a table item on the client** ("the game offers nothing usable").
  Not yet separated: Fika not offering an item it does not know, or the harness's aiming under Fika.
- **Waking in the hideout fails on a client.** The new-game "wake at the bed" ran on the client,
  the fall guard found nothing under the player at the hideout's place and put them back on the
  map, leaving the mod believing the player was inside. To look at in stage 1: the wake-up runs
  while Fika's waiting room still holds the raid, and the interior may be loaded but not yet solid.
- **"No hideout controller in the bunker; stations not shown"** on the client.
- Early on the host: 23 warnings "no player with id 2 for packet type HealthSync" before the
  client's character existed there (this mod's test "undying" heal starts at once). Harmless, but
  it shows health changes made before Fika has finished spawning everyone go nowhere.

**Memory in a raid, Customs, lowest settings:** host 11.0 GB, client 8.0 GB, 0.9 GB free of 32.
Customs with two players is the limit of this machine. Use Factory or Ground Zero for the spikes,
and do not run the headless next to two players.

## Spike S1: dead state and respawn (2026-11-20): works, with gaps to close

What was built: a prefix on the health controller's `Kill` for the local player. When armed, the
kill is skipped, health is restored, damage is switched off and the player is teleported to a
"limbo" spot far under the map. Reviving is a teleport back and damage switched on again.

**Seen on host and client in one raid:**
- A direct kill and a real lethal fall on the client were both stopped ("kill by Bullet stopped",
  "kill by Fall stopped"). The player stayed alive on their own machine.
- The host saw the client alive at the limbo position, with no corpse and no error: to Fika the
  ghost is just a living player who moved. Reviving showed the client back at the spawn spot on
  the host as well.
- The same worked for the host player as the one who dies, with the client still alive: the raid
  went on. This is the case Fika's stock flow does not allow (a dead host cannot leave).
- No exception on either machine.

**So the design decision D4 stands**: death as "never truly die", with the state carried by
position alone. Nothing had to be sent over the network for the basic case.

**Gaps found, to design for in stage 5:**
- **Limbo must be a real, pre-built, solid place.** The quick slab made at the moment of death
  did not always hold (the host fell through it; so did the client the second time). Build the
  limbo room at raid start with the trader rooms, on every machine.
- **A teleport out of a long free fall does not always land** (a revive from the void put the
  player at the wrong height twice; a second teleport fixed it). Never let a ghost fall, and
  check the position after a revive teleport, repeating it if needed.
- **A player teleported into mid-air hangs there until they move**, then falls. Not a problem in
  itself, but it means a "drop" is not a reliable way to test damage.
- On a client, bots appear in the same list as humans (they are "observed players" there, and
  are not marked as AI). Anything that counts living humans must use Fika's own list of human
  players, not the game's player list.

**Not tried yet:** spectating a teammate from limbo; whether bots forget the ghost at once;
the body container with the gear; death by bleeding, starvation, grenade and headshot (all go
through the same `Kill`, but only bullet and fall were run); Fika's own revive option switched
on at the same time; what the transit check makes of a ghost.

## Not done yet

- The reference run with Fika and Vagabond only (no harness there; needs Fika's own screens
  driven by hand or by mouse), including the dead-teammate-plus-transit case.
- The rest of S1 (list above), S2 (join in progress), S3 in full (host-created items,
  stash container, trade through operations).
- A headless-hosted raid (headless + one client), and asking the headless for a raid from this mod.
- The quick checks in section C of the stage 0 plan.
