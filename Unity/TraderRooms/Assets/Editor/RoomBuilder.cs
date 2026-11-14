using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the trader's room and its door from nothing but code, and packs them into the
/// asset bundle the mod loads. Run from the menu or with -executeMethod RoomBuilder.BuildAll.
/// </summary>
public static class RoomBuilder
{
    private const string Root = "Assets/TraderRooms";
    private const string BundleName = "traderrooms";

    // Inside measurements, in metres. The origin is the middle of the floor; the player comes in at -z.
    private const float Width = 6f, Height = 2.8f, Length = 7f, Wall = 0.3f;

    private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
    private static int _meshCount;

    [MenuItem("Trader Rooms/Build everything")]
    public static void BuildAll()
    {
        try
        {
            if (AssetDatabase.IsValidFolder(Root))
            {
                AssetDatabase.DeleteAsset(Root);
            }
            foreach (var folder in new[] { "", "/Textures", "/Materials", "/Meshes" })
            {
                Directory.CreateDirectory(Root + folder);
            }
            AssetDatabase.Refresh();
            Materials.Clear();
            _meshCount = 0;

            MakeMaterial("plaster", Plaster, 0.08f);
            MakeMaterial("concrete", Concrete, 0.12f);
            MakeMaterial("planks", Planks, 0.18f);
            MakeMaterial("wood", Wood, 0.22f);
            MakeMaterial("darkwood", DarkWood, 0.25f);
            MakeMaterial("metal", Metal, 0.45f);
            MakeMaterial("crate", Crate, 0.15f);
            MakeMaterial("shade", Shade, 0.3f);
            MakeMaterial("tiles", Tiles, 0.55f);
            MakeMaterial("armypaint", ArmyPaint, 0.15f);
            MakeMaterial("corrugated", Corrugated, 0.4f);
            MakeMaterial("brick", Brick, 0.08f);
            MakeMaterial("logs", Logs, 0.12f);
            MakeMaterial("rug", Rug, 0.03f);
            MakeMaterial("white", White, 0.5f);
            MakeMaterial("rubber", Rubber, 0.1f);
            MakeMaterial("cloth red", (u, v) => Cloth(u, v, new Color(0.45f, 0.10f, 0.09f)), 0.03f);
            MakeMaterial("cloth blue", (u, v) => Cloth(u, v, new Color(0.12f, 0.20f, 0.38f)), 0.03f);
            MakeMaterial("cloth grey", (u, v) => Cloth(u, v, new Color(0.30f, 0.30f, 0.31f)), 0.03f);
            MakeMaterial("cloth sand", (u, v) => Cloth(u, v, new Color(0.55f, 0.47f, 0.33f)), 0.03f);
            MakeMaterial("un blue", (u, v) => Cloth(u, v, new Color(0.22f, 0.45f, 0.70f)), 0.25f);

            foreach (var theme in Themes())
            {
                SavePrefab(BuildRoom(theme), "TraderRoom_" + theme.Name);
            }
            SavePrefab(BuildDoor(), "TraderDoor");
            AssetDatabase.SaveAssets();

            string output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Build");
            Directory.CreateDirectory(output);
            var manifest = BuildPipeline.BuildAssetBundles(output, BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
            {
                throw new System.Exception("BuildAssetBundles returned nothing");
            }
            Debug.Log("[TraderRooms] Bundle written to " + Path.Combine(output, BundleName));
        }
        catch (System.Exception e)
        {
            Debug.LogError("[TraderRooms] Build failed: " + e);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }

    // ---------------------------------------------------------------- the room

    /// <summary>What makes one trader's room different from another's.</summary>
    private class Theme
    {
        public string Name, Floor, Walls, Ceiling, Trim, TableTop;
        public Color Light = new Color(1f, 0.90f, 0.74f);
        public System.Action<GameObject> Furnish;
    }

    private const float HalfW = Width / 2f, HalfL = Length / 2f;

    private static List<Theme> Themes()
    {
        return new List<Theme>
        {
            // The plain shopkeeper's room; also used for any trader without a room of their own.
            new Theme { Name = "Base", Floor = "planks", Walls = "plaster", Ceiling = "concrete", Trim = "darkwood", TableTop = "wood", Furnish = FurnishShop },
            // An army storeroom.
            new Theme { Name = "Prapor", Floor = "concrete", Walls = "armypaint", Ceiling = "concrete", Trim = "metal", TableTop = "metal", Furnish = FurnishArmoury },
            // A clinic: tiles, white cabinets, a couch.
            new Theme { Name = "Therapist", Floor = "tiles", Walls = "tiles", Ceiling = "white", Trim = "white", TableTop = "white", Light = new Color(0.92f, 0.97f, 1f), Furnish = FurnishClinic },
            // A fixer's back office.
            new Theme { Name = "Skier", Floor = "planks", Walls = "darkwood", Ceiling = "plaster", Trim = "darkwood", TableTop = "darkwood", Light = new Color(1f, 0.82f, 0.60f), Furnish = FurnishOffice },
            // A steel container post.
            new Theme { Name = "Peacekeeper", Floor = "metal", Walls = "corrugated", Ceiling = "corrugated", Trim = "metal", TableTop = "metal", Light = new Color(0.95f, 0.97f, 1f), Furnish = FurnishPost },
            // A workshop.
            new Theme { Name = "Mechanic", Floor = "concrete", Walls = "brick", Ceiling = "concrete", Trim = "metal", TableTop = "wood", Furnish = FurnishWorkshop },
            // A clothes stockroom.
            new Theme { Name = "Ragman", Floor = "planks", Walls = "plaster", Ceiling = "plaster", Trim = "wood", TableTop = "wood", Furnish = FurnishStockroom },
            // A hunter's cabin.
            new Theme { Name = "Jaeger", Floor = "planks", Walls = "logs", Ceiling = "planks", Trim = "darkwood", TableTop = "wood", Light = new Color(1f, 0.78f, 0.52f), Furnish = FurnishCabin },
            // A lock-up full of whatever turned up.
            new Theme { Name = "Fence", Floor = "concrete", Walls = "brick", Ceiling = "corrugated", Trim = "metal", TableTop = "crate", Light = new Color(1f, 0.86f, 0.62f), Furnish = FurnishLockup },
        };
    }

    private static GameObject BuildRoom(Theme theme)
    {
        var room = new GameObject("TraderRoom_" + theme.Name);
        float halfW = Width / 2f, halfL = Length / 2f;

        // Shell.
        Box(room, "floor", new Vector3(0f, -Wall / 2f, 0f), new Vector3(Width + 2 * Wall, Wall, Length + 2 * Wall), theme.Floor);
        Box(room, "ceiling", new Vector3(0f, Height + Wall / 2f, 0f), new Vector3(Width + 2 * Wall, Wall, Length + 2 * Wall), theme.Ceiling);
        Box(room, "wall west", new Vector3(-(halfW + Wall / 2f), Height / 2f, 0f), new Vector3(Wall, Height, Length + 2 * Wall), theme.Walls);
        Box(room, "wall east", new Vector3(halfW + Wall / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Length + 2 * Wall), theme.Walls);
        Box(room, "wall south", new Vector3(0f, Height / 2f, -(halfL + Wall / 2f)), new Vector3(Width, Height, Wall), theme.Walls);
        Box(room, "wall north", new Vector3(0f, Height / 2f, halfL + Wall / 2f), new Vector3(Width, Height, Wall), theme.Walls);

        // Skirting boards and ceiling beams, for something to catch the light.
        const float skirt = 0.12f, skirtDepth = 0.03f;
        Box(room, "skirting west", new Vector3(-halfW + skirtDepth / 2f, skirt / 2f, 0f), new Vector3(skirtDepth, skirt, Length), theme.Trim, false);
        Box(room, "skirting east", new Vector3(halfW - skirtDepth / 2f, skirt / 2f, 0f), new Vector3(skirtDepth, skirt, Length), theme.Trim, false);
        Box(room, "skirting north", new Vector3(0f, skirt / 2f, halfL - skirtDepth / 2f), new Vector3(Width, skirt, skirtDepth), theme.Trim, false);
        for (int i = -1; i <= 1; i++)
        {
            Box(room, "beam " + (i + 1), new Vector3(0f, Height - 0.09f, i * 2.2f), new Vector3(Width, 0.18f, 0.16f), theme.Trim, false);
        }

        // The way out: a frame and a door leaf set into the south wall.
        const float doorW = 0.95f, doorH = 2.05f, frame = 0.08f;
        float doorZ = -halfL + 0.03f;
        Box(room, "door frame left", new Vector3(-(doorW + frame) / 2f, doorH / 2f, doorZ), new Vector3(frame, doorH + frame, 0.1f), "darkwood", false);
        Box(room, "door frame right", new Vector3((doorW + frame) / 2f, doorH / 2f, doorZ), new Vector3(frame, doorH + frame, 0.1f), "darkwood", false);
        Box(room, "door frame top", new Vector3(0f, doorH + frame / 2f, doorZ), new Vector3(doorW + 2 * frame, frame, 0.1f), "darkwood", false);
        var exit = Box(room, "ExitDoor", new Vector3(0f, doorH / 2f, doorZ), new Vector3(doorW, doorH, 0.05f), "wood");
        Box(exit, "handle", new Vector3(0.36f, 0f, 0.05f), new Vector3(0.12f, 0.03f, 0.04f), "metal", false);

        // The table the rewards land on, against the east wall.
        const float tableW = 0.8f, tableL = 1.6f, tableH = 0.8f, top = 0.05f, leg = 0.07f;
        var tableCentre = new Vector3(halfW - 0.55f, 0f, 0.3f);
        var table = new GameObject("Table");
        table.transform.SetParent(room.transform, false);
        table.transform.localPosition = tableCentre;
        Box(table, "top", new Vector3(0f, tableH - top / 2f, 0f), new Vector3(tableW, top, tableL), theme.TableTop);
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Box(table, "leg", new Vector3(sx * (tableW / 2f - leg), (tableH - top) / 2f, sz * (tableL / 2f - leg)),
                    new Vector3(leg, tableH - top, leg), "darkwood");
            }
        }
        Box(table, "rail", new Vector3(0f, 0.25f, 0f), new Vector3(0.04f, 0.04f, tableL - 2 * leg), "darkwood", false);
        int slot = 0;
        for (int row = 0; row < 5; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                Anchor(table, "TableSlot_" + slot.ToString("00"),
                    new Vector3((column - 0.5f) * 0.32f, tableH + 0.02f, (row - 2f) * 0.3f), 0f);
                slot++;
            }
        }

