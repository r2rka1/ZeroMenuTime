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

## The Fika add-on exists (2026-11-20)

`ZeroMenuTime.Fika` is a second client project, referencing `Fika.Core.dll`, loaded only where
Fika is (`BepInDependency` on Fika and on the main plugin). It subscribes to Fika's
"network manager created" event and registers this mod's packets on host and client. It builds
with `dotnet build ZeroMenuTime.Fika/ZeroMenuTime.Fika.csproj -c Release` and its DLL goes next to
the main one. Not yet in `pack.ps1` or in `zmt install`.

## Spike S3: items the mod creates (2026-11-20): the pattern works

- **Fika's own "spawn item" message cannot be used**: it only exists in Fika's debug builds.
- **This mod's own packet does the job.** `WorldItemPacket { Item, Position }`: the sender lays
  the item locally and sends it; the host relays a client's packet to the other clients; every
  machine loads the item's models and calls the game's `SetupItem` with the same item. Fika's
  item serializer (`PutItem` / `GetItem`) carries the whole item with its ids.
- **Seen in a two-player raid:** an item created by the client appeared on the host with the
  same id (as Fika's own loot type on both). The host took one: it left the world on the client
  too. The client took the other: it left the world on the host, with no refusal and no resync
  in either log. So an item created this way is ordinary loot to Fika from then on.
- **Either side can be the creator.** The host need not be the one who makes the item, as long
  as every machine makes the same one. That is simpler than the "ask the host" flow written in
  stage 3: the buyer's machine can create and announce.
- The item hangs where it was placed until something moves it (no drop). Table placement needs
  an exact resting position sent in the packet, not physics.

**Stash decision (overview decision 1):** one shared stash, the raid owner's, is also the
technically simpler one: a single container item created once for everyone when the hideout is
first entered, by the same pattern. A stash per player would be the same pattern repeated per
player. Go with the shared stash as the author prefers. Still to prove in stage 4: a container
with content (not a loose item), and saving it back to the owner's profile.

**Not tried yet:** a container with content; paying and selling as inventory operations;
crafting products; what the host does with an operation on an item only the client has
(the baseline showed such items simply do not exist for the host).

## Spike S2: joining a raid in progress (2026-11-20): works

What blocks a late joiner in Fika is two checks on the host, both reading the game controller's
"raid started" flag: the answer to the join ping ("in progress") and the connection request
("Raid already started"). A waiting client is told the flag's value on request, so one that gets
in after the start goes straight on.

The spike (`LateJoin` in the add-on) makes the flag read false while either of those two host
methods runs, when late join is switched on. Nothing on the client is changed: it joins the
ordinary way.

**Seen:** the host started a raid alone on Customs and played on. With late join on, a second
game joined through the normal join call and loaded into the running raid.
- Each saw the other, and the late joiner saw the bots already in the raid.
- An item the host had laid down before the join was there for the late joiner, who took it;
  the host accepted the operation and the item left its world.
- An item the late joiner created reached the host.
- The late joiner went through Skier's door; the host saw them in the room.
- Left over in the logs: 29 host errors "IncreaseLoadedPlayers: could not find netId N" (the
  host counting loaded players for a start that already happened), and client warnings about
  packets for characters it had not created yet during loading. No exceptions. Neither stopped
  anything; both need a look for side effects.

**So the highest risk of the plan is much smaller than assumed.** No change to Fika's join or
sync code was needed, only the two gates on the host. Fika's first-join path already brings a
newcomer the world as it is.

**Not tried yet:** where the late joiner spawns when the group has moved on (they appeared at
their own Vagabond spawn, which was the same spot here); a late joiner whose own state says a
different map; joining twice; a player who was in the raid, left, and comes back (the host
already has their character, which Fika's reconnect path is for); two joiners at once; doors,
dead bots and their bodies, containers already looted; the headless as host.

## Not done yet

- A headless-hosted raid (headless + one client), and asking the headless for a raid from this
  mod. Put off to stage 1 on purpose: the mod's raid-start code returns early with no local
  player, so the headless builds no rooms yet, and the result would only list what stage 1 fixes.
- The reference run with Fika and Vagabond only, including the dead-teammate-plus-transit case.
- The rest of S1 (spectating, bots and the ghost, body container, other causes of death) and of
  S3 (containers, payment, crafting).
- The quick checks in section C of the stage 0 plan.
