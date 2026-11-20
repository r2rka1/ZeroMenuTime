# Stage 7: save and exit, coming back, transits, and the menu

Goal: rules 5 and 6 of the overview, and a main menu that works with Fika. A player can save and
leave and return; the group can move to another map; in both cases dead teammates come back on
the new raid as if they had died there, under their death setting.

Depends on: stages 5 and 6, and spike S4. Needs decisions 6 and 9 from the overview.

## Saving in a group

- A save is still per player: each player's own character snapshot, at a bed or a trader.
- **Bed (sleep)**: by agreement (stage 4); every living player is saved at that bed. For a guest
  the save records the owner's hideout door as the place, so a later death puts them back there.
- **Trader "Save"**: saves the player who chose it, at that trader. Others are not affected.
- A ghost cannot save.
- The server save path runs a "survived raid" step on the profile without ending the raid. Under
  Fika the profile is saved by each client at raid end; check the mid-raid snapshot still matches
  what the host believes the player carries (the host is the truth for inventory).

## Save and go to menu

- **On a headless host**: the player is saved at that trader and leaves the raid; the raid goes
  on for the others. Fika shows them as extracted.
- **As the player host** (decision 9; proposed): the host cannot leave while others are
  connected. Choosing it asks "this ends the raid for everyone"; on yes, every living player who
  stands at a trader or in the hideout is saved there, anyone elsewhere keeps their last trader
  or bed save, ghosts are handled as dead, and the raid ends for all.
- Auto-continue after the menu is off in a group; the player decides when to return.

## Coming back (rule 5)

- A player who left returns through join in progress (stage 6) when the raid still runs, or by
  starting the raid again when it does not.
- **When a raid is started again at a place** (the returning player becomes the host, or asks the
  headless for it), teammates who were dead when the last raid ended **can join it and spawn
  there as if they had died on that location**: at the spawn location, in the state their death
  setting gives (below). They are not ghosts in the new raid.
- "Can" means they choose to join; nobody is pulled into a raid.
- The server remembers, per group, who was dead at the end and where the group last was, so the
  right state is given on join. A group is the set of profiles that were in the match.

## Transits (rule 6)

Today: all living players must stand in the transit point; Vagabond's README says a dead
teammate plus a transit leaves the game stuck.

- Ghosts are alive to Fika, so the transit check would wait for them. At the moment the living
  players are all in the transit point, the host teleports every ghost into it as well (unseen),
  so the check passes and the whole group, ghosts included, moves in one piece.
- On arrival on the new map each ghost is **respawned as if they had died there**: at the group's
  spawn point on the new map, alive, in the state their death setting gives.
- Vagabond's per-player state (current map, last exit) is then right for everyone, because
  everyone really made the transit.
- Check Vagabond's desync guard (it compares the player's map with the raid's) stays quiet.
- A player who had left the raid before the transit is not moved; they join later (stage 6).

## Death settings at respawn

The three modes, applied in the raid at the moment of respawn after a transit or a new raid
(proposed, decision 6). The body from the old raid is gone with that raid.

| Death setting | What the respawned player has |
|---|---|
| Keep items | Everything they had at their last save: the saved character is put back |
| Lose carried gear | The saved character without the gear they carried when they died; secure container kept |
| Lose everything | As above, and the secure container emptied |

- "Put the saved character back" in a running raid means giving items through the host (the item
  rule of stage 3) and setting health, skills as the save has them. For a transit this can be done
  cleanly at the raid boundary: the profile is restored on the server between the two raids,
  exactly as a solo death does today, and the player loads into the new map with it. Prefer that.
- A revive inside the same raid (stage 5) is different on purpose: no gear, the body is there to
  be fetched, whatever the setting.
- A player with no save at all: as today, the "stay or hideout" rule does not apply in a group;
  they respawn with what the mode leaves of their current character.

## The main menu with Fika (from S4)

- **Continue**: starts the player's own world. Alone it behaves as now. With Fika it creates a
  match on the saved map, with a choice "host it myself" or "use the headless" when one is
  available. Friends can then join.
- **Join**: lists friends' raids, running or waiting, and joins one. Shown only with Fika.
- If S4 found that Fika's screens cannot be bypassed safely, Continue and Join open Fika's own
  screens with the map preselected, and this mod only removes what is not needed.
