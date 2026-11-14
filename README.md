# TradersInRaid

A mod for Single Player Tarkov (SPT) 4.1.x that moves trading, quest hand-ins and the hideout out of the menus and into the raid.

Built and tested against SPT 4.1.6 (client 0.16.9.5) together with the open-world mod [Vagabond](https://github.com/MrEliasen/SPT-Vagabond) 0.8.3. It works without Vagabond, but then traders have to be placed by hand (see "Trader spots").

## What it does

**Traders have rooms on the maps.** At each trader's spot there is a door. Using it takes you into that trader's own room, which lies outside the playable map: bots cannot follow or shoot in. The trader stands inside. Looking at them offers:

- **Trade with ...** opens the normal trade screen. Trades are settled on what you carry: payment is taken from your gear, purchases go into your gear, and the stash is not involved.
- **Collect deliveries** drops everything waiting in your mail (quest rewards, insurance returns, from every sender) on the table in the room. An item leaves the mail only when you pick it up, so whatever you leave on the table is there again next visit.
- **Save and go to menu** ends the raid at that spot and returns to the main menu.

The rooms are furnished from the game itself: walls, floor, tables, shelves, crates and the door are copies of pieces of the game's hideout interior, laid out differently for each trader. The layouts are plain text in `TradersInRaid/RoomLayouts.cs`.

**Quests work at those traders.** Accept and hand in quests through the trader's Tasks tab. On hand-in the reward items land on the table at once; experience, standing and follow-up quests are paid when the raid ends. Trader sales totals and loyalty levels grow from in-raid trades.

With "Put traders in rooms" switched off, traders stand in the open at their spots as figures, and deliveries go straight into your gear.

**No menus between raids.** When a raid ends the result screens are passed through automatically. If it ended at your Vagabond hideout entrance, the hideout opens; leaving the hideout starts a raid at its door. After a death or an ordinary extract the next raid starts by itself. Hold Backspace during the short countdown to stay in the main menu.

## Install

Unpack the release zip into the SPT game folder (the one containing `EscapeFromTarkov.exe`). It adds:

- `BepInEx/plugins/TradersInRaid/TradersInRaid.dll` (client)
- `BepInEx/plugins/TradersInRaid/traderrooms` (the rooms; without this file the mod builds plain grey ones)
- `SPT_Runtime/user/mods/TradersInRaid/TradersInRaid.Server.dll` (server)

Restart the server and the game.

If you use Vagabond and want to receive trader mail inside the hideout, set `"LimitTraderMailAccess": false` in Vagabond's `vagabond.json`.

## Settings

In game, press F12 and open TradersInRaid.

| Section | Setting | Default | Meaning |
|---|---|---|---|
| General | Show trader figures | on | Stand a character at each trader spot |
| General | Show trader markers | on | Show a box at a spot until its figure has loaded |
| General | Also use traders.json spots | off | Add hand-placed spots on top of Vagabond's |
| General | Capture position key | F10 | Write your current position to `captured_positions.txt` |
| (no setting) | Hideouts, crafting, sleep, saving | on | Always on when the hideout doors exist on the map; `hideouts.json` next to the plugin can move or disable a door |
| Trader rooms | Put traders in rooms | on | Give each trader a room behind a door; off puts them back in the open |
| Trader rooms | Furnish rooms from the game | on | Build the rooms from the game's own furniture and wall materials; off uses the mod's own simple rooms |
| Trader rooms | Room depth below the door | 300 | How far under the map the rooms are built, in metres |
| No menus | Enabled | on | Skip result screens and continue automatically |
| No menus | Seconds before continuing | 4 | Length of the countdown in the main menu |
| No menus | Stay in menu key | Backspace | Hold during the countdown to stay in the menu |
| Testing | Undying | off | Heal fully and take no damage |
| Testing | Self-test key | F8 | Run the inventory steps a trade is made of and report |
| Testing | Test harness | off | Carry out commands from a file in the running raid (see "Testing") |

## Trader spots

With Vagabond installed, traders stand at Vagabond's trader extracts, read from its `trader_locations.json`.

Without it, or with "Also use traders.json spots" on, spots come from `BepInEx/plugins/TradersInRaid/traders.json`, which is created on first run. Stand where you want a trader, press F10, and copy the line from `captured_positions.txt` into that file with the trader's name and id filled in.

## Hideouts

There are three hideouts, each behind an existing door on its map: a red metal double door near Prapor on Streets, the door of a metal cabin north of Skier on Customs, and a grey steel door in a red shed west of Peacekeeper on Shoreline. Looking at the door offers **Enter hideout**. Inside is the game's own hideout interior, loaded into the raid; bots cannot follow.

Each hideout is separate: its own stations, its own stash, its own crafts. The character's existing hideout and stash become the Streets one.

- **Building.** Look at a station and choose **Build** or **Upgrade**. A level needs only its materials, taken from what you carry (pack, rig, pockets, secure container; never what you wear or hold) and then from that hideout's stash. Requirements for other stations, trader loyalty and skills are gone, and construction is instant.
- **Stash.** The Stash station offers **Open stash**: your gear beside that hideout's stash. It is saved to the server whenever it is closed.
- **Crafting.** A built station offers **Craft...**, which turns its action list into the recipe list, a page at a time. Tools are needed but not used up. When the time is up, **Collect** puts the result in that hideout's stash. Recipes locked behind quests stay locked until those quests are done.
- **Boosts.** While you are inside, the hideout's built stations give back health, energy and water, at the game's own bonus amounts per game hour.
- **Sleeping.** A built Rest Space offers sleeping for 2, 4 or 8 hours, or until morning. The clock jumps, you heal 20, 40 or 70 health per hour by Rest Space level, and you use up energy and water as if the time had passed.
- **Saving.** Sleeping saves the game, and the Rest Space also offers **Save**. If you die, the save is put back (gear, health, skills, quests, all three hideouts and the clock) and the next raid begins beside that bed. The same happens if the game stops without the raid ending. Leaving a raid in the ordinary way carries on from where you left.

### Time

The character has one world clock. It runs at the raid's pace (seven game hours to a real hour) while a raid runs, jumps when you sleep and stands still while the game is closed. The time of day carries over from raid to raid, and crafts in every hideout are timed on it, so they keep going while you are elsewhere.

A raid has no time limit; it lasts until you leave, extract, die or quit. The countdown in the corner is hidden.

## Doors

The door is stood against the nearest flat stretch of wall near the trader's spot, and on the spot itself if there is none. Where that choice looks wrong, put the door by hand: stand where it should be, facing the way it should face, press F10, and copy the line from `captured_positions.txt` into `BepInEx/plugins/TradersInRaid/doors.json` (a list, same format as `traders.json`) with the trader's id filled in.

## Known limits

- Only the backpack (or the rig, if there is no backpack) is shown for selling. Payment and purchases use all carried containers.
- Barter trades paid with items are not counted toward trader loyalty.
- Quests that give you an item when you accept them do not deliver it if accepted in a raid.
- Not built for the hideouts: generator fuel, the bitcoin farm, the scav case, the water collector's filter and the cultist circle. Skill and experience bonuses from stations are not applied.
- Hideout bonuses apply only while you are inside; menus outside a raid show the hideout last entered.
- Gear lost in a death is still reported to insurance by the game although the save brings it back, so insured items can return twice.
- Inside a hideout you arrive in a bare corridor and leave through a door prop; waking up after a death puts you by the Rest Space.
- Over a very long raid bots may thin out, since the game spawns them on a schedule made for short raids.
- If a raid is lost to a crash after a hand-in, the reward items you picked up are kept although the quest is not recorded as finished.
- A room is bare for the first half minute of a raid, until the furniture has been fetched.
- Trader figures are dressed stand-ins built from the game's own clothing; the game has no trader character models.
- Post-raid healing is skipped along with the other result screens.

## Build

Needs the .NET 10 SDK and an installed SPT 4.1.x copy to reference.

```
dotnet build TradersInRaid/TradersInRaid.csproj -c Release -p:TarkovDir=F:\SPT\
dotnet build TradersInRaid.Server/TradersInRaid.Server.csproj -c Release -p:TarkovDir=F:\SPT\
```

The rooms are built separately, with Unity 2022.3.43f1 (see `Unity/TraderRooms/README.md`); the result is `Unity/TraderRooms/Build/traderrooms`.

`pack.ps1` builds both DLLs, adds the rooms file if it has been built, and writes a release zip to `dist/`. The server project references `Vagabond.Common.dll` from the Vagabond server mod folder.

## Testing

With "Test harness" on, the mod reads `BepInEx/plugins/TradersInRaid/test/commands.txt` twice a second while in a raid, carries the lines out in order and appends what happened to `test/results.log`. Hot reloading with BepInEx ScriptEngine plus this file makes a change testable in seconds without touching the game.

| Command | Does |
|---|---|
| `status` | Map, position, health, which room, indoors or not |
| `check` | Every room: floor, indoor area, doors, shader |
| `enter NAME`, `leave` | Use a door, then check you are standing still and unhurt |
| `goto X Y Z`, `look YAW PITCH`, `lookat NAME`, `wait SECONDS` | Move and turn |
| `screenshot NAME` | Save the view to `test/NAME.jpg` |
| `lay TEMPLATE [COUNT]`, `drop TEMPLATE` | Put a made-up item on the table, or let one fall |
| `deliveries`, `table` | Ask for deliveries; list what lies on the tables |
| `quests`, `handin [QUEST_ID]` | List quests; hand one in whether or not it is done |
| `style NAME` | Rebuild the rooms in another trader's style |
| `layout [NAME]`, `props WORD`, `survey` | Furnish the rooms again (from `test/layout.txt` if it exists); search the hideout's pieces; write the full catalogue to `test/` |
| `savequit` | Choose "Save and go to menu" at the room's trader |
| `startraid` | From the main menu: start a raid where the character is |
| `hideout enter ID`, `hideout leave`, `use` | Go into a hideout (streets, customs, shoreline) and out; use the nearest door of ours |
| `stations`, `build NUMBER`, `give TEMPLATE COUNT` | List stations with costs; build one; hand out materials |
| `stash [open\|close\|put TPL N\|take TPL\|save]`, `carried [TPL]` | Work with the hideout stash; count carried items |
| `recipes [STATION]`, `craft RECIPE_ID`, `collect STATION` | Crafting |
| `clock [add HOURS]`, `sleep HOURS`, `vitals [hurt N]` | The world clock, sleeping, health |
| `save`, `undying on\|off`, `die` | Save at the bed; switch Undying; kill the character |
| `doors [R]`, `scenery [R] [WORD]`, `shot X Y Z NAME`, `aim`, `target` | Scout doors and see what the game offers to interact with |
| `post ROUTE JSON`, `bunker ...`, `hideoutdata` | Call a server route; load and inspect the hideout interior |

`handin`, `savequit`, `build`, `craft`, `save` and `die` change the profile for real; back it up first.

## Credits

The in-raid trading idea follows the In-Raid Traders mod for SPT 3.11 by Lillian. This is a separate implementation for the 4.1 client.
