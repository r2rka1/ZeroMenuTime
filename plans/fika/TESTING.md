# Co-op build: how it is tested, what passed, what is left for people

Branch `fika/stage8`, version 2.0.0. Everything below was run by scripts on one machine with
two game copies and a headless. None of it has been played by people. Back up the profiles
before playing it.

## Where things are

- Test set: `F:\SPT-Fika\host`, `client`, `headless`; its server is in `host\SPT_Runtime` on
  port 6970. Lowest video settings, 1280x720 windows. Profiles: Test (host), Archee (client),
  one headless profile.
- The single-player test game `F:\SPT` has the 2.0.0 build too (no Fika there, so no add-on).
- Zips: `dist\ZeroMenuTime-2.0.0.zip` (main) and `dist\ZeroMenuTime-Fika-2.0.0.zip` (add-on,
  goes into the same plugin folder, only where Fika is).

## The dev kit for co-op

| File | What it does |
|---|---|
| `devkit/zmt --on host\|client\|headless CMD` | every dev kit command against one copy of the test set |
| `devkit/coop-raid.sh [hostonly] [MAP EXIT]` | server up, host hosts, client joins the raid in progress |
| `devkit/coop-lib.sh` | helpers to source: `h`/`c`/`d` (harness on host, client, headless), `both`, `headless_raid`, `trio`, `check`, `checknot`, `note`, `errors`, `stopall`, `protect` |
| `devkit/coop-suite.sh [GROUP ...]` | the scripted checks, in groups; each group starts fresh games |

Run everything: `devkit/coop-suite.sh` (about an hour). One group: `devkit/coop-suite.sh death`.
Results are appended to `temp/coop-results.txt` as `PASS` / `FAIL` / `NOTE` lines, with a summary
at the end. The single-player group (`solo`) runs on `F:\SPT` and is not part of "all": it backs
the profile up and puts it back.

Groups: `trade`, `hideout`, `death`, `returns`, `late`, `transit`, `mismatch`, `menu`,
`headless`, `solo`.

Harness commands added for co-op (in a raid unless noted):

- `fika` (role), `fika humans`, `fika raids`, `fika layout`, `fika bots [R]`, `fika bodies`,
  `fika loot [R]`, `fika door [open]`, `fika doorstate ID`, `fika transits`, `fika secure`
- `fika spawn TEMPLATE`, `fika take ITEMID`, `fika wear WORD POINTS`, `fika hurt PART AMOUNT [KIND]`,
  `fika killbot`, `fika set death keep|gear|everything`
- `fika dead`, `fika revive paid|free`, `fika arrive`, `fika transit ID [stand]`, `fika endforall EXIT`
- `prompt [yes|no]` (the question on screen), `menupress WORDS` (a menu entry by its text)
- `fika shot NAME` (a picture from that copy)
- In the menu: `fika host`, `fika join [ID|headless]`
- `trade open|close|list|buy WORD [N]|sell WORD`, `stash put|take TEMPLATE` (network moves in co-op)

## Last run of the suite (2026-11-21, final build of this branch)

| Group | Checks | Result |
|---|---|---|
| trade | 6 | all pass |
| hideout | 12 | all pass |
| death | 21 | all pass |
| returns | 6 | all pass |
| late | 6 | all pass |
| transit | 6 | all pass |
| mismatch | 2 | all pass |
| menu | 2 | all pass |
| headless | 12 | all pass |
| solo (main copy, no Fika) | 7 | all pass |

The nine co-op groups were run in one go (72 of 73; the one failure was the test player left
open to a bot's bullet, fixed in the script and the group run again: 21 of 21). The logs of each
group's last run are kept in `temp/suite-logs/`.

When a check fails, read that group's logs before believing it: of the failures met while the
suite was being written, about half were the script's own (a leftover in a test profile, a check
made too soon, a bot shooting a test player who had been left hurtable).

## What the scripts have shown (2026-11-20 and 21)

**Start and joining.** Host starts from Continue without Fika's waiting room. "Join a friend"
names the host and map and brings the player in. "Continue on headless" asks the headless, joins
and starts. A second player joins a headless raid in progress. Esc in a co-op raid shows only
Back to the game, Settings, Guide, Exit.

**One world.** A client with its own, different doors file builds the host's rooms and matches
them. Clock the same on all machines; guest's own clock catches up afterwards. The raid stands
still only with every living player inside.

**Late joiner.** Sees doors changed before it came, bodies of bots killed before it came, items
laid before it came, both players, the host's clock and rooms.

**Trading.** Two players buying from one trader in the same moment; selling (item gone for the
host, payment laid in the room for both); repair with a kit.

**Hideout.** Owner builds, guest sees it and cannot build. Power switched on by the owner is
noticed by the guest. A guest's craft is known to the owner; the collected thing is laid for
everyone. Shared stash in and out by both, and kept in the owner's profile. Medical station
REVIVE button. Sleep and wait: asked, refused, not answered, agreed; a group sleep saves both.

**Death.** Headshot, grenade, exhaustion, fall, dehydration all end in the ghost state. Gear lies
where the player fell on every machine. The ghost's screen follows a living teammate (picture
looked at). No bot is after a ghost. Free and paid revival; the revived stand where the group
came in. The host can be the ghost. The last living player dying or leaving ends it for all.
Dying inside the hideout and being revived.

