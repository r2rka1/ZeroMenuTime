# Trader rooms

The Unity project that builds the `traderrooms` asset bundle: the room each trader stands in and
the door that leads to it.

Everything in the bundle is made by `Assets/Editor/RoomBuilder.cs`: the geometry, the textures
(generated from noise) and the materials. Nothing from the game is used, so the bundle can be
shipped with the mod.

## Building

Needs Unity 2022.3.43f1, the version the game runs on, with a licence activated.

    Unity.exe -batchmode -nographics -quit -projectPath Unity/TraderRooms -executeMethod RoomBuilder.BuildAll

or open the project and choose **Trader Rooms > Build everything**. The bundle is written to
`Unity/TraderRooms/Build/traderrooms`; copy it to `BepInEx/plugins/TradersInRaid/`.

## What the mod expects

A prefab named `TraderRoom_Base` with these children, found by name:

| Name | Meaning |
| --- | --- |
| `Entry` | Where the player stands after coming in; its forward is the way they face |
| `ExitDoor` | The door back out; needs a collider to look at |
| `TraderStand` | The floor under the trader; its forward is the way the trader faces |
| `TableSlot_00` ... | Places on the table where items are dropped |
| `IndoorBounds` | A unit cube scaled to enclose the room; marks it as indoors |

and a prefab named `TraderDoor` for the door on the map. Solid surfaces only need ordinary
colliders; the mod puts them on the game's collision layers when it loads the room.
