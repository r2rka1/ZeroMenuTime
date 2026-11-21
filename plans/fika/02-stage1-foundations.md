# Stage 1: foundations

Goal: the mod loads next to Fika on host, client and headless without errors, knows what role it
has, can send its own messages, and its server half knows who is in a raid together. No feature
is made multiplayer yet; features that are not ready are switched off cleanly in a Fika raid.

Depends on: stage 0.

## Client: the multiplayer seam (main plugin, no Fika types)

New `ZeroMenuTime/Net/` in the main plugin:

- `Role`: `Single`, `Host`, `Client`, `HeadlessHost`.
- `IMultiplayer` with: `Role`, `IsHost` (true for `Single`, `Host`, `HeadlessHost`), `HasLocalPlayer`,
  `Humans` (everyone in the raid: profile id, name, alive, position), `Send(message, toAll)`,
  `On<T>(handler)`, and events `RaidStarted`, `RaidEnded(status, exit)`, `PeerJoined`, `PeerLeft`.
- `SinglePlayer`: the implementation used without Fika. `Humans` is the one local player; `Send`
  to all calls the handlers locally. Everything written against the seam therefore still runs
  alone.
- `Net.Current`: the one in use. The add-on replaces it when it loads.
- Messages are plain classes with a small writer/reader of this mod's own, so the main plugin
  needs no Fika reference.

## Client: the add-on `ZeroMenuTime.Fika.dll` (new project)

- `[BepInDependency("com.fika.core")]`, `[BepInDependency("com.r2rka1.zeromenutime")]`.
- Subscribes to Fika's events: network manager created and destroyed, game created, raid started,
  game ended, peer connected and disconnected; turns them into the seam's events.
- One Fika packet type that carries any of this mod's messages (type id + bytes), reliable and
  ordered, registered on host and on client. Client messages marked "to all" are relayed by the host.
- Role from Fika's flags (`IsServer`, `IsClient`, `IsHeadless`).
- `Humans` from Fika's list of human players.
- Version handshake: on connect, each client sends this mod's version; the host answers. A
  mismatch shows a clear message and switches this mod's multiplayer features off for that player.
- Shipped in the same zip, in the same plugin folder. It only loads when Fika is there.

## Client: make the main plugin safe

- **No local player**: wrap the per-frame calls in `Plugin.Update` and the raid-start patch so
  each feature states what it needs: `local player`, `host`, or `any`. On the headless only `host`
  and `any` parts run. Menu, guide, backdrop, photo, fades, sound, lights and map markers never
  run there.
- **Raid lifecycle**: move what `RaidStopPatch` does (clock end, hideout close, death flags,
  auto-continue arming) behind the seam's `RaidEnded`, so it fires under Fika's game classes too.
  The raid-start work moves behind `RaidStarted`.
- **Menu**: with Fika present, the auto-continue and the direct local raid start are off until
  stage 7 decides the menu flow; Continue shows a notice and leaves matchmaking to Fika. On the
  headless no menu patch is applied at all.
- **Feature gate**: a table of features with a "works in a Fika raid" flag. Until its stage is
  done a feature is refused with a notice ("not yet available in co-op") instead of half working.
  Starts with everything off except rooms being visible; each later stage flips its own.

## Server

- `FikaLink` (reflection only, as Vagabond's adapter does; no reference to Fika's assembly):
  - `IsInstalled`.
  - `MatchOf(sessionId)`: match id and its members.
  - `OwnerOf(sessionId)`: the hosting player's session; for a headless match the player who asked
    for it; without Fika, the session itself.
  - `IsHeadlessProfile(sessionId)`.
- Every service that is keyed by session gets an explicit choice per call: "my own" (mail, save,
  death mode, trading, quests) or "the owner's" (hideout store, world clock). Nothing changes
  behaviour yet; the choice is written down in code and used from stage 2 on.
