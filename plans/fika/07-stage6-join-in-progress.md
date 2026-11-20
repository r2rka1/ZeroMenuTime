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
