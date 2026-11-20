# Co-op build: what to test by hand

Branch `fika/stage8`, version 1.3.0. Everything below was built and run by scripts on one
machine with two game copies and a headless. None of it has been played by people. Back up the
profiles first.

## Where things are

- Test set: `F:\SPT-Fika\host`, `client`, `headless`; its server is in `host\SPT_Runtime` on
  port 6970. Lowest video settings, 1280x720 windows.
- The single-player test game `F:\SPT` has the 1.3.0 build too (no Fika there, so no add-on).
- Zips: `dist\ZeroMenuTime-1.3.0.zip` (main) and `dist\ZeroMenuTime-Fika-1.3.0.zip` (add-on,
  goes into the same plugin folder, only where Fika is).
- Dev kit: `devkit/zmt --on host|client|headless ...`; `devkit/coop-raid.sh` brings host and
  client into one raid (`coop-raid.sh hostonly` for the host alone).
- Harness, in a raid: `fika` (role), `fika humans`, `fika raids`, `fika layout`, `fika dead`,
  `fika revive paid|free`, `fika answer yes|no`, `fika endforall EXIT`, `fika arrive`,
  `fika spawn TEMPLATE`, `fika take ITEMID`, `fika loot [R]`. In the menu: `fika host`,
  `fika join` (first raid on the list), `fika join headless`.

## With the menu, as a player would (not done by the scripts)

1. Host: **Continue**. The raid should start without Fika's waiting room.
2. Friend: **Join a friend**: the question names the host and the map; yes; you arrive beside them.
3. **Continue on headless** with the headless running.
4. Press Esc in a co-op raid: Back to the game, no Join or Reset there.

## The rules asked for

| Rule | How to check | Scripted? |
|---|---|---|
| Death: watch the others, raid goes on | die with a friend alive; your gear lies there; view follows them; arrows switch | state yes, the view not looked at |
| Join mid-raid | join a raid 10 minutes old: opened doors, looted crates, bodies | joined a fresh raid only |
| Revive at a trader, 100,000 | trader menu "Revive NAME"; roubles go; friend at their entry point, nothing on them | yes |
| Revive at the medical station, free | station window "REVIVE NAME" (station built) | same code, the button not pressed |
| Everyone dead: raid over | last one dies | yes |
| Save and exit, come back | on a headless: save and go to menu at a trader, then Join a friend | on a player host yes; on a headless no |
| Dead teammates when the group moves on | die, friend takes a transit: you should arrive alive, with your death setting applied | **never run** |

## Most likely to be wrong

1. **Transits in co-op.** Never run, with or without a dead player.
2. **The spectating view** (camera behind a teammate): written, never looked at.
3. **The shared stash over a longer session**: many moves by two players, then what the owner's
   profile holds after the raid. One move each way was tested.
4. **Crafting, power and building as seen by the other player.**
5. **A raid that has run for a while before someone joins.**
6. **Sleep and wait prompts**: the yes path was tested; no and no-answer were not.
7. **Bots and a ghost**: do they keep shooting at where it stood?
8. **Three players.**

## Decisions still open (see the overview)

- Keep items in a group can double gear through a transit.
- Fika's own revive option must be off; the mod does not check.
- How the add-on is offered on the Forge (a second download, or an addon entry).
