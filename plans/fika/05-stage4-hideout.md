# Stage 4: the hideout for a group

Goal: the group walks into one hideout together: the raid owner's. Everyone sees the same
stations and power, can use the bed, the stash and the workbenches, and time only jumps when the
group agrees.

Depends on: stages 2 and 3. Needs decisions 1, 2 and 3 from the overview.

## Whose hideout

- The hideout behind every hideout door in the raid is the raid owner's: their station levels,
  generator and fuel, crafts under way, shelves. The server routes for the hideout resolve the
  caller to the owner (the mapping from stage 1).
- A guest's own hideout is untouched while they are a guest and is theirs again when they play
  alone or host.
- The client of the owner is not special in the raid: everyone asks the server, the server
  answers with the owner's state.

## The interior on every machine

- Each machine loads the interior at the same place under the map (from the host's list, stage 2)
  and shows the stations at the owner's levels.
- It is no longer parked when the local player leaves if anyone else is inside; simplest rule:
  in a Fika raid the interior stays loaded and shown for the whole raid, on every machine,
  the headless included (floor and walls for physics). Measure the frame cost; if it is too
  high, show it only while any player is inside (the host knows who is).
- Switching the map's sun off and the hideout's level settings on is local to players inside.
- Station changes (build, power on or off, lamp tier) are announced by the host to everyone after
  the server accepted them, so the look updates on all machines.

## Stash

- **Decided: one shared stash, the raid owner's** (the author's preference, and stage 0 showed it
  is also the simpler one technically). Everyone in the group sees and uses the same stash; what
  is in it belongs to the owner's profile when the raid ends. The docs must say this plainly:
  what a guest puts in the owner's stash stays with the owner.
- Under a host that owns inventory, the stash is a container the host must know. Pattern from
  spike S3; expected shape: when a player opens their stash the client sends its content to the
  host, the host creates it as that player's container, transfers are ordinary operations, and on
  close the owner's client saves the result to the server as today.
- It is created once, for everyone, when the hideout is first entered in the raid, and saved to
  the owner's profile whenever anyone closes it and at raid end.
- Shelves (weapon stand, place of fame) are the owner's and visible to all; only the owner
  changes them (proposed).

## Building, crafting, power

- Proposed (decision 2): only the owner builds stations and pays for them. Guests can use them.
- Crafting: anyone may start a craft with their own materials at the owner's stations; the craft
  is remembered with who started it; only that player (or the owner) collects it. Products follow
  the item rule from stage 3.
- Generator and fuel: anyone may switch it and add fuel. It is the owner's store.
- Gym, scav case, cultist circle, water and air filters: per the owner's store; effects on a
  player's own body (gym) are local to that player.
- Gunsmith and range: local to the player using them; weapon changes are inventory operations.

## Boosts and the bed

- Healing and feeding while inside is applied per player on their own machine (each owns their
  health). Unchanged.
- **Sleep** (proposed, decision 3): offered when every living player is in the hideout. One asks;
  the others get a yes/no prompt for a short time. When all agree the host jumps the clock, every
  player gets the sleep effects, and every player's game is saved at this bed (stage 7 defines
  the save). If anyone refuses or is outside, nothing happens.
- **Wait until morning** at a trader: the same agreement, among living players; those not in a
  room or the hideout must also agree, since their world jumps too.
- Alone: unchanged, no prompt.

## Freeze rule

- Already in stage 2: bots stop only while every living human is inside. Being in the hideout
  no longer protects a player whose teammate is outside. The guide says so (stage 8).

## Headless

- Loads the interior (physics only), runs the host side: clock jumps, announcing station changes,
  creating stash containers and crafted items, tracking who is inside.

## Feature gate after this stage

On: hideout, stash, crafting, power, sleep and wait by agreement. Still off in a group: save and
go to menu, trader save, death handling of this mod (Fika's stock death applies).

**This is the first releasable co-op version.**

## Checks

- Host and client enter by the map's hideout door, at the same time and one after the other, and
  meet inside; one leaves while the other stays, and the one inside keeps a floor under them.
- Client is a guest: sees the owner's station levels; own stash content; cannot build.
- Client moves a rifle from stash to pack and back; host log clean; after the raid both profiles
  hold what they should, nothing doubled or lost.
- Craft started by a guest, collected by the guest after a clock jump.
- Sleep: both agree, clock jumps on all machines; one refuses, nothing happens.
- One inside, one outside: bots move; outside player is attacked while the other is in the stash.
- All with a headless host; and alone without Fika unchanged.

## Risks

- The stash under host-owned inventory is the hard part; if S3 shows it is not workable, the
  fallback is a stash that is only usable when the player is alone inside... which is poor. A
  second fallback: stash transfers done by this mod's server and applied as host-created items.
- The interior shown all raid costs memory and frames on every machine.
