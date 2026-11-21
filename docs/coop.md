# Zero Menu Time with friends (Fika)

Zero Menu Time works with Fika, on a player's own game or on a headless host. This is the first
co-op release: it has been through a long scripted test with two game copies and a headless host
on one machine, and far less play by people. Back up your profiles before trying it.

## What you need

On **every** player's game, and on the headless host if you use one:

- SPT 4.1.6, Vagabond 0.8.3, Fika 2.4.6 (client), and Zero Menu Time with its co-op add-on
  (`ZeroMenuTime.Fika.dll`, in the same folder as `ZeroMenuTime.dll`).
- The same version of Zero Menu Time everywhere. A player whose version differs from the host's
  gets its doors and rooms switched off for that raid.

On the server: Fika's server mod 2.4.2 and Zero Menu Time's server mod.

In Fika's server settings: leave its own **revive** option **off**. This mod has its own.

The headless host must not have mods that Fika's headless refuses (Dynamic Maps is one).

## Starting

- **Continue** hosts your own world on your own game. It starts at once; there is no waiting room.
- **Join a friend** shows the raids that are running and joins the one you pick, at any time.
  You come in where they came into the map.
- **Continue on headless** hosts your world on a free headless host. Then you can save and leave
  while others play on, and come back.

If you host on your own game you cannot leave while others are in your raid: choosing
"Save and go to menu" asks whether to end the raid for everyone.

## Whose world it is

The world belongs to whoever hosts it (on a headless host: whoever asked for the raid).

- **The clock** is theirs. Your own clock catches up to it when you go home, and never goes back.
- **The hideout** is theirs: their stations, their generator, their crafts. Only they can build
  and train there. Everyone can use the stations.
- **There is one stash, and it is theirs.** Everyone can put things in and take things out.
  **What is in the stash when the raid ends belongs to the host.**
- Trading, deliveries and quests are your own, as always. What you buy lies on the trader's
  table where anyone can pick it up.

## Time

- The raid stands still only while **every living player** is inside a trader's room or the
  hideout. One player indoors does not stop the world for the others.
- **Sleeping** needs everyone alive to be in the hideout, and everyone's yes. **Waiting until
  morning** at a trader needs everyone's yes. Time then moves on for all.

## Dying

- If you die while a teammate is alive, the raid goes on. Everything you carried drops where you
  fell (your secure container, armband, knife and dog tag stay with you), and you watch the
  others: left and right arrow change whom.
- A teammate can **revive** you at any trader for **100,000 roubles** from what they carry, or
  for free at the hideout's **medical station**. You come back where you came into the map, hurt
  and hungry, with nothing. Your gear is where you fell.
- If the **last** living player dies, or leaves, everyone is dead: the raid ends, and each
  player's own "When you die" setting applies, as it does alone.
- If the group goes through a **transit** while you are dead, you come to life on the new map,
  and your "When you die" setting applies there: *Keep items* lays the gear of your last save in
  front of you; *Lose carried gear* leaves you what the death left; *Lose everything* also
  empties your secure container.

## Saving

Saving is each player's own: at the bed, or with **Save** at a trader. If you die for good, you
are put back at your last save, as alone.

## Known limits

- With a dead teammate, the transit refuses whoever uses it before the dead one has been brought
  there. A notice tells you so; use the transit again.
- With *Keep items*, gear can be doubled: a teammate can carry your dropped gear through a
  transit while you also get the gear of your save.
- The stash cannot be sorted in co-op.
- A game that crashes can come back into the same raid with **Join a friend**; the host drops a
  lost player after about half a minute.
- On a headless host the hideout's floor does not exist for the host itself; things dropped on
  the hideout floor may behave oddly.
- Repairs change durability on your own machine only; others see the old numbers.
- A thing laid down for everyone (a purchase, a reward) appears for the others a moment later.
  Leave it a second before picking it up.
- Hosting on your own game needs a lot of memory with two players on a large map.
- Tested with two players and a headless host. More players, and play over a real network,
  have not been tried by the author.
