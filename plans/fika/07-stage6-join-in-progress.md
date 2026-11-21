# Stage 6: joining a raid in progress

Goal: rule 2 of the overview. A player can join a raid that is already running and spawns on
that location. This is also what "coming back to the raid" in stage 7 is built on.

Depends on: stages 1 and 5, and spike S2. This is the highest-risk stage of the plan.

## What Fika does today

- After the raid has started the host refuses every new connection ("Raid already started").
  Only a reconnect is let in, and a reconnect restores a character the host already holds:
  profile, health, position.
- A reconnecting player is sent: changed doors and other interactive objects, lamp states, broken
  windows, smoke, every character with alive flag, health and hands, the special stashes, quest
  sync, and refetches the world loot from the host.
- There is no setting for late join. Fika's server knows a match is in progress.

## Stage 0 result

The approach below was tried and works: opening the host's two checks is enough, the newcomer
joins by the ordinary path and receives the world as it is (players, bots, loot, items this mod
made). See `spike-findings.md`. Step 2 needs no work of its own. What remains is step 3 to 6,
the untested cases listed in the findings, and the leftovers in the logs.

## The approach (confirmed by S2)

1. **Let the connection in.** A patch on the host's connection check accepts a new player when
   this mod's "join in progress" setting is on (host's choice, default on in headless raids).
2. **Enter as a new character, synced like a reconnect.** The new player loads the map and goes
   through the first-join character creation, then receives the same world state a reconnect
   receives. S2 finds which of the two paths is the better base and what is missing.
3. **This mod's own state** for the newcomer, sent by the host on join: the room and door list,
   who is inside what, the clock, the ghost set and bodies, what lies on tables, the freeze state.
4. **Where they spawn** (proposed): at the spawn location of the raid, the same point revived
   players use. If that point is not safe (bots within a set distance), the nearest trader door
   of this map instead. Fully geared: joining is not dying.
5. **Server side**: the newcomer's Vagabond state says some other map. Joining a friend moves
   them: their Vagabond map and exit are set to the raid's map at join, so their own next solo
   start is consistent. Fika's server must accept a new player into the match (its join route and
   player list); check what it needs.
6. **Finding the raid**: Fika's server lists raids in progress. The menu's join entry (stage 7)
   shows them; if S4 says to leave matchmaking to Fika's screens, the in-progress raids have to be
   made joinable there instead.

## What a late joiner must not break

- Bots: the host spawns them; the newcomer only receives them. Check spawn waves are not re-run.
- Loot: refetched from the host as on reconnect. Items this mod created were created on the host,
  so they are in that set (stages 3 and 4 guarantee this).
- The freeze rule: a newcomer outside lifts the freeze at the moment they exist.
- Quests: per player; Fika's shared progression has its own sync on reconnect, check it for a new player.
- The raid's time limit: lifted already.

## Fallback if S2 fails

Joining mid-raid stays impossible, and joining happens only where a new raid instance starts:
- at a transit: a waiting player is taken into the group as the new map loads;
- when the group leaves and comes back from a trader save (stage 7).
The waiting player sees "joins at the next transit". Rule 2 would then be only partly met, and
the overview must say so.

## Headless and player hosts

- Headless: the natural case; the world keeps running and players come and go.
- Player host: allowed too; the host's machine does the same work.

## Checks

- Host and one client in raid for ten minutes (doors opened, loot taken, bots killed, a body on
  the ground, goods on a table, one player in the hideout). A second client joins: sees all of
  that as the others do, can open the same doors, loot the same body, trade, enter the hideout.
- The newcomer's own inventory operations are accepted by the host.
- Join while the others are frozen-in (all inside): the freeze lifts.
- Join, leave, join again.
- Two players join within the same few seconds.
- After the raid, the newcomer's profile saved correctly and their next solo start is on this map.
- Same with a headless host.

## Risks

- The patch reaches into Fika's connection and join code, the part most likely to change between
  Fika versions (a 5.0 branch exists). Keep it in one file of the add-on, with a version check
  that turns the feature off with a notice on an unknown Fika version.
- State that Fika only syncs at raid start and nobody thought to resend (airdrops, mines, map
  events, the armoured vehicle, weather). S2 must list these; some may be accepted as gaps.
- If Fika adds its own join in progress later, this stage is replaced by using theirs.

---

## Result (2026-11-20, branch `fika/stage6`)

**Built**

- **Setting** "Let players join a raid in progress" (on by default), read on the host. It opens
  the two checks found in stage 0 while a join is being answered.
- **Setting** "Start a co-op raid without waiting" (on by default): a raid this game hosts, or asks
  a headless host for, starts as soon as this player's character is in it. There is no waiting
  room to sit in; friends come in as it runs.
- **The server lists the raids** (`/zeromenutime/coop/raids`): who hosts, whose world, which map,
  how many players, headless or not. **Joining** (`/zeromenutime/coop/join`) first moves the
  joiner, in Vagabond's terms, to where that world's owner is (same map, same way in), so the
  joiner comes in beside the others and their own next start is from there. The client then
  starts the raid on the owner's map with the join in front.
- **Main menu, with Fika installed:** "Join a friend" (offers each running raid in turn: name,
  map, players) and "Continue on headless" (your own world on a free headless host). Continue
  itself hosts your world on your own game.
- **What a newcomer asks for** when its raid starts: the host's room and door settings, the
  clock, the stash if one exists, the list of ghosts. (All built in the earlier stages.)
- **Leaving and coming back.** "Save and go to menu" now also tells the others, the way an
  extraction does, so each machine lets the character go; a moment later each machine also
  forgets it in the raid's own record of everyone who was ever there. Coming back is then a fresh
  arrival. (Fika's "reconnect" was tried for this and does not work for someone who left
  properly: the join is accepted and the raid never starts. It is kept only for a player Fika
  itself still lists, whose game died.)
- On the host, `Humans()` counts a character only while its player's game is connected.

**Seen (host + client, Customs)**

- The host's raid started by itself. The client found it in the list, joined it as it ran, took
  the host's clock, built its rooms from the host's settings (`PASS Layout: 2 of 2`) and stood
  beside the host.
- The client saved and left at Skier; the host noted "the raid no longer remembers their
  character"; the client joined again and was in the raid, and an item it laid down reached the host.

**Known problems**

- After coming back, the client logged two "could not find item" errors for things another
  character did while it was loading. No effect seen; to watch.
- (Fixed after the run, not re-run:) the host left the returning player out of its count of
  humans, because Fika gives the same network id to a player who comes back and the old id was
  on its "extracted" list.

**Not tested**

- A joiner whose own Vagabond state was on another map (the server call that moves them was
  made; both test profiles were on Customs already).
- Joining a headless-hosted raid as the second player; two joiners at once.
- The two menu entries by hand (the harness calls what they call).
- A raid long under way: opened doors, looted containers, dead bots and their bodies.
- A player whose game died coming back by Fika's reconnect.

**Test campaign, 2026-11-21.** Now run: joining from the menu; a late joiner sees doors changed earlier, bodies and laid items; leaving and coming back on a headless host; a game closed without leaving comes back. Found and fixed: the host did not tell a newcomer about changed doors, lamps and windows; the late-join patch stood in the way of Fika's own reconnect, so a crashed player was refused; with three machines every message to all arrived twice. See `TESTING.md`.
