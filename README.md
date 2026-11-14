# TradersInRaid

A mod for Single Player Tarkov (SPT) 4.1.x that moves trading, quest hand-ins and the hideout out of the menus and into the raid.

Built and tested against SPT 4.1.6 (client 0.16.9.5) together with the open-world mod [Vagabond](https://github.com/MrEliasen/SPT-Vagabond) 0.8.3. It works without Vagabond, but then traders have to be placed by hand (see "Trader spots").

## What it does

**Traders have rooms on the maps.** At each trader's spot there is a door. Using it takes you into that trader's own room, which lies outside the playable map: bots cannot follow or shoot in. The trader stands inside. Looking at them offers:

- **Trade with ...** opens the normal trade screen. Trades are settled on what you carry: payment is taken from your gear, purchases go into your gear, and the stash is not involved.
- **Collect deliveries** drops everything waiting in your mail (quest rewards, insurance returns, from every sender) on the table in the room. An item leaves the mail only when you pick it up, so whatever you leave on the table is there again next visit.
- **Save and go to menu** ends the raid at that spot and returns to the main menu.

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
| Trader rooms | Put traders in rooms | on | Give each trader a room behind a door; off puts them back in the open |
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

## Doors

The door is stood against the nearest flat stretch of wall near the trader's spot, and on the spot itself if there is none. Where that choice looks wrong, put the door by hand: stand where it should be, facing the way it should face, press F10, and copy the line from `captured_positions.txt` into `BepInEx/plugins/TradersInRaid/doors.json` (a list, same format as `traders.json`) with the trader's id filled in.

## Known limits

- Only the backpack (or the rig, if there is no backpack) is shown for selling. Payment and purchases use all carried containers.
- Barter trades paid with items are not counted toward trader loyalty.
- Quests that give you an item when you accept them do not deliver it if accepted in a raid.
- If a raid is lost to a crash after a hand-in, the reward items you picked up are kept although the quest is not recorded as finished.
- The rooms are made of simple shapes with generated textures.
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
| `savequit` | Choose "Save and go to menu" at the room's trader |

`handin` and `savequit` change the profile for real; back it up first.

## Credits

The in-raid trading idea follows the In-Raid Traders mod for SPT 3.11 by Lillian. This is a separate implementation for the 4.1 client.