        theme.Furnish(room);

        // Two hanging lamps: one over the middle, one over the table.
        Lamp(room, new Vector3(0f, Height, 0.4f), 1.5f, 11f, theme.Light);
        Lamp(room, new Vector3(tableCentre.x - 0.1f, Height, tableCentre.z), 0.9f, 6f, theme.Light);

        // The named places the mod looks for.
        Anchor(room, "Entry", new Vector3(0f, 0.05f, -halfL + 1.2f), 0f);
        Anchor(room, "TraderStand", new Vector3(0f, 0f, halfL - 1.4f), 180f);
        var indoor = Anchor(room, "IndoorBounds", new Vector3(0f, Height / 2f, 0f), 0f);
        indoor.transform.localScale = new Vector3(Width + 2 * Wall, Height + 2 * Wall, Length + 2 * Wall);
        return room;
    }

    // ---------------------------------------------------------------- furniture
    // The table stands against the east wall at z 0.3 and the trader at z HalfL - 1.4, facing the
    // door. Keep the middle of the room and the way from the door to both of them clear.

    /// <summary>Shelves along the back wall with boxes on them.</summary>
    private static void Shelves(GameObject room, string board, string upright, string[] boxMaterials)
    {
        float shelfZ = HalfL - 0.2f;
        for (int level = 0; level < 4; level++)
        {
            Box(room, "shelf " + level, new Vector3(0f, 0.35f + level * 0.55f, shelfZ), new Vector3(3.2f, 0.04f, 0.38f), board, level == 0);
        }
        for (int post = -1; post <= 1; post++)
        {
            Box(room, "shelf upright " + (post + 1), new Vector3(post * 1.6f, 1.05f, shelfZ), new Vector3(0.05f, 2.1f, 0.38f), upright, false);
        }
        var boxes = new[]
        {
            new Vector4(-1.2f, 0, 0.45f, 0.30f), new Vector4(-0.55f, 0, 0.35f, 0.22f), new Vector4(0.6f, 0, 0.50f, 0.34f),
            new Vector4(-0.9f, 1, 0.40f, 0.26f), new Vector4(0.3f, 1, 0.30f, 0.20f), new Vector4(1.1f, 1, 0.42f, 0.30f),
            new Vector4(-0.3f, 2, 0.48f, 0.28f), new Vector4(0.95f, 2, 0.34f, 0.24f), new Vector4(-1.25f, 3, 0.36f, 0.22f),
        };
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i];
            float shelfTop = 0.37f + b.y * 0.55f;
            Box(room, "shelf box", new Vector3(b.x, shelfTop + b.w / 2f, shelfZ), new Vector3(b.z, b.w, 0.30f), boxMaterials[i % boxMaterials.Length], false);
        }
    }

    /// <summary>A stack of boxes on the floor, each a little askew.</summary>
    private static void Stack(GameObject room, Vector3 floorPoint, string material, params Vector3[] sizes)
    {
        float y = 0f;
        for (int i = 0; i < sizes.Length; i++)
        {
            var box = Box(room, "stack " + i, floorPoint + new Vector3(0f, y + sizes[i].y / 2f, 0f), sizes[i], material, i == 0);
            box.transform.localRotation = Quaternion.Euler(0f, (i * 37 % 23) - 11f, 0f);
            y += sizes[i].y;
        }
    }

    private static void Desk(GameObject room, string top, string sides)
    {
        Box(room, "desk top", new Vector3(-HalfW + 0.45f, 0.75f, 1.6f), new Vector3(0.7f, 0.05f, 1.5f), top);
        Box(room, "desk side a", new Vector3(-HalfW + 0.45f, 0.36f, 0.9f), new Vector3(0.66f, 0.72f, 0.05f), sides);
        Box(room, "desk side b", new Vector3(-HalfW + 0.45f, 0.36f, 2.3f), new Vector3(0.66f, 0.72f, 0.05f), sides);
    }

    private static void FurnishShop(GameObject room)
    {
        Shelves(room, "wood", "darkwood", new[] { "crate", "metal" });
        Desk(room, "wood", "darkwood");
        Box(room, "ledger", new Vector3(-HalfW + 0.45f, 0.79f, 1.5f), new Vector3(0.24f, 0.03f, 0.32f), "darkwood", false);
        Box(room, "strongbox", new Vector3(-HalfW + 0.42f, 0.90f, 2.0f), new Vector3(0.36f, 0.24f, 0.28f), "metal", false);
        Stack(room, new Vector3(-HalfW + 0.5f, 0f, -HalfL + 0.6f), "crate", new Vector3(0.8f, 0.6f, 0.8f), new Vector3(0.7f, 0.5f, 0.7f));
        Box(room, "locker", new Vector3(HalfW - 0.3f, 0.95f, -HalfL + 0.7f), new Vector3(0.5f, 1.9f, 0.9f), "metal");
    }

    private static void FurnishArmoury(GameObject room)
    {
        Shelves(room, "metal", "metal", new[] { "armypaint", "crate", "armypaint" });
        // Long ammunition crates along the west wall, and a rack of rifles standing against it.
        for (int i = 0; i < 3; i++)
        {
            Stack(room, new Vector3(-HalfW + 0.45f, 0f, -0.2f + i * 1.15f), "armypaint",
                new Vector3(0.6f, 0.32f, 1.05f), new Vector3(0.6f, 0.32f, 1.05f), i == 1 ? new Vector3(0.6f, 0.32f, 1.05f) : new Vector3(0.5f, 0.2f, 0.6f));
        }
        Box(room, "rack base", new Vector3(HalfW - 0.2f, 0.12f, -2.2f), new Vector3(0.3f, 0.24f, 1.6f), "darkwood");
        Box(room, "rack bar", new Vector3(HalfW - 0.1f, 1.05f, -2.2f), new Vector3(0.06f, 0.06f, 1.6f), "darkwood", false);
        for (int i = 0; i < 6; i++)
        {
            var rifle = Box(room, "rifle " + i, new Vector3(HalfW - 0.17f, 0.78f, -2.85f + i * 0.26f), new Vector3(0.05f, 1.1f, 0.07f), i % 2 == 0 ? "darkwood" : "metal", false);
            rifle.transform.localRotation = Quaternion.Euler(0f, 0f, -7f);
        }
        Stack(room, new Vector3(-HalfW + 0.5f, 0f, -HalfL + 0.6f), "crate", new Vector3(0.8f, 0.55f, 0.8f), new Vector3(0.75f, 0.5f, 0.75f), new Vector3(0.5f, 0.3f, 0.5f));
    }

    private static void FurnishClinic(GameObject room)
    {
        // Glass-fronted cabinets behind, an examination couch, a screen and a trolley.
        for (int i = -1; i <= 1; i++)
        {
            Box(room, "cabinet " + (i + 1), new Vector3(i * 1.05f, 1.0f, HalfL - 0.22f), new Vector3(1.0f, 2.0f, 0.4f), "white");
            Box(room, "cabinet band " + (i + 1), new Vector3(i * 1.05f, 0.95f, HalfL - 0.43f), new Vector3(0.96f, 0.05f, 0.02f), "metal", false);
            Box(room, "cabinet cross " + (i + 1), new Vector3(i * 1.05f, 1.55f, HalfL - 0.43f), new Vector3(0.22f, 0.07f, 0.02f), "cloth red", false);
            Box(room, "cabinet cross v " + (i + 1), new Vector3(i * 1.05f, 1.55f, HalfL - 0.432f), new Vector3(0.07f, 0.22f, 0.02f), "cloth red", false);
        }
        Box(room, "couch", new Vector3(-HalfW + 0.5f, 0.55f, 0.6f), new Vector3(0.75f, 0.12f, 1.9f), "cloth grey");
        Box(room, "couch frame", new Vector3(-HalfW + 0.5f, 0.24f, 0.6f), new Vector3(0.65f, 0.48f, 1.7f), "white");
        Box(room, "pillow", new Vector3(-HalfW + 0.5f, 0.66f, 1.35f), new Vector3(0.5f, 0.1f, 0.3f), "white", false);
        Box(room, "screen", new Vector3(-HalfW + 1.2f, 0.9f, -0.75f), new Vector3(0.04f, 1.7f, 1.1f), "cloth sand");
        Box(room, "trolley top", new Vector3(-HalfW + 1.3f, 0.8f, 2.2f), new Vector3(0.45f, 0.03f, 0.7f), "metal");
        Box(room, "trolley shelf", new Vector3(-HalfW + 1.3f, 0.35f, 2.2f), new Vector3(0.45f, 0.03f, 0.7f), "metal", false);
        Box(room, "tray", new Vector3(-HalfW + 1.3f, 0.84f, 2.1f), new Vector3(0.3f, 0.04f, 0.22f), "white", false);
        Box(room, "sink", new Vector3(HalfW - 0.25f, 0.8f, -2.3f), new Vector3(0.45f, 0.2f, 0.6f), "white");
        Box(room, "medical fridge", new Vector3(HalfW - 0.33f, 0.85f, 2.6f), new Vector3(0.6f, 1.7f, 0.6f), "white");
    }

    private static void FurnishOffice(GameObject room)
    {
        Box(room, "rug", new Vector3(0f, 0.01f, 0.2f), new Vector3(2.6f, 0.02f, 3.6f), "rug", false);
        // A heavy desk behind the trader, a sofa, a safe and a drinks cabinet.
        Box(room, "desk top", new Vector3(0f, 0.78f, HalfL - 0.55f), new Vector3(2.2f, 0.07f, 0.9f), "darkwood");
        Box(room, "desk front", new Vector3(0f, 0.38f, HalfL - 0.95f), new Vector3(2.1f, 0.74f, 0.05f), "darkwood");
        Box(room, "desk lamp", new Vector3(0.8f, 0.95f, HalfL - 0.5f), new Vector3(0.14f, 0.28f, 0.14f), "shade", false);
        Box(room, "desk papers", new Vector3(-0.5f, 0.83f, HalfL - 0.55f), new Vector3(0.3f, 0.02f, 0.4f), "white", false);
        Box(room, "sofa seat", new Vector3(-HalfW + 0.5f, 0.3f, 0.2f), new Vector3(0.85f, 0.4f, 2.0f), "cloth red");
        Box(room, "sofa back", new Vector3(-HalfW + 0.16f, 0.65f, 0.2f), new Vector3(0.22f, 0.5f, 2.0f), "cloth red", false);
        Box(room, "sofa arm a", new Vector3(-HalfW + 0.5f, 0.5f, -0.9f), new Vector3(0.85f, 0.25f, 0.2f), "cloth red", false);
        Box(room, "sofa arm b", new Vector3(-HalfW + 0.5f, 0.5f, 1.3f), new Vector3(0.85f, 0.25f, 0.2f), "cloth red", false);
        Box(room, "safe", new Vector3(HalfW - 0.4f, 0.6f, 2.6f), new Vector3(0.7f, 1.2f, 0.7f), "metal");
        Box(room, "safe dial", new Vector3(HalfW - 0.4f, 0.7f, 2.24f), new Vector3(0.12f, 0.12f, 0.03f), "white", false);
        Box(room, "cabinet", new Vector3(HalfW - 0.25f, 0.55f, -2.3f), new Vector3(0.45f, 1.1f, 1.1f), "darkwood");
        for (int i = 0; i < 4; i++)
        {
            Box(room, "bottle " + i, new Vector3(HalfW - 0.28f, 1.24f, -2.65f + i * 0.2f), new Vector3(0.07f, 0.28f, 0.07f), i % 2 == 0 ? "shade" : "cloth sand", false);
        }
        Box(room, "picture", new Vector3(-HalfW + 0.02f, 1.6f, 0.2f), new Vector3(0.03f, 0.6f, 0.9f), "cloth blue", false);
    }

    private static void FurnishPost(GameObject room)
    {
        // Stencilled supply cases, a radio desk, a cot.
        Shelves(room, "metal", "metal", new[] { "un blue", "white", "metal" });
        Desk(room, "metal", "metal");
        Box(room, "radio", new Vector3(-HalfW + 0.42f, 0.95f, 1.9f), new Vector3(0.4f, 0.32f, 0.5f), "armypaint", false);
        Box(room, "radio aerial", new Vector3(-HalfW + 0.3f, 1.5f, 2.1f), new Vector3(0.012f, 0.8f, 0.012f), "metal", false);
        Box(room, "map board", new Vector3(-HalfW + 0.03f, 1.6f, 1.6f), new Vector3(0.03f, 0.8f, 1.2f), "white", false);
        for (int i = 0; i < 2; i++)
        {
            Stack(room, new Vector3(-HalfW + 0.5f, 0f, -HalfL + 0.6f + i * 1.0f), "un blue", new Vector3(0.8f, 0.45f, 0.6f), new Vector3(0.8f, 0.45f, 0.6f), new Vector3(0.6f, 0.3f, 0.5f));
        }
        Box(room, "cot", new Vector3(HalfW - 0.45f, 0.35f, -2.3f), new Vector3(0.8f, 0.1f, 1.9f), "cloth sand");
        Box(room, "cot frame", new Vector3(HalfW - 0.45f, 0.15f, -2.3f), new Vector3(0.74f, 0.3f, 1.8f), "metal");
        Box(room, "water cans", new Vector3(HalfW - 0.3f, 0.25f, 2.7f), new Vector3(0.35f, 0.5f, 0.5f), "un blue");
    }

    private static void FurnishWorkshop(GameObject room)
    {
        // A workbench with a vice and a tool board along the west wall, parts shelves behind.
        Shelves(room, "wood", "metal", new[] { "metal", "crate", "rubber" });
        Box(room, "bench top", new Vector3(-HalfW + 0.42f, 0.9f, 0.8f), new Vector3(0.75f, 0.07f, 3.0f), "wood");
        for (int i = 0; i < 3; i++)
        {
            Box(room, "bench leg " + i, new Vector3(-HalfW + 0.42f, 0.43f, -0.6f + i * 1.4f), new Vector3(0.7f, 0.86f, 0.07f), "metal");
        }
        Box(room, "bench shelf", new Vector3(-HalfW + 0.42f, 0.3f, 0.8f), new Vector3(0.65f, 0.04f, 2.8f), "wood", false);
        Box(room, "vice", new Vector3(-HalfW + 0.55f, 1.02f, -0.3f), new Vector3(0.18f, 0.18f, 0.3f), "metal", false);
        Box(room, "toolbox", new Vector3(-HalfW + 0.4f, 1.04f, 1.8f), new Vector3(0.3f, 0.2f, 0.55f), "cloth red", false);
        Box(room, "tool board", new Vector3(-HalfW + 0.03f, 1.75f, 0.8f), new Vector3(0.03f, 0.9f, 2.6f), "wood", false);
        for (int i = 0; i < 7; i++)
        {
            Box(room, "tool " + i, new Vector3(-HalfW + 0.06f, 1.7f + (i % 3) * 0.12f, -0.3f + i * 0.36f), new Vector3(0.02f, 0.3f + (i % 2) * 0.15f, 0.05f), "metal", false);
        }
        for (int i = 0; i < 3; i++)
        {
            Box(room, "tyre " + i, new Vector3(-HalfW + 0.45f, 0.11f + i * 0.22f, -HalfL + 0.6f), new Vector3(0.75f, 0.2f, 0.75f), "rubber", i == 0)
                .transform.localRotation = Quaternion.Euler(0f, i * 20f, 0f);
        }
        Box(room, "drum", new Vector3(HalfW - 0.4f, 0.45f, 2.7f), new Vector3(0.55f, 0.9f, 0.55f), "cloth blue");
        Box(room, "gas bottle", new Vector3(HalfW - 0.25f, 0.6f, -2.6f), new Vector3(0.26f, 1.2f, 0.26f), "cloth red");
    }

    private static void FurnishStockroom(GameObject room)
    {
        // Folded stock on shelves, two rails of hanging clothes, shoe boxes.
        string[] cloth = { "cloth red", "cloth blue", "cloth grey", "cloth sand" };
        float shelfZ = HalfL - 0.2f;
        for (int level = 0; level < 4; level++)
        {
            Box(room, "shelf " + level, new Vector3(0f, 0.35f + level * 0.55f, shelfZ), new Vector3(3.2f, 0.04f, 0.38f), "wood", level == 0);
            for (int pile = 0; pile < 6; pile++)
            {
                float height = 0.12f + ((level * 5 + pile * 3) % 4) * 0.07f;
                Box(room, "folded", new Vector3(-1.3f + pile * 0.52f, 0.37f + level * 0.55f + height / 2f, shelfZ), new Vector3(0.4f, height, 0.3f), cloth[(level + pile) % 4], false);
            }
        }
        for (int post = -1; post <= 1; post++)
        {
            Box(room, "shelf upright " + (post + 1), new Vector3(post * 1.6f, 1.05f, shelfZ), new Vector3(0.05f, 2.1f, 0.38f), "darkwood", false);
        }
        for (int rail = 0; rail < 2; rail++)
        {
            float z = -0.9f + rail * 2.1f;
            Box(room, "rail " + rail, new Vector3(-HalfW + 0.5f, 1.7f, z), new Vector3(0.04f, 0.04f, 1.7f), "metal", false);
            Box(room, "rail post a " + rail, new Vector3(-HalfW + 0.5f, 0.85f, z - 0.85f), new Vector3(0.04f, 1.7f, 0.04f), "metal");
            Box(room, "rail post b " + rail, new Vector3(-HalfW + 0.5f, 0.85f, z + 0.85f), new Vector3(0.04f, 1.7f, 0.04f), "metal");
            for (int i = 0; i < 8; i++)
            {
                float length = 0.7f + ((i * 7 + rail) % 3) * 0.2f;
                Box(room, "garment", new Vector3(-HalfW + 0.5f, 1.66f - length / 2f, z - 0.7f + i * 0.2f), new Vector3(0.5f, length, 0.06f), cloth[(i + rail) % 4], false);
            }
        }
        Stack(room, new Vector3(HalfW - 0.35f, 0f, -2.4f), "white", new Vector3(0.5f, 0.2f, 0.9f), new Vector3(0.5f, 0.2f, 0.85f), new Vector3(0.45f, 0.2f, 0.7f), new Vector3(0.4f, 0.18f, 0.5f));
        Box(room, "mirror", new Vector3(HalfW - 0.03f, 1.2f, 2.5f), new Vector3(0.03f, 1.6f, 0.6f), "white", false);
    }

    private static void FurnishCabin(GameObject room)
    {
        // A stove with its pipe, firewood, a bunk and a chest.
        Box(room, "stove", new Vector3(-HalfW + 0.5f, 0.45f, 2.4f), new Vector3(0.6f, 0.9f, 0.7f), "metal");
        Box(room, "stove pipe", new Vector3(-HalfW + 0.5f, 1.85f, 2.5f), new Vector3(0.16f, 1.9f, 0.16f), "metal", false);
        Box(room, "stove door", new Vector3(-HalfW + 0.81f, 0.4f, 2.4f), new Vector3(0.02f, 0.3f, 0.35f), "cloth red", false);
        for (int row = 0; row < 4; row++)
        {
            for (int log = 0; log < 5; log++)
            {
                Box(room, "firewood", new Vector3(-HalfW + 0.35f, 0.07f + row * 0.13f, 0.9f + log * 0.15f + (row % 2) * 0.07f), new Vector3(0.5f, 0.12f, 0.12f), "logs", row == 0 && log == 0);
            }
        }
        Box(room, "bunk", new Vector3(-HalfW + 0.5f, 0.45f, -1.6f), new Vector3(0.85f, 0.12f, 1.9f), "cloth sand");
        Box(room, "bunk frame", new Vector3(-HalfW + 0.5f, 0.2f, -1.6f), new Vector3(0.9f, 0.4f, 1.95f), "darkwood");
        Box(room, "blanket", new Vector3(-HalfW + 0.5f, 0.53f, -1.9f), new Vector3(0.87f, 0.05f, 1.1f), "cloth red", false);
        Shelves(room, "wood", "darkwood", new[] { "crate", "darkwood", "cloth sand" });
        Box(room, "chest", new Vector3(HalfW - 0.4f, 0.3f, 2.6f), new Vector3(0.6f, 0.6f, 0.9f), "darkwood");
        Box(room, "chest band", new Vector3(HalfW - 0.4f, 0.45f, 2.6f), new Vector3(0.62f, 0.05f, 0.92f), "metal", false);
        Box(room, "hide", new Vector3(HalfW - 0.02f, 1.6f, -2.2f), new Vector3(0.02f, 0.9f, 1.3f), "cloth sand", false);
        Box(room, "rifle pegs", new Vector3(0f, 2.35f, HalfL - 0.04f), new Vector3(1.3f, 0.06f, 0.07f), "darkwood", false);
    }

    private static void FurnishLockup(GameObject room)
    {
        // Mismatched shelving and heaps of boxes; nothing sits square.
        Shelves(room, "metal", "metal", new[] { "crate", "cloth blue", "metal", "cloth red", "white" });
        Stack(room, new Vector3(-HalfW + 0.5f, 0f, -HalfL + 0.6f), "crate", new Vector3(0.9f, 0.5f, 0.8f), new Vector3(0.7f, 0.45f, 0.7f), new Vector3(0.6f, 0.3f, 0.45f), new Vector3(0.35f, 0.25f, 0.3f));
        Stack(room, new Vector3(-HalfW + 0.5f, 0f, 0.3f), "crate", new Vector3(0.8f, 0.7f, 1.0f), new Vector3(0.6f, 0.35f, 0.6f));
        Stack(room, new Vector3(-HalfW + 0.45f, 0f, 1.7f), "metal", new Vector3(0.6f, 0.9f, 0.6f), new Vector3(0.5f, 0.3f, 0.4f));
        Stack(room, new Vector3(HalfW - 0.4f, 0f, 2.6f), "crate", new Vector3(0.7f, 0.4f, 0.7f), new Vector3(0.65f, 0.4f, 0.6f), new Vector3(0.5f, 0.4f, 0.5f));
        Box(room, "television", new Vector3(-HalfW + 0.5f, 1.3f, 0.3f), new Vector3(0.45f, 0.4f, 0.55f), "rubber", false);
        Box(room, "rolled carpet", new Vector3(HalfW - 0.2f, 0.9f, -2.7f), new Vector3(0.22f, 1.8f, 0.22f), "rug", false).transform.localRotation = Quaternion.Euler(8f, 0f, 6f);
        Box(room, "bike frame", new Vector3(HalfW - 0.12f, 0.55f, -2.0f), new Vector3(0.06f, 0.9f, 1.4f), "metal", false);
        Box(room, "tarp heap", new Vector3(-1.9f, 0.2f, -2.6f), new Vector3(1.0f, 0.4f, 0.8f), "cloth blue").transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
    }

    private static void Lamp(GameObject room, Vector3 ceilingPoint, float intensity, float range, Color colour)
    {
        const float drop = 0.35f;
        Box(room, "lamp cord", ceilingPoint - new Vector3(0f, drop / 2f, 0f), new Vector3(0.015f, drop, 0.015f), "metal", false);
        Box(room, "lamp shade", ceilingPoint - new Vector3(0f, drop + 0.06f, 0f), new Vector3(0.30f, 0.09f, 0.30f), "shade", false);
        var bulb = new GameObject("lamp light");
        bulb.transform.SetParent(room.transform, false);
        bulb.transform.localPosition = ceilingPoint - new Vector3(0f, drop + 0.2f, 0f);
        var light = bulb.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = colour;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.7f;
    }

    // ---------------------------------------------------------------- the door on the map

    private static GameObject BuildDoor()
    {
        // The origin is on the ground in the middle of the doorway; the door faces +z.
        const float doorW = 0.95f, doorH = 2.05f, frame = 0.09f;
        var door = new GameObject("TraderDoor");
        Box(door, "frame left", new Vector3(-(doorW + frame) / 2f, (doorH + frame) / 2f, 0f), new Vector3(frame, doorH + frame, 0.16f), "darkwood");
        Box(door, "frame right", new Vector3((doorW + frame) / 2f, (doorH + frame) / 2f, 0f), new Vector3(frame, doorH + frame, 0.16f), "darkwood");
        Box(door, "frame top", new Vector3(0f, doorH + frame / 2f, 0f), new Vector3(doorW + 2 * frame, frame, 0.16f), "darkwood");
        var leaf = Box(door, "Leaf", new Vector3(0f, doorH / 2f, 0f), new Vector3(doorW, doorH, 0.06f), "wood");
        Box(leaf, "handle front", new Vector3(0.36f, 0f, 0.055f), new Vector3(0.12f, 0.03f, 0.05f), "metal", false);
        Box(leaf, "handle back", new Vector3(0.36f, 0f, -0.055f), new Vector3(0.12f, 0.03f, 0.05f), "metal", false);
        Box(leaf, "plate", new Vector3(0f, 0.55f, 0.035f), new Vector3(0.4f, 0.14f, 0.01f), "metal", false);
        return door;
    }

    // ---------------------------------------------------------------- pieces

    private static GameObject Anchor(GameObject parent, string name, Vector3 localPosition, float yaw)
    {
        var anchor = new GameObject(name);
        anchor.transform.SetParent(parent.transform, false);
        anchor.transform.localPosition = localPosition;
        anchor.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return anchor;
    }

    /// <summary>A box with its own mesh, so the texture keeps the same size on every piece.</summary>
    private static GameObject Box(GameObject parent, string name, Vector3 localCentre, Vector3 size, string material, bool solid = true)
    {
        var box = new GameObject(name);
        box.transform.SetParent(parent.transform, false);
        box.transform.localPosition = localCentre;
        var mesh = BoxMesh(size);
        string path = Root + "/Meshes/box_" + (_meshCount++).ToString("000") + ".asset";
        AssetDatabase.CreateAsset(mesh, path);
        box.AddComponent<MeshFilter>().sharedMesh = mesh;
        box.AddComponent<MeshRenderer>().sharedMaterial = Materials[material];
        if (solid)
        {
            box.AddComponent<BoxCollider>().size = size;
        }
        return box;
    }

    private static Mesh BoxMesh(Vector3 size)
    {
        var half = size / 2f;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        // Each face: its normal, and the two directions that span it. On the four upright faces the
        // first runs sideways and the second up, so bricks and planks lie the right way on every wall.
        var faces = new[]
        {
            new[] { Vector3.up, Vector3.right, Vector3.forward }, new[] { Vector3.down, Vector3.forward, Vector3.right },
            new[] { Vector3.right, Vector3.forward, Vector3.up }, new[] { Vector3.left, Vector3.back, Vector3.up },
            new[] { Vector3.forward, Vector3.left, Vector3.up }, new[] { Vector3.back, Vector3.right, Vector3.up },
        };
        foreach (var face in faces)
        {
            Vector3 n = face[0], a = face[1], b = face[2];
            float na = Mathf.Abs(Vector3.Dot(a, half)), nb = Mathf.Abs(Vector3.Dot(b, half)), nn = Mathf.Abs(Vector3.Dot(n, half));
            int start = vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                float sa = (i == 1 || i == 2) ? 1f : -1f, sb = (i >= 2) ? 1f : -1f;
                vertices.Add(n * nn + a * (sa * na) + b * (sb * nb));
                normals.Add(n);
                // One texture repeat per metre and a half, whatever the size of the piece.
                uvs.Add(new Vector2(sa * na, sb * nb) / 1.5f);
            }
            triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }
        var mesh = new Mesh { name = "box" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static void SavePrefab(GameObject instance, string name)
    {
        string path = Root + "/" + name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
        AssetImporter.GetAtPath(path).assetBundleName = BundleName;
    }

    // ---------------------------------------------------------------- materials and textures

    private delegate Color Painter(float u, float v);

    private static void MakeMaterial(string name, Painter painter, float smoothness)
    {
        const int size = 512;
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                texture.SetPixel(x, y, painter(x / (float)size, y / (float)size));
            }
        }
        texture.Apply();
        string texturePath = Root + "/Textures/" + name + ".png";
        File.WriteAllBytes(texturePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(texturePath);

        var material = new Material(Shader.Find("Standard"));
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Metallic", name == "metal" ? 0.6f : 0f);
        AssetDatabase.CreateAsset(material, Root + "/Materials/" + name + ".mat");
        Materials[name] = material;
    }

    /// <summary>Noise that repeats at the texture's edges, so tiled surfaces show no seams.</summary>
    private static float Noise(float u, float v, float scale, float seed)
    {
        float a = Mathf.PerlinNoise(seed + u * scale, seed + v * scale);
        float b = Mathf.PerlinNoise(seed + (u - 1f) * scale, seed + v * scale);
        float c = Mathf.PerlinNoise(seed + u * scale, seed + (v - 1f) * scale);
        float d = Mathf.PerlinNoise(seed + (u - 1f) * scale, seed + (v - 1f) * scale);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    /// <summary>0 below the first edge, 1 above the second, smooth in between.</summary>
    private static float Step(float from, float to, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
    }

    private static float Grain(float u, float v, float seed)
    {
        return Noise(u, v, 4f, seed) * 0.5f + Noise(u, v, 16f, seed + 7f) * 0.3f + Noise(u, v, 64f, seed + 19f) * 0.2f;
    }

    private static Color Plaster(float u, float v)
    {
        float g = Grain(u, v, 3f);
        float stain = Step(0.55f, 0.8f, Noise(u, v, 2.5f, 41f));
        return Color.Lerp(new Color(0.55f, 0.54f, 0.49f), new Color(0.70f, 0.68f, 0.61f), g) * (1f - 0.18f * stain);
    }

    private static Color Concrete(float u, float v)
    {
        float g = Grain(u, v, 11f);
        return Color.Lerp(new Color(0.30f, 0.30f, 0.30f), new Color(0.46f, 0.45f, 0.43f), g);
    }

    private static Color WoodTone(float u, float v, Color dark, Color light, int boards, float seed)
    {
        float across = u * boards;
        int board = Mathf.FloorToInt(across);
        float edge = Mathf.Min(across - board, board + 1 - across);
        // Long streaks along the board, a different tint for each board, a dark line between boards.
        float streak = Noise(u * 6f, v, 3f, seed + board * 5.3f) * 0.6f + Noise(u * 24f, v, 5f, seed + 31f) * 0.4f;
        float tint = Mathf.PerlinNoise(board * 1.37f + seed, 0.5f) * 0.25f;
        var colour = Color.Lerp(dark, light, Mathf.Clamp01(streak + tint - 0.1f));
        return colour * Mathf.Lerp(0.55f, 1f, Step(0f, 0.035f, edge));
    }

    private static Color Planks(float u, float v)
    {
        return WoodTone(u, v, new Color(0.22f, 0.15f, 0.09f), new Color(0.42f, 0.30f, 0.19f), 6, 2f);
    }

    private static Color Wood(float u, float v)
    {
        return WoodTone(u, v, new Color(0.33f, 0.22f, 0.12f), new Color(0.55f, 0.39f, 0.22f), 3, 23f);
    }

    private static Color DarkWood(float u, float v)
    {
        return WoodTone(u, v, new Color(0.12f, 0.08f, 0.05f), new Color(0.24f, 0.16f, 0.10f), 2, 57f);
    }

    private static Color Crate(float u, float v)
    {
        var colour = WoodTone(u, v, new Color(0.36f, 0.30f, 0.18f), new Color(0.58f, 0.50f, 0.32f), 5, 71f);
        // A darker band across, like a strap.
        float band = Mathf.Abs(v - 0.5f) < 0.06f ? 0.6f : 1f;
        return colour * band;
    }

    private static Color Metal(float u, float v)
    {
        float g = Grain(u, v, 97f);
        float rust = Step(0.62f, 0.85f, Noise(u, v, 5f, 13f));
        var paint = Color.Lerp(new Color(0.17f, 0.22f, 0.19f), new Color(0.26f, 0.32f, 0.27f), g);
        return Color.Lerp(paint, new Color(0.33f, 0.17f, 0.09f), rust * 0.7f);
    }

    private static Color Shade(float u, float v)
    {
        return Color.Lerp(new Color(0.10f, 0.20f, 0.14f), new Color(0.16f, 0.30f, 0.20f), Grain(u, v, 5f));
    }
    private static Color Tiles(float u, float v)
    {
        float tu = u * 8f, tv = v * 8f;
        float edge = Mathf.Min(Mathf.Min(tu - Mathf.Floor(tu), Mathf.Ceil(tu) - tu), Mathf.Min(tv - Mathf.Floor(tv), Mathf.Ceil(tv) - tv));
        float shade = 0.92f + Mathf.PerlinNoise(Mathf.Floor(tu) * 3.1f, Mathf.Floor(tv) * 2.7f) * 0.08f;
        var tile = new Color(0.86f, 0.88f, 0.86f) * shade;
        return Color.Lerp(new Color(0.45f, 0.46f, 0.45f), tile, Step(0f, 0.04f, edge));
    }

    private static Color ArmyPaint(float u, float v)
    {
        // Olive below, cream above, with a dark line where they meet; the split shows on tall walls.
        float g = Grain(u, v, 61f);
        var olive = Color.Lerp(new Color(0.20f, 0.25f, 0.15f), new Color(0.28f, 0.33f, 0.20f), g);
        float chipped = Step(0.70f, 0.85f, Noise(u, v, 9f, 29f));
        return Color.Lerp(olive, new Color(0.45f, 0.44f, 0.38f), chipped * 0.6f);
    }

    private static Color Corrugated(float u, float v)
    {
        float rib = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f * 12f);
        float g = Grain(u, v, 83f);
        var paint = Color.Lerp(new Color(0.24f, 0.30f, 0.36f), new Color(0.34f, 0.41f, 0.47f), g);
        float rust = Step(0.66f, 0.9f, Noise(u, v, 4f, 17f));
        return Color.Lerp(paint * Mathf.Lerp(0.6f, 1.05f, rib), new Color(0.36f, 0.19f, 0.10f), rust * 0.6f);
    }

    private static Color Brick(float u, float v)
    {
        float rows = v * 10f;
        int row = Mathf.FloorToInt(rows);
        float across = u * 5f + (row % 2) * 0.5f;
        int brick = Mathf.FloorToInt(across);
        float edge = Mathf.Min(Mathf.Min(rows - row, row + 1 - rows) * 0.5f, Mathf.Min(across - brick, brick + 1 - across));
        float tint = Mathf.PerlinNoise(brick * 1.7f + 5f, row * 2.3f + 9f);
        var face = Color.Lerp(new Color(0.36f, 0.16f, 0.11f), new Color(0.55f, 0.28f, 0.19f), tint) * (0.85f + 0.3f * Grain(u, v, 43f));
        return Color.Lerp(new Color(0.42f, 0.40f, 0.36f), face, Step(0f, 0.05f, edge));
    }

    private static Color Logs(float u, float v)
    {
        float rows = v * 5f;
        int row = Mathf.FloorToInt(rows);
        float round = Mathf.Sin((rows - row) * Mathf.PI);
        float streak = Noise(u, v * 5f, 3f, 37f + row * 3.3f) * 0.6f + Noise(u, v * 5f, 14f, 3f) * 0.4f;
        var bark = Color.Lerp(new Color(0.20f, 0.13f, 0.07f), new Color(0.44f, 0.31f, 0.18f), streak);
        return bark * Mathf.Lerp(0.35f, 1.05f, round);
    }

    private static Color Rug(float u, float v)
    {
        float border = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
        float diamond = Mathf.Abs(((u * 6f) % 1f) - 0.5f) + Mathf.Abs(((v * 6f) % 1f) - 0.5f);
        var field = diamond < 0.3f ? new Color(0.62f, 0.50f, 0.28f) : new Color(0.42f, 0.09f, 0.08f);
        var colour = border < 0.06f ? new Color(0.12f, 0.10f, 0.20f) : field;
        return colour * (0.85f + 0.3f * Noise(u, v, 90f, 7f));
    }

    private static Color White(float u, float v)
    {
        return Color.Lerp(new Color(0.80f, 0.82f, 0.80f), new Color(0.92f, 0.93f, 0.91f), Grain(u, v, 77f));
    }

    private static Color Rubber(float u, float v)
    {
        return Color.Lerp(new Color(0.04f, 0.04f, 0.04f), new Color(0.11f, 0.11f, 0.11f), Grain(u, v, 53f));
    }

    private static Color Cloth(float u, float v, Color colour)
    {
        float weave = 0.9f + 0.1f * Mathf.Sin(u * 400f) * Mathf.Sin(v * 400f);
        return colour * weave * (0.85f + 0.3f * Grain(u, v, 67f));
    }
}
