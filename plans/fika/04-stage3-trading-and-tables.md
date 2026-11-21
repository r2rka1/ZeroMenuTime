# Stage 3: trading, tables, quests and repairs

Goal: everything that creates, removes or moves items at a trader works when the host owns
inventory operations, and what lies on a table is the same for everyone.

Depends on: stage 2, and the pattern found in spike S3.

## Stage 0 result

The pattern is proven and simpler than first written: any machine may create an item, as long
as it announces it with this mod's packet so every machine creates the same one (same ids). The
host does not have to be asked first. The numbered flow below stands with step 2 and 3 merged:
the creating machine lays the item and announces it. See `spike-findings.md`.

## The rule for every item this mod makes

An item exists in the raid only after the host created it. A client never creates an item
locally. The flow for anything new:

1. The client's request goes to this mod's server as today (purchase, mail, reward, craft).
   The server answers with the items.
2. The client sends the items to the host ("put these on table X for player P" or "give these to
   player P").
3. The host creates them and announces them through Fika's item-spawn path, so every machine has
   the same ids.
4. From then on, picking up, moving and dropping are ordinary inventory operations and Fika
   replicates them.

Removing items a player carries (payment, sold goods, hand-in items, crafting materials) is done
as inventory operations on the owner's controller so the host and observers follow. `LocalTrade`
today edits the carried items directly; that is the main rewrite of this stage.

## Trading

- The trade screen stays local to the player using it. Two players can trade with the same
  trader at once; each settles against their own profile and loyalty.
- Buying: payment removed by operations; goods go through the rule above onto the table.
- Selling: goods removed by operations; money created through the rule above.
- The stand-in stash that shows the pack in the trade screen is local presentation; make sure it
  never reaches the host as a real container.
- Loyalty and sales totals stay per player on the server (unchanged).

## Tables

- Items on a trader's table are world loot spawned by the host. Each carries who it is for.
- Decide in the detailed plan: can a teammate take another player's goods from the table?
  Proposed: yes, it is loot in the world; the mail bookkeeping marks it taken by its owner
  either way, so nothing is duplicated.
- "Taken" detection today is "the item vanished from the table or is in my gear". Replace with the
  host telling the owner's client that the item left the table.
- A late joiner (stage 6) receives what lies on the tables with the rest of the world loot; check.

## Deliveries (mail) and quests

- Mail is per player: each player collects their own at any trader. Unchanged on the server.
- Quest hand-in at the counter: the hand-in itself is the game's per-player quest code; reward
  items follow the rule above. The patch that stops the game putting rewards into gear must also
  hold under Fika's quest controllers (it has two: normal and shared progression).
- With Fika's shared quest progression on, a quest can complete for several players at once:
  each gets their own reward on the table of the trader they hand in at. Check for double mail.
- Quest start items: same path as rewards.
- Quest sync to Vagabond (which opens its quest exits) stays per player; on a headless raid
  Vagabond already merges the members' quest exits.

## Repairs and sealed cases

- Repair with kits changes durability on items the player carries and uses up kit points. Both
  must go through operations or a small message to the host so the values match everywhere.
- Unpacking sealed cases creates items: same rule; contents go to the stash (stage 4) or the table.

## Headless

- The headless has the rooms (stage 2) and performs the host's part of every flow above. It has
  no trade screen and no mail of its own.

## Feature gate after this stage

On: trade, collect deliveries, quest hand-in, repair. Still off in a group: hideout, save, sleep.

## Checks

- Client buys three things: payment leaves the client's pack on every machine; goods appear on
  the table on every machine; client picks them up; host log shows no refused operation.
- Client sells; client hands in a quest with two reward weapons; client collects mail.
- Two players trade at the same trader at the same time.
- Player A's goods taken by player B: mail shows them delivered, nothing doubles.
- All of the above with a headless host.
- Alone without Fika: same behaviour as 1.2.4 (the single-player seam runs the same code path).

## Risks

- This touches the most code of any stage. Do it for one flow first (buy), prove the pattern,
  then the rest.
- Operations refused by the host leave the client out of step; every flow needs a failure path
  that puts money and goods back.

---

## Result (2026-11-20, branch `fika/stage3`)

**Built**

- `InventorySync` (`zmt.inv`): when a trade, a build or a craft takes something out of what the
  local player carries (a whole item, or part of a stack), the machine says so by item id and
  every other machine does the same to its copy of that player. `LocalTrade.Take` calls it.
- Items for tables in co-op are set down by hand on the creating machine and announced
  (`TellItem`): every machine lays the same item at exactly that place. `RewardTable.Place` has
  the co-op path; alone it throws the item as before.
- `LocalTrade.Give` in co-op never puts an item straight into gear: it is laid on the table of
  the room the player stands in, or in front of them (`RewardTable.LayNear`). That covers money
  from selling, craft products and mail collected where there are no rooms.
- Gate: trade, deliveries, quest rewards at the counter and repairs are on in co-op.

**Seen (host + client, Customs)**

- Client collected deliveries and bought two items: all 11 things lay on the table on the host
  too, with the same ids. The host's copy of the client's money stack dropped to the same count.
- Client took a bought item; the host took one of the client's delivered items. Both were
  accepted, and each left the other machine's world.
- Client accepted and handed in a quest: the reward (two stacks of roubles and a weapon) lay on
  the table on both machines. No errors in either log.

**Not tested**

- Selling (the harness has no sell command; it uses the same two primitives as buying).
- Repairs in co-op: switched on, not run. Durability is changed on the owner's machine only and
  is not sent to the others; damage is worked out on the owner's machine, so this should only
  affect what others see when inspecting.
- Two players trading with the same trader at the same time; Fika's shared quest progression.
- The headless as host for these flows (it relays the same packets as a player host).

**Known gaps**

- When a teammate takes an item from another player's table, the owner's list of "what is on my
  table" can go on showing it for a while; the mail bookkeeping still marks it taken.
- Items with nothing solid on them get a look-at box only on the machine that laid them.

**Test campaign, 2026-11-21.** Now run: two players buying from one trader at the same moment; selling in co-op (the item is gone for the host, the payment is laid in the room on both machines); repair with a kit. See `TESTING.md`.
