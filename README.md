# Zero Menu Time

**Play SPT without ever leaving the world.** Trade with traders in their own rooms on the maps, hand in quests over the counter, build and run your hideout behind a real door, sleep in your own bed, and save your game there. The main menu is down to four words, and the menus between raids are gone.

Client and server mod for **SPT 4.1.6**. Requires **[Vagabond](https://sp-mod.com/mod/2642/vagabond)** (the open-world mod), which provides where traders stand and how maps connect.

![The main menu](docs/screenshots/main-menu.jpg)

| | |
|---|---|
| ![Trading in a trader's room](docs/screenshots/trading-in-the-room.jpg) | ![Quests at the trader](docs/screenshots/quests-at-the-trader.jpg) |
| ![The Workbench window](docs/screenshots/workbench-window.jpg) | ![The Rest Space window](docs/screenshots/rest-space-window.jpg) |

---

## What it does

### Traders live on the maps

- Every trader has a **door** at their spot on the map. Through it is the trader's **own room**, furnished in their style (Prapor's armoury, Therapist's clinic, Skier's den, Jaeger's cabin, ...). Bots cannot follow you in.
- Inside, talk to the trader: **Trade**, **Collect deliveries**, **Save and go to menu**.
- Trades are paid from what you carry and bought items go into your gear. The stash is not involved.
- **Quests** are accepted and handed in at the trader. On hand-in the **reward items are laid on the table** in front of you; experience and standing follow when the raid ends.
- Mail from traders (quest rewards, insurance returns) waits **on the table**. Anything you leave there is still there next time.
- Every trader stocks a few **field supplies** (basic meds, food, water, a surgical kit) and the **map of the area they stand in**, so you are never stranded.

### One hideout, behind real doors

- The game's own hideout, loaded into the raid, behind an existing door on **Streets**, **Customs** and **Shoreline**. All three doors lead to the same hideout.
- Stations open a **window** showing what the next level needs (and how much you have, with item pictures), what they can make, from what, and how long it takes.
- **Build anything in any order**: no station, loyalty or skill requirements, and construction is instant. You still pay the materials.
- **Crafting**, timed on the world clock: crafts keep running while you are away, asleep, or on another map.
- **Power**: fuel the Generator and switch it on. Without power the lights go out, crafts pause, and the bitcoin farm stops. Lamps (candles, bulbs, fluorescent) are fitted from the Generator's window.
- **Bitcoin farm**, **Water Collector** with filters, **Solar Power**, **Scav Case**, **Cultist Circle**, **Gym** workouts, **Shooting Range** controls.
- **Weapon modding** at the Workbench with the game's own modding screen, vital parts included, using parts from your gear and the stash.
- **Weapon Stand, Place of Fame, Equipment Stand** hold and display what you put on them.
- **Stash** in the hideout. Things sent between maps by the BTR driver arrive there.
- While you are inside, the hideout's stations **heal and feed you** at the game's own bonus rates, and every bot on the map is paused.

### Time, sleep and saving

- **One world clock**. The time of day carries on from raid to raid, and **raids have no time limit**.
- **Sleep** at the Rest Space: the clock jumps, you heal, and you get hungry and thirsty as if the hours had passed.
- **Save** at the bed (sleeping saves too), or with **Save and go to menu** at a trader. If you die, your **last save** is put back: you wake at your bed, or continue at the trader you saved at, with the gear, health, skills, quests, hideout and clock of that save. If the game stops mid-raid after a bed save, you wake at your bed.

### A menu of its own

- The main menu is **Continue / Reset / Settings / Exit** over a live view of your own hideout. Under Continue it says where and when you will pick up.
- **Continue** starts a raid where your character is.
- A **new character** gets the same start as Reset, without asking.
- **Reset** starts over, keeping a backup of the old character: you wake in the hideout by its Customs door with a pistol and three magazines, soft armour, a rig, a backpack, medicine, water, food, and the Alpha container with 30,000 roubles.
- Result screens after a raid are skipped and the next raid starts by itself (hold Backspace to stay in the menu).

### Extras

- With **Dynamic Maps**, the hideout and trader doors of the map you are on are marked on its map.
- **F11** takes a clean picture without the interface; **F9** hides the interface.

---

## Where the doors are

Every door is one of the map's own doors. With [Dynamic Maps](https://sp-mod.com/mod/1431/dynamic-maps), the doors of the map you are on are also marked on its map (a door with a key; hideout doors in gold).

| Door | Map | Where |
|---|---|---|
| Skier | Customs | The trailer door just north of the Crackhouse, between the Crackhouse and the Repair Shop |
| Hideout | Customs | The ZB-1011 bunker on the east edge, past Warehouse 7: down the stairs, the steel door at the end of the tunnel |
| Prapor | Streets | First floor of the building by the Beluga restaurant on Primorsky Ave., the grey office door |
| Fence | Streets | The red door at the end of the alley between the Post Office and Cardinal Bank |
| Hideout | Streets | Under Prapor's building, the dark red double door at the end of the covered walkway |
| Ragman | Interchange | Parking garage, section C, below Avokado: the white door in the end wall |
| Jaeger | Woods | The stone bunker in the rock face between the Lumber mill and the Crash Site |
| Mechanic | Factory | Basement, by the caged electrical room: the door in the corridor wall |
| Therapist | Ground Zero | TerraGroup building by the Science Offices, 2nd floor, end of the corridor, next to the big 2 |
| Peacekeeper | Shoreline | The gate of checkpoint 11, at the west end of the map |
| Hideout | Shoreline | The red shed next to checkpoint 11, the steel door on its north side |
| Fence | Lighthouse | The drug lab: the shipping container doors behind the plant tables |

<details>
<summary>Pictures of every door</summary>

![Skier, Customs](docs/doors/01-skier-customs.jpg)
![Hideout, Customs](docs/doors/02-hideout-customs.jpg)
![Prapor, Streets](docs/doors/03-prapor-streets.jpg)
![Fence, Streets](docs/doors/04-fence-streets.jpg)
![Hideout, Streets](docs/doors/05-hideout-streets.jpg)
![Ragman, Interchange](docs/doors/06-ragman-interchange.jpg)
![Jaeger, Woods](docs/doors/07-jaeger-woods.jpg)
![Mechanic, Factory](docs/doors/08-mechanic-factory.jpg)
![Therapist, Ground Zero](docs/doors/09-therapist-ground-zero.jpg)
![Peacekeeper, Shoreline](docs/doors/10-peacekeeper-shoreline.jpg)
![Hideout, Shoreline](docs/doors/11-hideout-shoreline.jpg)
![Fence, Lighthouse](docs/doors/12-fence-lighthouse.jpg)

</details>

---

## Requirements

| Mod | | Version tested |
|---|---|---|
| **SPT** | required | 4.1.6 |
| **[Vagabond](https://sp-mod.com/mod/2642/vagabond)** | required | 0.8.3 |
| [UI Fixes](https://sp-mod.com/mod/1342/ui-fixes) | optional, supported | 6.0.4 |
| [Dynamic Maps](https://sp-mod.com/mod/1431/dynamic-maps) | optional, supported | 1.2.1 |

**Fika** (multiplayer) is **not supported**: the hideout, world clock and save game are single-player.

## Install

1. Install SPT 4.1.6 and Vagabond first, and start the game once with Vagabond.
2. Extract the Zero Menu Time zip **into your SPT folder** (the one with `EscapeFromTarkov.exe`). It adds:
   - `BepInEx/plugins/ZeroMenuTime/` (client)
   - `SPT_Runtime/user/mods/ZeroMenuTime/` (server)
3. Start the SPT server, then the game.
4. Make a new character as usual. It starts with the small starting kit and wakes in your hideout at Customs.
   With a character you already have, choose **Reset** in the main menu for the same start: the game closes; start it again and choose **Continue**.

Reset keeps the old character in `SPT_Runtime/user/profileData/<profile id>/newgame-backups/`. To remove the mod, delete the two folders above.

---

## How it works with other mods

**Vagabond (required).** Zero Menu Time is built on top of Vagabond's open world:

- Traders stand at Vagabond's trader extracts, read from its `trader_locations.json`. Change those and the trader rooms follow.
- After a death, Zero Menu Time puts your save back and wakes you at your bed. This replaces Vagabond's death handling while a save exists; before your first save, Vagabond's own rules apply. The mod's "Ask where to continue" setting covers the case without a save.
- Recommended Vagabond settings (`SPT_Runtime/user/mods/Vagabond/config/vagabond.json`):
  - `"LimitTraderMailAccess": false`, so trader mail can be collected at any trader's table.
  - `"EnableVirtualStashes": false`. Zero Menu Time has one hideout stash of its own; Vagabond's per-location stashes would swap it out depending on where you extracted.

**UI Fixes (supported).** Its in-raid weapon modding rule ("Modify Raid Weapons") is lifted inside the hideout, so the Workbench works as in the old menus. Everywhere else UI Fixes behaves as configured.

**Dynamic Maps (supported).** Door markers are added to its map at run time. Without it, nothing changes.

**Bot mods (SAIN, QuestingBots, Looting Bots, BigBrain, Waypoints, bot placement mods).** Played and tested alongside these. Bots are paused while you are in the hideout and resume where they stood. Over very long raids bots may thin out, because spawn waves are timed for normal raid lengths.

**Things this mod changes on the server.** Mods that change the same things may conflict:

- **Raid time limit:** every map's limit is set very high, because raids have no time limit.
- **Hideout data:** requirements for other stations, trader loyalty and skills are removed; construction time is zero.
- **Trader assortments:** a small stock of field supplies and local maps is added.
- **Quest rewards** handed in during a raid are paid to the trader's table.

**Likely to conflict:**

- Other in-raid trader mods.
- Main menu replacers.
- Mods that rewrite hideout production or hideout requirements.
- Mods that remove or replace the raid timer.
- Mods that change how energy and hydration drain. Zero Menu Time slows hunger and thirst as they run low; switch "Slow hunger and thirst as they empty" off if another mod handles this.

**Other mods in the test setup:** Realistic Insurance, Kirisame Med Timing, Dynamic Item Weights, Pause, HollywoodFX, Hollywood Graphics, ODT Item Info, Restore the Old Number of Medical Uses. No problems seen with these.

---

## Settings

Press **F12** in game and open **r2rka1-ZeroMenuTime**.

| Section | Setting | Default | What it does |
|---|---|---|---|
| General | Show trader figures | on | Stand a figure for each trader |
| General | Show trader markers | on | Show a box at a trader spot until its figure has loaded |
| General | Also use traders.json spots | off | Add hand-placed trader spots on top of Vagabond's |
| General | Capture position key | F10 | Write your position to `captured_positions.txt`, for placing doors and spots |
| Trader rooms | Put traders in rooms | on | Off: traders stand in the open and deliveries go straight into your gear |
| Trader rooms | Furnish rooms from the game | on | Off: plain rooms |
| Trader rooms | Room depth below the door | 300 | How far under the map the rooms are built, in metres |
| Trader models | Use trader models from a bundle | on | Use real trader models if you built a bundle from your own copy of the game |
| No menus | Enabled | on | Skip result screens and continue automatically |
| No menus | Seconds before continuing | 4 | Countdown in the main menu |
| No menus | Stay in menu key | Backspace | Hold during the countdown to stay in the menu |
| Death | Ask where to continue | on | Without a save, ask whether to continue at the hideout or where you died |
| Metabolism | Slow hunger and thirst as they empty | on | Food and water drain more slowly as they run low |
| Pictures | Take a picture key | F11 | Save the view without the interface to `BepInEx/plugins/ZeroMenuTime/screenshots` |
| Pictures | Hide the interface key | F9 | Hide the interface until pressed again |
| Pictures | Hide the interface in pictures | on | Off: pictures include menus and windows |
| Pictures | Picture size | 2 | Saved picture size, in multiples of the screen |
| Testing | Undying, Free hideout, Self-test key, Test harness | off | For development only |

### Placing doors yourself

Every trader door is placed on a real door of the map (see "Where the doors are"). A trader added at a new spot gets a door against the nearest building wall. To move one:

1. Stand with your back to the door you want, and press **F10**.
2. Copy the line from `BepInEx/plugins/ZeroMenuTime/captured_positions.txt` into `doors.json` in the same folder (a list), and fill in the trader's id.

Hideout doors can be moved the same way in `hideouts.json`.

---

## Known limits

- Rooms are bare for about half a minute after a raid starts, while their furniture is fetched. Using a door earlier waits behind a dark screen until it is ready.
- Only the backpack (or the rig) is offered for selling; payment and purchases use everything you carry.
- Barter trades paid with items do not count toward trader loyalty.
- Quests that hand you an item when accepted do not deliver it if accepted in a raid.
- Air filters and the Equipment Stand's outfits are not built.
- Gear lost in a death is still reported to insurance, so insured items can come back twice after the save is restored.
- If the game crashes after a quest hand-in, the reward items you picked up are kept although the quest is not recorded as done.
- Trader figures are stand-ins dressed in the game's own clothing.
- Post-raid healing is skipped along with the other result screens.

---

## Building from source

Needs the .NET 10 SDK and an SPT 4.1.6 install to reference.

```
dotnet build ZeroMenuTime/ZeroMenuTime.csproj -c Release -p:TarkovDir=C:\path\to\SPT\
dotnet build ZeroMenuTime.Server/ZeroMenuTime.Server.csproj -c Release -p:TarkovDir=C:\path\to\SPT\
```

- The server project references `Vagabond.Common.dll` from the Vagabond server mod folder.
- The fallback room bundle is built with Unity 2022.3.43f1 from `Unity/TraderRooms`; see its README. It is made only from simple shapes and generated textures, with no game assets.
- `pack.ps1` builds everything and writes the release zip to `dist/`, laid out to extract into the SPT folder, with this README and the licence.

The development test harness (Testing → Test harness) runs commands from `BepInEx/plugins/ZeroMenuTime/test/commands.txt` in a running raid; see `ZeroMenuTime/TestHarness.cs` for the commands.

## Credits

- Built on [Vagabond](https://github.com/MrEliasen/SPT-Vagabond) by Oogabooga.
- The in-raid trading idea follows Lillian's In-Raid Traders for SPT 3.11; this is a separate implementation.
- Written with the help of an AI coding assistant (Claude), then played and tested by hand.

## Licence

[MIT](LICENSE)
