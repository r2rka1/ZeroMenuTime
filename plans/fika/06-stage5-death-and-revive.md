# Stage 5: death, spectating and revival

Goal: rules 1, 3 and 4 of the overview. A player who dies leaves a body with their gear,
spectates the others, and can be brought back at a trader for 100,000 roubles or at the hideout's
medical station for free, appearing at the spawn location with nothing. The raid ends only when
the whole group is dead.

Depends on: stages 2 and 4, and spike S1. Needs decisions 4, 5, 7 and 8 from the overview.

## Why not a true death

In Fika a true death turns the player into a synced corpse and that profile is never spawned
again in the raid. Bringing someone back would mean making a new player object, network id,
inventory ids and health on every machine, which Fika has no path for. So the player never truly
dies. This is how Fika's own downed system works and how the one existing respawn mod does it.

## The dead state

When a kill would land on a human player in a group raid, on that player's machine:

1. The kill is stopped (prefix on the client health controller's kill, as in S1).
2. **Body**: all gear except what the death setting protects in every mode (the secure container
   and its content, pockets' special slots, armband, melee as the game treats them) is moved into
   a body container the host creates at that spot. It looks like a body bag or a fallen pack;
   the detailed plan picks the model. It is ordinary loot: teammates can carry gear back, and so
   can bots that loot, if such a mod is installed.
3. **The player becomes a ghost**: not drawn, no collisions, cannot be hit or targeted, cannot act,
   takes no damage, hunger and thirst stop. Bots drop them as a target. Other machines hide the
   observed copy. This state is announced to everyone and kept by the host for late joiners.
4. **Spectating**: the camera moves to Fika's spectate view on living teammates. Check whether
   Fika's spectate can be entered for a player it considers alive; if not, this mod drives its own
   follow camera on a teammate with next/previous keys.
5. A notice to the group: who died and where; a marker on the map if Dynamic Maps is installed.

Fika's own revive option must be off; if the host has it on, this mod's death handling refuses to
start and says why.

Alone (no other human in the raid), nothing changes: death is the game's death and this mod's
existing save and death modes apply.

## Revival

- **At a trader**: the trader's menu gets one entry per dead teammate, "Revive NAME (100,000)".
  The money is taken from the reviver's carried roubles by inventory operations (decision 4).
  Not enough money: the entry is greyed out with the reason.
- **At the hideout**: the medical station's use box gets "Revive NAME", free. Requires the
  station to be built in the owner's hideout.
- On revival the host tells the dead player's machine; it teleports the player to the **spawn
  location** (decision 5; proposed: where the group entered this map, which the host recorded at
  raid start), restores health to a modest value (proposed: everything at about a third, no
  bleeds or fractures, some energy and water), ends the ghost state, and gives a few seconds of
  protection.
- They have nothing but what the death setting never takes. The body with the gear stays where
  it fell (rule 4, decision 7).
- A dead player cannot revive themselves and cannot ask through the UI; teammates decide.

## The whole group dead

- The host watches the set of living humans. When it is empty, the raid ends for everyone
  (decision 8). Each player is then really killed locally so the game and Fika run their normal
  end of raid with status "killed", and this mod's existing per-player death handling applies
  (save put back according to each player's death mode).
- Next start: at the owner's bed or trader save, as today for a solo death.
- On a headless host the raid ends and the headless goes back to waiting.

## A dead player who leaves

- A ghost can still choose to leave the raid (Fika's extract key while spectating). That is a
  death for them: handled as a solo death is today. Their body stays in the raid until it ends.
- If they come back later, that is stage 6 and 7.

## Bodies and the map

- A body lasts until the raid on that map ends. On a transit it is left behind (see stage 7 for
  what the death setting says about the gear in it).
- Insurance: the gear in a body the player never fetched counts as lost at raid end. Check how
  Fika's raid-end insurance handling sees items that were moved into a world container.

## Headless

- Runs the host side: creates bodies, keeps the ghost set, decides the group wipe, performs
  revivals' item removals. No camera work.

## Checks

- Client dies from: body shot, headshot, grenade, fall, bleed-out, starvation. Each time: body
  with gear appears for all, client spectates, bots ignore the ghost, raid goes on.
- Host player dies while a client lives: same, and the raid goes on (this is the case Fika's
  stock flow handles worst).
- Revive at a trader with exactly 100,000: money gone, teammate stands at the spawn location with
  no gear, can walk back and loot their body.
- Revive at the medical station; not offered when the station is not built.
- Both die: raid ends for both, each restored by their own death mode.
- Die, get revived, die again: second body, first body still there.
- Same set with a headless host.
- Alone without Fika, and alone as a Fika host with no peers: stock behaviour.

## Risks

- The ghost must be airtight: any path that still kills, or any system that still sees the
  player, breaks the model. S1 lists the causes; keep that list as regression tests.
- Fika treats the ghost as alive: things that count living players (transit checks, extraction,
  Fika's own UI) will count ghosts. Useful for stage 7, wrong elsewhere; audit each.
- Depends on a patch in the same place Fika patches for its downed system.

---

## Result (2026-11-20, branch `fika/stage5`)

**Built** (`ZeroMenuTime/Net/Afterlife.cs`)

- A kill of the local player in a co-op raid is looked at first. If another player is alive, the
  kill is stopped and the player becomes a ghost: healed, damage off, hands emptied, everything
  they carry dropped where they fell (one thing at a time, as the game's own drop, so every
  machine has the same pile), then moved to a shut waiting room built under the map at raid start
  on every machine. The secure container, armband, knife and dog tag are kept.
- Every machine keeps the list of ghosts (`zmt.ghost`); a machine coming into the raid asks for it.
  Ghosts are not counted as living for the freeze rule or the rest vote.
- A ghost's view is put behind a living teammate just before each frame is drawn (left and right
  arrow change who), with a line across the top of the screen.
- **Revival**: a trader's menu lists "Revive NAME (100,000 roubles)" for each ghost; the reviver's
  roubles are taken. The hideout's medical station (built) lists "REVIVE NAME", free. The ghost's
  own machine moves the player to where they came into the map, at about a third of health, hungry
  and thirsty, with nothing but what was kept; it checks the move landed and repeats it if not.
- **Group wipe**: when a kill lands on the last living player it is let through, every ghost is
  killed for real, and each machine ends its raid as a death (clients first, the hosting player
  once they have gone). The same happens if the last living player leaves or loses the
  connection. Each player's own death setting then applies as it does alone.
- Alone, nothing changes: the patch lets every kill through.

**Seen (host + client, Customs)**

- Client killed: the raid went on; its 6 pieces of gear lay at the spot on the host as well;
  the host saw the client alive in the waiting room and listed it as revivable.
- Host paid: its roubles went from 180,000 to 80,000 on both machines; the client stood at its
  entry point with 154 of 440 health, 30 energy, 30 water, and was a living player again everywhere.
- Client a ghost, host killed: "the raid is lost for everyone"; both raids ended as Killed and
  both games went back to the menu.

**Two things found and fixed while testing**

- Dropping the gear and moving the player in the same moment left the gear in the waiting room.
  The ghost now stays where it fell until every drop has finished.
- The weapon in the hands could not be dropped on the other machine ("hands controller can't
  perform this operation"). The hands are emptied first.

**Deviation from the plan:** the gear is not put into one body container; it is dropped as the
game drops things, a pile at the spot. It needed no new kind of object and every machine agrees
on it.

**Not tested**

- The medical station entry (it calls the same revival as the trader's, without the payment).
- What the spectating view looks like (it was not looked at by eye), and sound while a ghost.
- Whether bots stop shooting at once and forget the ghost (damage is off, so no harm either way).
- Deaths by bleeding, starvation, grenades and headshots (all end in the same call).
- A headless host; three or more players; a ghost choosing to leave the raid.
- Fika's own "revive" option switched on at the same time. It must be off; the mod does not
  check it yet.
