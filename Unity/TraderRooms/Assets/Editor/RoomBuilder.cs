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

            SavePrefab(BuildRoom(), "TraderRoom_Base");
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

    private static GameObject BuildRoom()
    {
        var room = new GameObject("TraderRoom_Base");
        float halfW = Width / 2f, halfL = Length / 2f;

        // Shell.
        Box(room, "floor", new Vector3(0f, -Wall / 2f, 0f), new Vector3(Width + 2 * Wall, Wall, Length + 2 * Wall), "planks");
        Box(room, "ceiling", new Vector3(0f, Height + Wall / 2f, 0f), new Vector3(Width + 2 * Wall, Wall, Length + 2 * Wall), "concrete");
        Box(room, "wall west", new Vector3(-(halfW + Wall / 2f), Height / 2f, 0f), new Vector3(Wall, Height, Length + 2 * Wall), "plaster");
        Box(room, "wall east", new Vector3(halfW + Wall / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Length + 2 * Wall), "plaster");
        Box(room, "wall south", new Vector3(0f, Height / 2f, -(halfL + Wall / 2f)), new Vector3(Width, Height, Wall), "plaster");
        Box(room, "wall north", new Vector3(0f, Height / 2f, halfL + Wall / 2f), new Vector3(Width, Height, Wall), "plaster");

        // Skirting boards and ceiling beams, for something to catch the light.
        const float skirt = 0.12f, skirtDepth = 0.03f;
        Box(room, "skirting west", new Vector3(-halfW + skirtDepth / 2f, skirt / 2f, 0f), new Vector3(skirtDepth, skirt, Length), "darkwood", false);
        Box(room, "skirting east", new Vector3(halfW - skirtDepth / 2f, skirt / 2f, 0f), new Vector3(skirtDepth, skirt, Length), "darkwood", false);
        Box(room, "skirting north", new Vector3(0f, skirt / 2f, halfL - skirtDepth / 2f), new Vector3(Width, skirt, skirtDepth), "darkwood", false);
        for (int i = -1; i <= 1; i++)
        {
            Box(room, "beam " + (i + 1), new Vector3(0f, Height - 0.09f, i * 2.2f), new Vector3(Width, 0.18f, 0.16f), "darkwood", false);
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
        Box(table, "top", new Vector3(0f, tableH - top / 2f, 0f), new Vector3(tableW, top, tableL), "wood");
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

        // The trader's side of the room: shelves behind, a desk off to the side.
        float shelfZ = halfL - 0.2f;
        for (int level = 0; level < 4; level++)
        {
            Box(room, "shelf " + level, new Vector3(0f, 0.35f + level * 0.55f, shelfZ), new Vector3(3.2f, 0.04f, 0.38f), "wood", level == 0);
        }
        for (int upright = -1; upright <= 1; upright++)
        {
            Box(room, "shelf upright " + (upright + 1), new Vector3(upright * 1.6f, 1.05f, shelfZ), new Vector3(0.05f, 2.1f, 0.38f), "darkwood", false);
        }
        var boxes = new[]
        {
            new Vector4(-1.2f, 0, 0.45f, 0.30f), new Vector4(-0.55f, 0, 0.35f, 0.22f), new Vector4(0.6f, 0, 0.50f, 0.34f),
            new Vector4(-0.9f, 1, 0.40f, 0.26f), new Vector4(0.3f, 1, 0.30f, 0.20f), new Vector4(1.1f, 1, 0.42f, 0.30f),
            new Vector4(-0.3f, 2, 0.48f, 0.28f), new Vector4(0.95f, 2, 0.34f, 0.24f), new Vector4(-1.25f, 3, 0.36f, 0.22f),
        };
        foreach (var b in boxes)
        {
            float shelfTop = 0.37f + b.y * 0.55f;
            Box(room, "shelf box", new Vector3(b.x, shelfTop + b.w / 2f, shelfZ), new Vector3(b.z, b.w, 0.30f), b.y % 2 == 0 ? "crate" : "metal", false);
        }

        Box(room, "desk top", new Vector3(-halfW + 0.45f, 0.75f, 1.6f), new Vector3(0.7f, 0.05f, 1.5f), "wood");
        Box(room, "desk side a", new Vector3(-halfW + 0.45f, 0.36f, 0.9f), new Vector3(0.66f, 0.72f, 0.05f), "darkwood");
        Box(room, "desk side b", new Vector3(-halfW + 0.45f, 0.36f, 2.3f), new Vector3(0.66f, 0.72f, 0.05f), "darkwood");
        Box(room, "ledger", new Vector3(-halfW + 0.45f, 0.79f, 1.5f), new Vector3(0.24f, 0.03f, 0.32f), "darkwood", false);
        Box(room, "strongbox", new Vector3(-halfW + 0.42f, 0.90f, 2.0f), new Vector3(0.36f, 0.24f, 0.28f), "metal", false);

        // Crates stacked in the corner by the door, and a locker.
        Box(room, "crate a", new Vector3(-halfW + 0.5f, 0.3f, -halfL + 0.6f), new Vector3(0.8f, 0.6f, 0.8f), "crate");
        Box(room, "crate b", new Vector3(-halfW + 0.5f, 0.85f, -halfL + 0.6f), new Vector3(0.7f, 0.5f, 0.7f), "crate").transform.localRotation = Quaternion.Euler(0f, 14f, 0f);
        Box(room, "crate c", new Vector3(-halfW + 1.4f, 0.25f, -halfL + 0.5f), new Vector3(0.6f, 0.5f, 0.6f), "crate").transform.localRotation = Quaternion.Euler(0f, -9f, 0f);
        Box(room, "locker", new Vector3(halfW - 0.3f, 0.95f, -halfL + 0.7f), new Vector3(0.5f, 1.9f, 0.9f), "metal");

        // Two hanging lamps: one over the middle, one over the table.
        Lamp(room, new Vector3(0f, Height, 0.4f), 1.5f, 11f);
        Lamp(room, new Vector3(tableCentre.x - 0.1f, Height, tableCentre.z), 0.9f, 6f);

        // The named places the mod looks for.
        Anchor(room, "Entry", new Vector3(0f, 0.05f, -halfL + 1.2f), 0f);
        Anchor(room, "TraderStand", new Vector3(0f, 0f, halfL - 1.4f), 180f);
        var indoor = Anchor(room, "IndoorBounds", new Vector3(0f, Height / 2f, 0f), 0f);
        indoor.transform.localScale = new Vector3(Width + 2 * Wall, Height + 2 * Wall, Length + 2 * Wall);
        return room;
    }

    private static void Lamp(GameObject room, Vector3 ceilingPoint, float intensity, float range)
    {
        const float drop = 0.55f;
        Box(room, "lamp cord", ceilingPoint - new Vector3(0f, drop / 2f, 0f), new Vector3(0.015f, drop, 0.015f), "metal", false);
        Box(room, "lamp shade", ceilingPoint - new Vector3(0f, drop + 0.06f, 0f), new Vector3(0.34f, 0.12f, 0.34f), "shade", false);
        var bulb = new GameObject("lamp light");
        bulb.transform.SetParent(room.transform, false);
        bulb.transform.localPosition = ceilingPoint - new Vector3(0f, drop + 0.2f, 0f);
        var light = bulb.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.90f, 0.74f);
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
        // Each face: its normal, and the two directions that span it.
        var faces = new[]
        {
            new[] { Vector3.up, Vector3.right, Vector3.forward }, new[] { Vector3.down, Vector3.forward, Vector3.right },
            new[] { Vector3.right, Vector3.forward, Vector3.up }, new[] { Vector3.left, Vector3.up, Vector3.forward },
            new[] { Vector3.forward, Vector3.up, Vector3.right }, new[] { Vector3.back, Vector3.right, Vector3.up },
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

    private static float Grain(float u, float v, float seed)
    {
        return Noise(u, v, 4f, seed) * 0.5f + Noise(u, v, 16f, seed + 7f) * 0.3f + Noise(u, v, 64f, seed + 19f) * 0.2f;
    }

    private static Color Plaster(float u, float v)
    {
        float g = Grain(u, v, 3f);
        float stain = Mathf.SmoothStep(0.55f, 0.8f, Noise(u, v, 2.5f, 41f));
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
        return colour * Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 0.035f, edge));
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
        float rust = Mathf.SmoothStep(0.62f, 0.85f, Noise(u, v, 5f, 13f));
        var paint = Color.Lerp(new Color(0.17f, 0.22f, 0.19f), new Color(0.26f, 0.32f, 0.27f), g);
        return Color.Lerp(paint, new Color(0.33f, 0.17f, 0.09f), rust * 0.7f);
    }

    private static Color Shade(float u, float v)
    {
        return Color.Lerp(new Color(0.10f, 0.20f, 0.14f), new Color(0.16f, 0.30f, 0.20f), Grain(u, v, 5f));
    }
}
