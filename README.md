# The True SPT

A mod for Single Player Tarkov (SPT) 4.1.x that moves trading, quest hand-ins and the hideout out of the menus and into the raid.

Built and tested against SPT 4.1.6 (client 0.16.9.5) together with the open-world mod [Vagabond](https://github.com/MrEliasen/SPT-Vagabond) 0.8.3. It works without Vagabond, but then traders have to be placed by hand (see "Trader spots").

## What it does

**Traders have rooms on the maps.** At each trader's spot there is a door. Using it takes you into that trader's own room, which lies outside the playable map: bots cannot follow or shoot in. The trader stands inside. Looking at them offers:

- **Trade with ...** opens the normal trade screen. Trades are settled on what you carry: payment is taken from your gear, purchases go into your gear, and the stash is not involved.
- **Collect deliveries** drops everything waiting in your mail (quest rewards, insurance returns, from every sender) on the table in the room. An item leaves the mail only when you pick it up, so whatever you leave on the table is there again next visit.
- **Save and go to menu** ends the raid at that spot and returns to the main menu.

The rooms are furnished from the game itself: walls, floor, tables, shelves, crates and the door are copies of pieces of the game's hideout interior, laid out differently for each trader. The layouts are plain text in `TheTrueSPT/RoomLayouts.cs`.

Things the BTR driver carries for you between maps are put in the hideout's stash when you next enter it (whatever does not fit waits for the next visit); they never land on a trader's table.

**Quests work at those traders.** Accept and hand in quests through the trader's Tasks tab. On hand-in the reward items land on the table at once; experience, standing and follow-up quests are paid when the raid ends. Trader sales totals and loyalty levels grow from in-raid trades.

With "Put traders in rooms" switched off, traders stand in the open at their spots as figures, and deliveries go straight into your gear.

**A main menu of its own.** The menu is a title and four words at the top left: **Continue**, **Reset**, **Settings**, **Exit**, over a live view of your own hideout as you have built it. Under Continue it says where and when the game picks up. Character, Trade, Hideout and the bar along the bottom are gone from this screen; everything they led to is reached inside the world. Continue starts a raid where the character is (after a death or a crash, at the bed of the last save). Reset makes the character afresh, keeping a backup, and closes the game: the new character wakes inside the hideout, by its Customs door, with a small kit and nothing else (a pistol with three loaded magazines and spare rounds, soft armour, a rig, a backpack, something for wounds, water and crackers, and the small secure container with 30,000 roubles); the stash is empty.

**No menus between raids.** When a raid ends the result screens are passed through automatically. If it ended at your Vagabond hideout entrance, the hideout opens; leaving the hideout starts a raid at its door. After a death or an ordinary extract the next raid starts by itself. Hold Backspace during the short countdown to stay in the main menu.

## Install

Unpack the release zip into the SPT game folder (the one containing `EscapeFromTarkov.exe`). It adds:

- `BepInEx/plugins/TheTrueSPT/TheTrueSPT.dll` (client)
- `BepInEx/plugins/TheTrueSPT/traderrooms` (the rooms; without this file the mod builds plain grey ones)
- `SPT_Runtime/user/mods/TheTrueSPT/TheTrueSPT.Server.dll` (server)

Restart the server and the game.

If you use Vagabond and want to receive trader mail inside the hideout, set `"LimitTraderMailAccess": false` in Vagabond's `vagabond.json`.

## Settings

In game, press F12 and open The True SPT.

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
| Pictures | Take a picture key | F11 | Save what is on screen, without the interface, to `BepInEx/plugins/TheTrueSPT/screenshots` |
| Pictures | Hide the interface key | F9 | Hide the whole interface until pressed again |
| Pictures | Picture size | 2 | How many times the screen's size a saved picture is |
| No menus | Enabled | on | Skip result screens and continue automatically |
| No menus | Seconds before continuing | 4 | Length of the countdown in the main menu |
| No menus | Stay in menu key | Backspace | Hold during the countdown to stay in the menu |
| Testing | Undying | off | Heal fully and take no damage |
| Testing | Free hideout | off | Building, upgrading and crafting in a hideout need no materials and no tools |
| Testing | Self-test key | F8 | Run the inventory steps a trade is made of and report |
| Testing | Test harness | off | Carry out commands from a file in the running raid (see "Testing") |

## Trader spots

With Vagabond installed, traders stand at Vagabond's trader extracts, read from its `trader_locations.json`.

Without it, or with "Also use traders.json spots" on, spots come from `BepInEx/plugins/TheTrueSPT/traders.json`, which is created on first run. Stand where you want a trader, press F10, and copy the line from `captured_positions.txt` into that file with the trader's name and id filled in.

## Hideouts

There is one hideout, reached through an existing door on each of three maps: a red metal double door near Prapor on Streets, a door on the east side of Customs, and a grey steel door in a red shed west of Peacekeeper on Shoreline. Looking at the door offers **Enter hideout**. Inside is the game's own hideout interior, loaded into the raid; bots cannot follow.

Looking at a station and using it opens the station's window: what its next level needs and how much of that you have, what it can make, from what, how long that takes (in game time and in time played), and what is being made now. Esc closes it. The way out of a hideout is the shut door at the end of the small room up the steps.

All three doors lead to the same hideout: the same stations, stash and crafts. It keeps running on the world clock wherever you are, including on maps that have no door to it.

- **Building.** Look at a station and choose **Build** or **Upgrade**. A level needs only its materials, taken from what you carry (pack, rig, pockets, secure container; never what you wear or hold) and then from that hideout's stash. Requirements for other stations, trader loyalty and skills are gone, and construction is instant.
- **Stash.** The Stash station offers **Open stash**: your gear beside that hideout's stash. It is saved to the server whenever it is closed.
- **Crafting.** A built station offers **Craft...**, which turns its action list into the recipe list, a page at a time. Tools are needed but not used up. When the time is up, **Collect** puts the result in that hideout's stash. Recipes locked behind quests stay locked until those quests are done.
- **Power.** The Generator takes fuel cans (put in and taken out in its window) and has a switch. While it is on, fuel burns on the world clock, also while you are away or asleep. Without power the lamps are out and nothing electric works: a craft cannot be started, a craft under way waits and carries on when power is back, and the bitcoin farm stops. Only the Lavatory is worked by hand. The lamps themselves (candles, bulbs, fluorescent) are put up from the Generator's window.
- **Bitcoin farm.** Fit graphics cards in its window; while there is power it mines bitcoins at the game's rate for that many cards, up to three waiting, collected into the stash.
- **Weapon modding.** A built Workbench lists the weapons you carry and those in the stash; MODIFY opens the game's own modding screen, with parts from your gear and the stash. Inside the hideout the menu's rules apply, vital parts included, except on the weapon in your hands: put it away first. With the UI Fixes mod installed, its in-raid modding rule is lifted inside the hideout too.
- **Kept on stations.** The Weapon Stand (both racks), the Place of Fame and the Equipment Presets Stand each have a STORE / TAKE button in their window that opens what they keep beside your gear; it is saved when closed.
- **While you are inside.** Every bot on the map is switched off and switched back on, where it stood, when you leave. The hideout's lamps cast no shadows, which is worth about a third of the frame rate with a map loaded above; outside, the interior itself is switched off.
- **Water Collector.** At level 3 it takes a water filter in its window and fills bottles of clean water on the world clock, up to three waiting. It needs no power; the filter wears out as it works.
- **Solar Power.** Once built, the generator burns half the fuel.
- **Scav Case.** Its window lists what the scavs can be paid (from 2,500 roubles to an intelligence folder), how long they are out and what they tend to bring; when they are back, the finds go into the stash.
- **Gym.** TRAIN in its window is a set that costs 30 energy and 30 water and gives Strength and Endurance points (18 each at low levels, fewer as the skills rise). Muscles ache for a game day before the next set.
- **Cultist Circle.** Open the circle from its window, lay things in it, and SACRIFICE. The game's own rite prices them and decides what comes back and how long it takes (six to fourteen game hours); the window keeps the circle shut until then, and what came back lies in it.
- **Shooting Range.** With power, its controls offer their actions when looked at: start and stop a training, raise and lower the poppers, run, stop and fold the rail target, hang new paper targets.
- **Boosts.** While you are inside, the hideout's built stations give back health, energy and water, at the game's own bonus amounts per game hour.
- **Sleeping.** A built Rest Space offers sleeping for 2, 4 or 8 hours, or until morning. The clock jumps, you heal 20, 40 or 70 health per hour by Rest Space level, and you use up energy and water as if the time had passed.
- **Saving.** Sleeping saves the game, and the Rest Space also offers **Save**. If you die, the save is put back (gear, health, skills, quests, all three hideouts and the clock) and the next raid begins beside that bed. The same happens if the game stops without the raid ending. Leaving a raid in the ordinary way carries on from where you left.

With the Dynamic Maps mod installed, the hideout's door and the traders' doors on the map you are on are marked on its map. Without that mod there are no markers and nothing else changes.

### What the stations' bonuses do

The server writes a station's bonuses into the character when its level is built, and the game reads them from there in a raid. Read from the game's code, not measured:

- **Count in a raid, by the game itself:** experience rate (applied to the raid's experience when it ends), skill group boosts (Library, Shooting Range, Air Filtering Unit, Place of Fame), the larger energy reserve from the Rest Space, and the repair bonuses wherever something is repaired.
- **Applied by this mod, while you are inside the hideout:** health, energy and water regeneration.
- **Counted by this mod:** Solar Power's halving of fuel use. The smaller fuel bonuses of the Emergency Wall are not.
- **Only matter in screens this mod no longer uses:** flea market commission, scav cooldown, insurance return time, quest money.

### Time

The character has one world clock. It runs at the raid's pace (seven game hours to a real hour) while a raid runs, jumps when you sleep and stands still while the game is closed. The time of day carries over from raid to raid, and crafts in every hideout are timed on it, so they keep going while you are elsewhere.

A raid has no time limit; it lasts until you leave, extract, die or quit. The countdown in the corner is hidden.

## Doors

The door is a real door of the game. Where a good one was picked by hand it is used (Prapor and Fence on Streets, Skier on Customs and Ragman on Interchange use doors that are part of the map). Elsewhere a door is stood against the nearest flat stretch of building wall near the trader's spot (fences, gates and low walls are passed over), and on the spot itself if there is none. Where that choice looks wrong, put the door by hand: stand where it should be, facing the way it should face, press F10, and copy the line from `captured_positions.txt` into `BepInEx/plugins/TheTrueSPT/doors.json` (a list, same format as `traders.json`) with the trader's id filled in.

## Known limits

- Only the backpack (or the rig, if there is no backpack) is shown for selling. Payment and purchases use all carried containers.
- Barter trades paid with items are not counted toward trader loyalty.
- Quests that give you an item when you accept them do not deliver it if accepted in a raid.
- Not built for the hideouts: the air filter's filters (its skill bonus is in the profile whether or not a filter is in) and the equipment stand's outfits.
- Hideout bonuses apply only while you are inside; menus outside a raid show the hideout last entered.
- Gear lost in a death is still reported to insurance by the game although the save brings it back, so insured items can return twice.
- Waking up after a death puts you by the Rest Space.
- Over a very long raid bots may thin out, since the game spawns them on a schedule made for short raids.
- If a raid is lost to a crash after a hand-in, the reward items you picked up are kept although the quest is not recorded as finished.
- A room is bare for the first half minute of a raid, until the furniture has been fetched.
- Trader figures are dressed stand-ins built from the game's own clothing; the game has no trader character models.
- Post-raid healing is skipped along with the other result screens.

## Build

Needs the .NET 10 SDK and an installed SPT 4.1.x copy to reference.

```
dotnet build TheTrueSPT/TheTrueSPT.csproj -c Release -p:TarkovDir=F:\SPT\
dotnet build TheTrueSPT.Server/TheTrueSPT.Server.csproj -c Release -p:TarkovDir=F:\SPT\
```

The rooms are built separately, with Unity 2022.3.43f1 (see `Unity/TraderRooms/README.md`); the result is `Unity/TraderRooms/Build/traderrooms`.

`pack.ps1` builds both DLLs, adds the rooms file if it has been built, and writes a release zip to `dist/`. The server project references `Vagabond.Common.dll` from the Vagabond server mod folder.

## Testing

With "Test harness" on, the mod reads `BepInEx/plugins/TheTrueSPT/test/commands.txt` twice a second while in a raid, carries the lines out in order and appends what happened to `test/results.log`. Hot reloading with BepInEx ScriptEngine plus this file makes a change testable in seconds without touching the game.

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
| `startraid`, `menu`, `press NAME` | From the main menu: start a raid where the character is; list what the menu shows; click one of its items |
| `backdrop X Y Z YAW PITCH`, `backdrop free`, `backdrop lamp POWER` | Hold the menu's view of the hideout at one spot; let it move again; set its lamp |
| `hideout enter ID`, `hideout leave`, `use` | Go into a hideout (streets, customs, shoreline) and out; use the nearest door of ours |
| `panel NUMBER`, `panel press WORDS`, `panel lines`, `panel close`, `locate WORD` | Open a station's window, press one of its buttons, read it, close it; find hideout objects by name |
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