- Reset is offered only when not in a match.
- The in-raid Esc menu (Back to the game, Settings, Guide, Exit) is unchanged; Exit in a group
  raid is "leave the raid" with the consequences above spelled out.

## Checks

- Headless raid, A and B. A saves and goes to menu at Skier; B keeps playing; A joins again.
- B dies while A is away; A comes back and revives B (stage 5 still works after a rejoin).
- A and B at a transit with B dead: both arrive on the next map; B stands at the spawn point,
  alive, in the state of B's death setting; all three settings tested.
- Player-hosted: host chooses save and go to menu: both saved, raid ends, both can Continue.
- All dead, raid ended, one player starts again: the other joins and spawns there.
- Vagabond state of every player after each case matches where they are.
- Alone without Fika: Continue, save, death and transit unchanged from 1.2.4.

## Risks

- Moving ghosts through a transit depends on Fika counting them as living players; if a Fika
  update changes the check, dead teammates stall the transit again.
- Profile restore between two raids of a transit must happen before the new raid loads the
  profile, on Fika's own raid-end and raid-start handlers; order matters (see stage 1's check).
- Group memory on the server (who was dead, where) must not leak between different groups a
  player belongs to.

---

## Result (2026-11-20, branch `fika/stage7`)

**Built**

- **Saving in co-op is on.** "Save" at a trader and saving at the bed are each player's own, as
  alone. Sleeping in a group saves each player at the bed once everyone has agreed and rested.
- **Save and go to menu.** For a client, and for anyone on a headless host, it takes that player
  out and the raid goes on (stage 1 and 6). For the hosting player with others in the raid it
  now asks: "Leaving ends it for everyone". On yes, every living player leaves by the same exit
  and starts next time from there; anyone dead dies for real with their own death setting; the
  host leaves last.
- **Dead teammates and a transit** (`Regroup`). While anyone is a ghost the host looks once a
  second at the map's transits. When every living player stands in the same one, the ghosts are
  brought to that spot, unseen, so the co-op layer's "everyone alive is here" is true and the
  whole group goes through.
- **Arriving after death.** A player who was a ghost when the raid ended by a transit comes to
  life on the new map, and their own death setting is applied there:
  - *Keep items*: the server gives the gear of their last save (everything worn and carried
    except the secure container, armband, knife, dog tag and pockets); it is laid in front of
    them as new copies, to be put on.
  - *Lose carried gear*: they have what the death left them (the kept slots).
  - *Lose everything*: the same, and the secure container is emptied.
- **Rule 5** (a dead teammate can spawn where the returning player starts again) needs nothing
  of its own: when nobody alive is left in a raid the ghosts die for real (stage 5), their death
  setting is applied by the server as for any death, and "Join a friend" then brings them to
  where the new raid's owner is (stage 6).
- **Menu**: done in stage 6 (Continue hosts, Join a friend, Continue on headless).

**Seen (host + client, Customs)**

- The client saved at Skier in a co-op raid.
- "Arriving after death" with Keep items laid 4 things from that save in front of the client;
  the host saw the same 4.
- The host ended the raid for everyone: both raids ended as Survived by Skier's exit, the host last.

**Not tested**

- **Any transit at all under Fika with this mod**, with or without a dead teammate. The harness
  has no way yet to make two players use a transit together. The code that brings ghosts to the
  transit, and the flag that carries "was dead" over to the next map, have not run in a real
  transit. This is the largest untested piece of the whole plan.
- The death settings Lose carried gear and Lose everything on arrival.
- The question shown to the hosting player (the harness calls what its yes does).
- A group sleep that saves; a guest dying after saving at the owner's bed, and where they wake.
- A dead player in a raid whose last living player saves and leaves on a headless host.

**Known weak points**

- *Keep items* in a group can double gear: the things dropped at death can be carried through
  the transit by a teammate while the dead player also gets copies from their save. Alone the
  same setting never doubles anything. To decide: accept, or give the copies only when the
  originals were left behind.
- A living player who is made to leave by the host's "end for everyone" keeps their last save
  as it was; only where they start next time changes.
