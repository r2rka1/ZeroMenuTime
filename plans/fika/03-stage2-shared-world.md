# Stage 2: one shared world

Goal: every player in the raid sees the same clock, the same trader rooms and doors at the same
places, and can walk in and out of them independently. The raid no longer stands still for one
player's sake.

Depends on: stage 1.

## World clock

- The clock belongs to the raid owner's profile. The host reads it from the server at raid start
  and sets the raid's date and time; Fika already pushes the host's time to clients, so clients
  stop setting it themselves.
- Only the host reports the clock back to the server (the 60 second tick), to the owner's store.
- On a headless the host half of the clock runs without a local player.
- "Wait until morning" and sleep are requests to the host (see the agreement rule in stage 4);
  until stage 4 they are refused in a group of more than one living player.
- Each member's own stored clock is left alone while they are a guest, and on their next solo
  raid it is moved forward to the owner's value if that is later, never back.

## Rooms and doors

- At raid start the host builds the list: for each trader spot, the trader, the door position and
  facing, the room's position under the map, which room model is used; and the hideout doors.
  The list is sent to every client (and to late joiners, stage 6). Clients build from the list,
  not from their own `traders.json`, `doors.json` or `hideouts.json`.
- The headless builds the rooms too: bots and physics on the host need the floors and walls, and
  items on tables are spawned by the host (stage 3). It skips figures, lights and sound.
- Room furniture that is copied out of the hideout interior needs the interior loaded on every
  machine; keep that as it is, per machine, but never parked while anyone is inside (stage 4).
- Going through a door stays a local teleport of the local player behind a local fade. Fika
  replicates the position. Check that a teleport of 300 to 600 metres does not trip Fika's
  position smoothing or anti-desync for the watching players.
- The widened physics region is applied on every machine, host first.
- Trader figures and voice lines are local presentation; no sync. A figure may speak for each
  player separately.

## The freeze rule

- Today: bots are switched off while the player is in the hideout. New rule, decided on the host:
  **bots are frozen only while every living human is inside the hideout or a trader's room.**
- Each client tells the host when its player goes in or out (door travel already knows). The
  host keeps the set and applies or lifts the freeze. Only the host touches bots.
- Alone, the rule gives the same behaviour as today for the hideout. Decide in the detailed plan
  whether trader rooms count when alone (today they do not freeze).
- Guide and README text ("the raid above stands still") is corrected for groups in stage 8.

## Local-only parts to confirm unchanged

- Indoor light, indoor ambience, hideout sound muting, fall guard, map markers, photo mode: all
  act on the local player and local camera. Confirm each works for a client and is skipped on
  the headless.
- Hunger and thirst drain patch: applies per machine to its own player; confirm it does not touch
  the copies of other players a machine holds.

## Feature gate after this stage

On: rooms and doors visible and enterable for everyone, shared clock, freeze rule.
Still off in a group: trading, deliveries, quest hand-in at the counter, hideout, save, sleep.

## Checks

- Two players stand at the same trader door and inside the same room and see each other there.
- Clock: host, client and headless-hosted client read the same hour; after the raid the owner's
  stored clock moved, the guest's did not move backwards.
- One player in a room, one outside: bots keep moving. Both inside: bots stop; one leaves: they
  move again. With a headless host the same.
- A client with different local door files sees the host's doors.
- Alone without Fika: unchanged.

## Risks

- Room positions depend on build order today (`Rooms.Count * 40`). The list from the host must
  carry explicit positions so order no longer matters.
- Teleports across large distances under Fika's interpolation; may need a "snap" hint to observers.