**Leaving and returning.** Save and leave at a trader, raid goes on, come back into the same raid
(player host and headless). A game closed without leaving (crash, Alt+F4) comes back. A ghost
whose game closed comes back still a ghost and can be revived.

**Transits.** Two players together (player host and headless host). With a dead teammate: they
arrive alive. Each death setting on arrival: carried gear lost; everything lost (secure container
emptied); keep items (gear of the last save laid in front, on both machines). Two transits in a
row with a dead teammate.

**Headless.** Three processes: headless host and two players. Owner is the asking player. Laid
item arrives once. Shared stash in and out. Ghost and revive. Freeze with both in the hideout.
The raid ends on the headless when the last player leaves.

**Version mismatch.** Both sides say so; the odd client is kept out of rooms and hideout.

**Alone, without Fika, on this build.** Rooms, hideout and its freeze, sleep with save, death
and the next raid at the save: as in 1.2.4.

## Found by the campaign and fixed

1. Transit asked from a client never reached the host (the wrong call was used); after the
   transit Fika waited for a key press that nobody would give. Both handled.
2. A ghost had to ask for the transit too, and could be asked before it had been brought there.
3. A craft collected in co-op went into the stash on one machine only: made things are now laid
   in front of the player.
4. A player who died inside the hideout came back still counted as inside.
5. The in-raid question was answered "yes" by the game by itself (its message window closes at
   once when no screen is open): the mod now draws its own question with Y / N and a countdown.
6. A late joiner saw doors as at raid start: the host now sends changed doors, lamps and windows.
7. With three machines every "to all" message from a client arrived twice (Fika's host passes
   it on already): laid items existed twice for the third machine.
8. A player whose game closed without leaving was refused on coming back (the late-join patch
   stood in the way of Fika's own reconnect).
9. A ghost whose game closed came back as a living player falling under the map.
10. On a version mismatch a trader's door still let the player in.
11. A player revived from far below the map (the waiting room, the hideout) could fall through
    ground that was not solid yet and die again at once: a revived player cannot be hurt until
    they are seen standing where they were put.
12. A guest joining a raid could wake at the hideout's bed (the owner's "wake at the bed" state
    was copied to them) and not beside the group.
13. After a transit a guest's game could load the next map still believing it was on the last
    one (its travel state on the server lagged behind the owner's): no trader there for the
    guest. The host now names the map in its layout message, and a guest's travel state is made
    to follow the owner's while the next map loads.
14. A dead teammate brought from one transit's zone straight into another's was lost by the
    host's transit (it believed them in neither), and nobody could go: they now pass through
    the waiting room in between.
15. A raid a player hosted and whose game then died stayed on the join list, and that player's
    own "Join a friend" could pick it: one's own raid is no longer listed.
16. **Found while taking the release pictures, at normal video settings:** on a client the sky
    did not follow the clock (the sky keeps a clock object of its own there): night on one
    machine, noon on the other. The sky's clock is now set too.
17. Found the same way: a dead player's view of a teammate was flat and washed white (or nearly
    black at night). The game lights the scene around where the camera stands before drawing,
    and the camera was still in the waiting room then. It is now moved late in the frame as well.
18. The harness setting "undying off" stays in the settings file: `protect` in the kit sets it
    back before every group.

The test set runs at the lowest video settings, which hid 16 and 17: pictures for the release
are worth taking at normal settings for that reason alone.

Running the suite as a whole (rather than each scenario by hand) is what found 11 to 15: each
group starts from whatever the last one left in the profiles, as real play does.

## Known and not fixed

- **A laid item picked up at once.** An item laid for everyone appears on the other machines a
  moment later (its model is loaded first). If its owner picks it up within that moment, another
  machine logs "could not find item" and stays wrong about that one item. Seen once, with the
  machine out of memory.
- **A transit asked too early.** With a dead teammate, a living player who uses the transit
  before the dead one has been brought to it is refused by Fika (it counts the dead as a living
  player who is not there yet). A notice says to use the transit again; the second time works.
- **A transit back along the chain** is not offered by Vagabond; not this mod's doing.
- **The test profiles drift**: what a group finds in the stash or on the characters depends on
  earlier runs. The suite's checks do not rely on it, but two checks are skipped with a NOTE
  when there is nothing left to build in the hideout.
- **The hideout on a headless** has the stash but no floor: a thing dropped on the hideout's
  floor is held by the players' machines, not by the headless's physics.
- **Fika's own revive option** must be off; the mod does not check it.
- The Fika version is only logged at start; an unknown version does not switch co-op off.

## Left for people (the scripts cannot judge these)

1. How it feels: the fades through doors with a friend watching, voices, the spectating view over
   minutes, sound while a ghost.
2. A long session: an hour or more, many stash moves, several transits, then what the profiles
   hold. The longest scripted raid was about 20 minutes.
3. A raid that has run for a long time before someone joins (many opened crates, many bodies).
4. More than two players and a real network. Everything here ran on one machine: no latency, no
   packet loss. Memory let only two players and a headless run at once.
5. Other mods together with Fika (bot, looting and quest mods).
6. Buying and handing in quests at length; only single trades were scripted.

## Decisions still open (see the overview)

- Keep items in a group can double gear through a transit (the pile where the player fell stays
  for the others to take, and the saved gear is given again on arrival).
- How the add-on is offered on the Forge (a second download, or an addon entry).