- Headless profile: skip new-character setup, starting kit, save and death handling for it.
- Check the three patches on raid start and raid end together (Fika's, Vagabond's, this mod's):
  order, and that this mod's raid-end handling runs once per player with the right status.
- Map time limit: confirm the lifted limit also covers the co-op limit field Fika reads.

## Fika server config to document (not to change for the user)

- This mod listed under required client mods, so everyone has the same version.
- Fika's own revive option: must be off once stage 5 ships.

## Checks

- Alone, without Fika: every existing harness check still passes (nothing changed for single player).
- With Fika, host + client + headless: no errors from this mod in any log through menu, raid
  start, five minutes of play, a transit and raid end.
- A test message sent from the client arrives on the host and the other client, and the reverse.
- Server log shows the right owner for host, client, and headless-raid sessions.
- Version mismatch between two players gives the notice, not a broken raid.

## Risks

- The seam is the contract every later stage uses; getting `Humans` and the events right matters
  more than anything else here. Keep it small.
- The headless starts its own raids through the menu; any menu patch left active can hang it.

---

## Result (2026-11-20, branch `fika/stage1`)

Built and run on the Fika test set (host, client, headless) and on the single-player game.

**What exists now**

- `ZeroMenuTime/Net/Net.cs`: the seam. `Role` (Single, Host, Client, HeadlessHost), `Net.IsHost`,
  `Net.Coop`, `Net.Headless` (the process has no screen), `Net.FikaInstalled`; events
  `RaidStarted`, `RaidEnded`, `PeerJoined`, `PeerLeft`; `Net.On(kind, handler)` for messages.
  `IMultiplayer`: `Humans()`, `Send(kind, body, toAll)`, `LayItem(item, at, turned)`,
  `PrepareRaid(settings, joinHost)`, `StartWaitingRaid()`, `LeaveRaid(exit, out why)`.
  `SinglePlayer` answers when there is no co-op layer; alone the mod runs through the same seam.
- `Coop.Allowed(feature)`: the feature gate. In a co-op raid only `Rooms` is on so far; trade,
  deliveries, quest rewards at the counter, hideout, sleep, wait, save and save-and-leave are
  refused with a notice. (Repairs are not gated yet.)
- `Handshake`: a client tells the host its version at raid start; the host tells everyone its
  own; a client that differs switches the co-op features off for itself.
- `ZeroMenuTime.Fika/FikaAddon.cs`: the Fika layer. Two packets (`ModMessagePacket` for any
  message of the mod, `WorldItemPacket` for an item everyone lays), Fika's events turned into
  the seam's, match create / join / ask the headless, the lobby's start, leaving a raid.
- Server `FikaLink` (reflection): `OwnerOf(session)`, `IsHeadlessProfile(session)`,
  `Describe(session)`; route `/zeromenutime/coop/who`. Headless profiles are skipped by the
  new-character set-up, the raid-start note and the raid-end save handling.
- Dev kit: `zmt build client` builds the add-on too; `zmt install` puts it only where Fika is.
  `devkit/coop-raid.sh` brings host and client into one raid.

**Seen working**

- Alone, no Fika: role Single; rooms, hideout, save and leave as before; raid start and end
  signals once each.
- Host + client: roles right on both; `Humans()` lists both; a message from either reaches the
  other, and "to the host only" reaches only the host; version handshake arrives; hideout is
  refused by the gate while rooms can be entered.
- Leaving: a client's "save and go to menu" ends the raid for that client only (Survived by the
  trader's exit) and brings it to the menu; the hosting player is refused while others are in,
  with the reason on screen, and can leave once alone.
- **Headless**: starts with no menu, guide or backdrop code running and no errors. A raid asked
  for from the client's own Continue is created on the headless, the client joins it, any player
  can start it from the lobby, the headless reports role HeadlessHost, and the server names the
  asking player as the owner of that raid.
- A player whose game died is no longer counted in the host's `Humans()`, and the server picks
  the newest match when a stale one still lists the player.

**Changes from the plan as written**

- The menu did not have to be handed to Fika: Continue hosts a match through the seam, and
  auto-continue is simply off while Fika is installed.
- "Save and go to menu" needed a co-op path already here (`LeaveRaid`): under Fika the game's own
  extraction at a trader does not take the player out of the raid.
- The headless needs nothing in `Update`; only raid start and raid end patches stay on there.

**Left open**

- No way yet for a real player to join or to pick the headless: both are harness commands
  (`fika join HOSTID`, `fika join headless`). The lobby then needs its start pressed (Fika's own
  START RAID button is on screen). The menu entries are stage 7; a first co-op release after
  stage 4 needs at least "Join" and "Use headless" brought forward.
- On clients, `Humans()` still lists a player who has gone until Fika removes the character.
- The version mismatch path was not exercised (all copies ran the same build).
- The server's handling of each player's raid end under Fika (trader save written, Vagabond
  state, order of the three patches) was not inspected beyond "no errors".
- How the add-on is shipped: BepInEx logs an error for a plugin whose hard dependency is
  missing, so the add-on DLL should not sit in the folder of someone without Fika. Decide in
  stage 8: a second zip, or a loader that only loads it when Fika is there.
- Server messages written with `Console.WriteLine` do not reach the server's log file.
