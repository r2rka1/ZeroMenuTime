# Stage 8: headless hardening and release

Goal: the headless host is a first-class way to run the world, the whole thing is tested as a
matrix, and the documentation and the Forge listing say exactly what works.

Depends on: every earlier stage. Parts of it (the matrix, the docs for the co-op beta) are also
run after stage 4 for the first release.

## Headless

- **Install guide**: what goes on the headless (Fika headless plugin, Vagabond's client plugin,
  this mod's plugin and its Fika add-on, the trader rooms bundle), and what must not (mods Fika's
  headless blocks, such as Dynamic Maps).
- **What runs there**: only host duties: the room and door list, physics for rooms and the
  hideout interior, the clock, the freeze rule, creating items, bodies and stash containers, the
  ghost set, group wipe, moving ghosts through transits, answering late joiners.
- **What never runs there**: menus, guide, backdrop, fades, cameras, lights, sound, figures and
  voices, map markers, photo mode, the test harness's screen tools.
- **Memory and load**: the headless loads every trader room and the hideout interior for the
  whole raid. Measure memory and frame time on the largest map; decide whether models and
  textures can be skipped there and only colliders kept.
- **Long raids**: a headless world may run for many hours with players coming and going. Soak
  test: six hours, players joining and leaving, several transits. Watch memory, bot counts, the
  clock, and the server's stores.
- **Nobody connected**: Fika's headless ends the raid when the last player leaves. Decide whether
  that is right for this mod (proposed: yes; the world's state is in the saves and the owner's
  store, and the next start makes a fresh raid at the saved place).
- **Raid owner on a headless**: the player who asked for the raid. If that player leaves while
  others stay, the world is still theirs (clock and hideout) until the raid ends. Say so in the docs.

## Test matrix

Every row is run on: alone without Fika; alone with Fika installed; player host + 1 client;
headless + 2 clients.

| Area | Cases |
|---|---|
| Start | Continue, Join, join in progress, version mismatch |
| Rooms | all nine trader doors and three hideout doors entered and left by host and client |
| Trading | buy, sell, deliveries, quest hand-in with rewards, repair, two players at once |
| Hideout | enter as owner and as guest, stash, build, craft, power, shelves, sleep agree and refuse |
| Clock and freeze | same hour everywhere, freeze only when all inside |
| Death | each cause; host dies; client dies; revive at trader and at medical station; group wipe |
| Leaving | save and menu on headless; on player host; crash of a client; crash of a player host |
| Transit | all alive; one dead; one away; each death setting |
| Saves | bed, trader, death restore per mode, new game, profile sizes after long play |
| Other mods | Vagabond 0.8.3, and the bot, looting and quest mods on the test machine that mention Fika |

The dev kit runs the scripted part of this (`zmt matrix`), with one harness per game copy; the
rest is a written checklist.

## Fika versions

- State the supported Fika client, server and headless versions in the README and check them at
  start; an unknown version turns the co-op features off with a clear notice instead of failing
  in the raid.
- Keep a list of every patch that reaches into Fika's own classes (connection check, kill
  interception, transit, spectate), each with the Fika version it was checked against.

## Documentation and listing

- README and the Forge description: a "Playing with friends" section: what Fika and headless
  setups are supported, the rules of section 5 of the overview in plain words, what changes
  compared with solo play (the raid no longer stands still, whose hideout it is, sleep by
  agreement, what happens when the host leaves).
- The in-game guide: a page for groups, shown only with Fika installed; the "raid stands still"
  line corrected.
- Fika server settings a group should use (this mod as a required client mod; Fika's own revive
  option off).
- Forge: Fika compatibility set to Compatible on the version that ships it; headless stated.
- Public texts keep the wording rules already in force for this mod.

## Release plan

1. **Co-op beta** after stage 4: rooms, trading, hideout, clock, with Fika's stock death. Marked
   as beta; single-player behaviour unchanged.
2. **Revival** after stage 5.
3. **Persistent group world** after stages 6 and 7: join in progress, save and exit, transits
   with dead teammates.
Each release: profile backup advice in the notes, since saves gain new files.

## Done when

- The matrix passes on all four setups, with known gaps listed in the README.
- A six-hour headless soak run ends with sane memory and correct saves.
- The docs describe every rule a group will meet, and the listing says Compatible.
