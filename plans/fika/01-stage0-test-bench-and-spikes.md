# Stage 0: test bench and spikes

Goal: be able to run Fika with two players and a headless on the test machine, and settle the
four ideas the rest of the plan stands on before any feature work. Output of this stage is a short
findings file (`plans/fika/spike-findings.md`) and an updated overview, not shipped code.

## A. Test bench

1. **A second copy of the test game.** Fika needs one game process per player. The existing copy
   at `F:\SPT` stays the host; add a client copy and a headless copy (Fika's installer makes the
   headless one). Check disk space first; a full copy is large. The live game folder is never
   touched.
2. **Install Fika**: server mod (Fika-Server-CSharp 2.4.2) into the one SPT server; client plugin
   2.4.6 into each game copy; headless plugin 1.5.3 into the headless copy. Record exact versions.
3. **Profiles**: a second test profile for the client; the headless profile Fika creates.
4. **Mods on every copy**: Vagabond client plugin and this mod's plugin (the headless needs both).
   Remove from the headless what Fika's headless blocks (Dynamic Maps and others in its list).
5. **Dev kit**: extend `zmt` to know several game copies: `zmt game start host|client|headless`,
   `zmt cmd --on client "..."`, one results file per copy, `zmt deploy` installing to all.
   The harness already drives one game through files; give each copy its own folder.
6. **Baseline run without this mod**: host a raid with Vagabond + Fika, client joins, transit
   together, one dies and the other transits (the "stuck" case from Vagabond's README). Write
   down what actually happens; this is the reference for stage 7.
7. **Baseline run with this mod as it is**: list every error and broken feature, on host, client
   and headless. This is the starting bug list for stage 1.

## B. Spikes (each a throwaway branch, a yes/no answer, and notes)

**S1. Dead state and respawn.** Can a player be kept from truly dying and brought back?
- Prefix the client health controller's kill as Fika's downed patch does; hide the player, stop
  damage, move to spectate; later teleport, restore health, show again.
- Check on host, on client, and seen from the other machine. Check every death cause: bullet,
  headshot, grenade, fall, bleed-out, starvation.
- Check bots: do they forget the hidden player? Check that Fika's own revive option off/on does
  not fight it.
- Answer needed: works / works with listed gaps / does not work. If it does not: the fallback is
  true death plus rejoin through the stage 6 path.

**S2. Join in progress.** Can a new player enter a running raid?
- Patch the host's connection check to accept a new player after the raid started.
- Follow what the reconnect path sends (doors, lamps, windows, characters, loot refetch) and what
  a first join sends, and find what a brand-new player is missing in a running raid.
- Try it with a player host and with the headless.
- Answer needed: works / works with gaps / not without changes inside Fika. Note how deep the
  patch into Fika goes; that is the maintenance cost.

**S3. Items the mod creates, under a host that owns inventory.**
- Host spawns a loose item through Fika's own spawn-item path; client picks it up.
- A custom container (the stash) whose content came from this mod's server: can a client move an
  item from it into their pack and back, with the host accepting? What must the host know first?
- A trade: remove money and add goods on a client through the inventory controller so it
  replicates. Compare with what `LocalTrade` does today.
- Answer needed: the pattern to use for tables, stash, trading and crafting in stages 3 and 4.

**S4. Starting and joining raids from this mod's menu.**
- Find the calls behind Fika's host and join buttons. Can the Continue item create a match on the
  saved map, and can a "Join" item list and join a friend's match, without showing Fika's screens?
- Check what the headless does with this mod's menu patches active; it drives the menu itself.
- Answer needed: keep this mod's menu and call into Fika / show Fika's screens from it / leave
  the menu to Fika when it is installed.

## C. Checks that are quick to do in the same sitting

- Does the raid-end patch on the single-player game class fire under Fika's game classes?
  (Expected: no; use Fika's game-ended event.)
- Order of the three patches on the server's raid start and raid end (Fika, Vagabond, this mod).
- What a dead or spectating client does at the moment the group transits.
- What a client sees when a player host crashes.
- Whether the time limit this mod lifts on every map still holds under Fika's raid settings.

## Done when

- Two players and a headless can be started and driven by the dev kit.
- `spike-findings.md` has a clear answer for S1 to S4 and the quick checks.
- The overview's risks and the stage files for 3 to 7 are corrected to match what was found.
- The author has answered the decisions in the overview that the next stage needs.
