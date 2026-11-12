# TradersInRaid

A mod for Single Player Tarkov (SPT) 4.1.x that moves trading, quest hand-ins and the hideout out of the menus and into the raid.

Built and tested against SPT 4.1.6 (client 0.16.9.5) together with the open-world mod [Vagabond](https://github.com/MrEliasen/SPT-Vagabond) 0.8.3. It works without Vagabond, but then traders have to be placed by hand (see "Trader spots").

## What it does

**Traders stand on the maps.** Each trader is a figure at a spot on a map. Looking at one offers:

- **Trade with ...** opens the normal trade screen. Trades are settled on what you carry: payment is taken from your gear, purchases go into your gear, and the stash is not involved.
- **Collect deliveries** hands over everything waiting in your mail (quest rewards, insurance returns), from every sender. Anything that does not fit stays in the mail for next time.
- **Save and go to menu** ends the raid at that spot and returns to the main menu.

**Quests work at those traders.** Accepting and handing in quests through a trader's Tasks tab in a raid is paid out when the raid ends, the same as from the menu. Trader sales totals and loyalty levels grow from in-raid trades.

**No menus between raids.** When a raid ends the result screens are passed through automatically. If it ended at your Vagabond hideout entrance, the hideout opens; leaving the hideout starts a raid at its door. After a death or an ordinary extract the next raid starts by itself. Hold Backspace during the short countdown to stay in the main menu.

## Install

Unpack the release zip into the SPT game folder (the one containing `EscapeFromTarkov.exe`). It adds:

- `BepInEx/plugins/TradersInRaid/TradersInRaid.dll` (client)
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
| No menus | Enabled | on | Skip result screens and continue automatically |
| No menus | Seconds before continuing | 4 | Length of the countdown in the main menu |
| No menus | Stay in menu key | Backspace | Hold during the countdown to stay in the menu |
| Testing | Undying | off | Heal fully and take no damage |
| Testing | Give test kit key | F9 | Hand out a full set of gear and money |
| Testing | Self-test key | F8 | Run the inventory steps a trade is made of and report |

## Trader spots

With Vagabond installed, traders stand at Vagabond's trader extracts, read from its `trader_locations.json`.

Without it, or with "Also use traders.json spots" on, spots come from `BepInEx/plugins/TradersInRaid/traders.json`, which is created on first run. Stand where you want a trader, press F10, and copy the line from `captured_positions.txt` into that file with the trader's name and id filled in.

## Known limits

- Only the backpack (or the rig, if there is no backpack) is shown for selling. Payment and purchases use all carried containers.
- Barter trades paid with items are not counted toward trader loyalty.
- Quests that give you an item when you accept them do not deliver it if accepted in a raid.
- Trader figures are dressed stand-ins built from the game's own clothing; the game has no trader character models.
- Post-raid healing is skipped along with the other result screens.

## Build

Needs the .NET 10 SDK and an installed SPT 4.1.x copy to reference.

```
dotnet build TradersInRaid/TradersInRaid.csproj -c Release -p:TarkovDir=F:\SPT\
dotnet build TradersInRaid.Server/TradersInRaid.Server.csproj -c Release -p:TarkovDir=F:\SPT\
```

`pack.ps1` builds both and writes a release zip to `dist/`. The server project references `Vagabond.Common.dll` from the Vagabond server mod folder.

## Credits

The in-raid trading idea follows the In-Raid Traders mod for SPT 3.11 by Lillian. This is a separate implementation for the 4.1 client.
