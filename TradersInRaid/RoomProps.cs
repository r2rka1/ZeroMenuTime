using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// Furniture for the trader rooms, copied out of the game's own hideout interior. The game has
    /// no loose furniture files; the hideout scene is the one place its tables, shelves, lamps and
    /// crates can be had on any map.
    /// </summary>
    public static class RoomProps
    {
        public static BepInEx.Configuration.ConfigEntry<bool> Use;

        // Inside measurements of the room the layouts are written for (TraderRoom's shell).
        private const float HalfWidth = 3f, HalfLength = 3.5f, Tile = 2.5f;

        private static Dictionary<string, Transform> _byName;
        private static Dictionary<string, Material> _materials;
        private static GameObject _indexedFrom;

        public static void Bind(BepInEx.Configuration.ConfigFile config)
        {
            Use = config.Bind("Trader rooms", "Furnish rooms from the game", true,
                "Build the trader rooms from the game's own furniture and wall materials, copied from the hideout interior. Turn off to use the mod's own simple rooms. Applies from the next raid.");
        }

        /// <summary>Looks up everything in the hideout by name, once per load of it.</summary>
        private static bool Index()
        {
            if (!HideoutBunker.Loaded)
            {
                return false;
            }
            if (_byName != null && _indexedFrom != null)
            {
                return true;
            }
            _byName = new Dictionary<string, Transform>();
            _materials = new Dictionary<string, Material>();
            _indexedFrom = null;
            foreach (var root in HideoutBunker.Roots())
            {
                if (_indexedFrom == null)
                {
                    _indexedFrom = root;
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    // Of several with one name, the one that draws something wins.
                    Transform held;
                    if (!_byName.TryGetValue(t.name, out held)
                        || (held.GetComponentInChildren<MeshRenderer>(true) == null && t.GetComponentInChildren<MeshRenderer>(true) != null))
                    {
                        _byName[t.name] = t;
                    }
                }
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || material.shader == null)
                        {
                            continue;
                        }
                        // Two materials can share a name; the one on the game's own surface shader is the real one.
                        Material held;
                        if (!_materials.TryGetValue(material.name, out held)
                            || (!held.shader.name.StartsWith("p0/", StringComparison.Ordinal) && material.shader.name.StartsWith("p0/", StringComparison.Ordinal)))
                        {
                            _materials[material.name] = material;
                        }
                    }
                }
            }
            Plugin.Log.LogInfo("Room furniture: " + _byName.Count + " hideout objects and " + _materials.Count + " materials to choose from");
            return _indexedFrom != null;
        }

        /// <summary>Furnishes every room that is still bare. Safe to call again and again.</summary>
        public static int DressAll(bool again = false)
        {
            if (Use == null || !Use.Value || !Index())
            {
                return 0;
            }
            int done = 0;
            foreach (var room in TraderRoom.Rooms)
            {
                if (room.Shell && (again || !room.Dressed))
                {
                    try
                    {
                        Dress(room);
                        done++;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError("Furnishing the room of " + room.TraderName + " failed: " + e);
                    }
                }
            }
            if (done > 0)
            {
                Physics.SyncTransforms();
            }
            return done;
        }

        private class Look
        {
            public readonly Dictionary<string, string> Skins = new Dictionary<string, string>();
            public readonly List<string> Lines = new List<string>();
        }

        private static Dictionary<string, Look> ReadLayouts()
        {
            string text = RoomLayouts.Text;
            try
            {
                // While testing, a file beside the test commands replaces the built-in layouts.
                string file = System.IO.Path.Combine(TestHarness.Dir, "layout.txt");
                if (TestHarness.Enabled != null && TestHarness.Enabled.Value && File.Exists(file))
                {
                    text = File.ReadAllText(file);
                }
            }
            catch (Exception)
            {
            }
            var looks = new Dictionary<string, Look>(StringComparer.OrdinalIgnoreCase);
            Look current = null;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    int close = line.IndexOf(']');
                    current = new Look();
                    looks[line.Substring(1, close - 1).Trim()] = current;
                    foreach (var pair in line.Substring(close + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq > 0)
                        {
                            current.Skins[pair.Substring(0, eq)] = pair.Substring(eq + 1);
                        }
                    }
                }
                else if (current != null)
                {
                    current.Lines.Add(line);
                }
            }
            return looks;
        }

        public static string Dress(TraderRoom room)
        {
            var looks = ReadLayouts();
            string style = TraderRoom.LookOverride ?? room.TraderName ?? "";
            Look own, common;
            looks.TryGetValue(style, out own);
            looks.TryGetValue("*", out common);
            if (own == null)
            {
                looks.TryGetValue("Base", out own);
            }

            var old = room.Root.transform.Find("props");
            if (old != null)
            {
                old.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(old.gameObject);
            }
            var holder = new GameObject("props");
            holder.transform.SetParent(room.Root.transform, false);

            // Walls, floor and ceiling take the hideout's own materials.
            foreach (var part in room.Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (part.transform.parent != room.Root.transform)
                {
                    continue;
                }
                string kind = part.name.StartsWith("wall", StringComparison.Ordinal) ? "wall" : part.name;
                string wanted = null;
                if (own != null)
                {
                    own.Skins.TryGetValue(kind, out wanted);
                }
                if (wanted == null && common != null)
                {
                    common.Skins.TryGetValue(kind, out wanted);
                }
                Material skin;
                if (wanted == null || !_materials.TryGetValue(wanted, out skin))
                {
                    if (wanted != null)
                    {
                        Plugin.Log.LogWarning("Room furniture: no material called " + wanted);
                    }
                    continue;
                }
                var size = part.transform.localScale;
                // The side of the box that faces into the room decides how often the picture repeats.
                Vector2 repeat = kind == "wall"
                    ? new Vector2((size.x < size.z ? size.z : size.x) / Tile, size.y / Tile)
                    : new Vector2(size.x / Tile, size.z / Tile);
                var mine = new Material(skin);
                foreach (var property in mine.GetTexturePropertyNames())
                {
                    mine.SetTextureScale(property, Vector2.Scale(skin.GetTextureScale(property), repeat));
                }
                part.sharedMaterial = mine;
            }

            int placed = 0, missing = 0;
            var all = new List<string>();
            if (common != null)
            {
                all.AddRange(common.Lines);
            }
            if (own != null)
            {
                all.AddRange(own.Lines);
            }
            foreach (var line in all)
            {
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4)
                {
                    continue;
                }
                float x = Number(p[1]), z = Number(p[2]), yaw = Number(p[3]), y = 0f, scale = 1f;
                bool solid = true, table = false, door = false;
                char against = ' ';
                for (int i = 4; i < p.Length; i++)
                {
                    float number;
                    if (float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    {
                        y = number;
                    }
                    else if (p[i] == "n")
                    {
                        solid = false;
                    }
                    else if (p[i] == "table")
                    {
                        table = true;
                    }
                    else if (p[i] == "door")
                    {
                        door = true;
                    }
                    else if (p[i].StartsWith("s=", StringComparison.Ordinal))
                    {
                        scale = Number(p[i].Substring(2));
                    }
                    else if (p[i].Length == 1 && "NESW".IndexOf(p[i][0]) >= 0)
                    {
                        against = p[i][0];
                    }
                }
                var prop = Place(holder.transform, p[0], new Vector3(x, y, z), yaw, scale, against, solid);
                if (prop == null)
                {
                    missing++;
                    Plugin.Log.LogWarning("Room furniture: nothing in the hideout is called " + p[0]);
                    continue;
                }
                placed++;
                if (door && room.ExitDoor != null)
                {
                    // The real door is what is seen; the plain one behind it stays as the thing to use.
                    var plainDoor = room.ExitDoor.GetComponent<MeshRenderer>();
                    if (plainDoor != null)
                    {
                        plainDoor.enabled = false;
                    }
                }
                Bounds b;
                if (table && Measure(prop, out b))
                {
                    // Deliveries are laid out on this one; the plain stand-in goes.
                    var plain = room.Root.transform.Find("table");
                    if (plain != null)
                    {
                        plain.gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(plain.gameObject);
                    }
                    room.TableTop = new Vector3(b.center.x, b.max.y, b.center.z);
                    room.TableSize = new Vector2(Mathf.Max(0.3f, b.size.x - 0.2f), Mathf.Max(0.3f, b.size.z - 0.2f));
                    room.TableSlots = null;
                }
            }
            room.Dressed = true;
            string result = "room of " + room.TraderName + " furnished as " + style + ": " + placed + " piece(s)" + (missing > 0 ? ", " + missing + " not found" : "");
            Plugin.Log.LogInfo("Room furniture: " + result);
            return result;
        }

        private static float Number(string text)
        {
            return float.Parse(text, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Stands a copy of a hideout object in a room. The place given is where the middle of its
        /// footprint goes, measured on the floor from the middle of the room; a wall letter pushes
        /// it flat against that wall instead.
        /// </summary>
        public static GameObject Place(Transform parent, string name, Vector3 local, float yaw, float scale, char against, bool solid)
        {
            Transform source;
            if (!Index() || !_byName.TryGetValue(name, out source))
            {
                return null;
            }
            // Made inside something switched off, so nothing on the copy wakes up before it is stripped.
            var crib = new GameObject("crib");
            crib.SetActive(false);
            var copy = UnityEngine.Object.Instantiate(source.gameObject, crib.transform);
            copy.name = name;
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    try
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.DestroyImmediate(body);
            }
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            foreach (var sound in copy.GetComponentsInChildren<AudioSource>(true))
            {
                UnityEngine.Object.DestroyImmediate(sound);
            }
            foreach (var light in copy.GetComponentsInChildren<Light>(true))
            {
                UnityEngine.Object.DestroyImmediate(light);
            }
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
            }

            // Keep its own tilt (many models lie on their side as imported) but not the way it happened to face.
            var tilt = source.rotation.eulerAngles;
            copy.transform.SetParent(parent, false);
            copy.transform.localScale = source.lossyScale * scale;
            copy.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt.x, 0f, tilt.z);
            copy.SetActive(true);
            UnityEngine.Object.Destroy(crib);

            Bounds b;
            if (!Measure(copy, out b))
            {
                UnityEngine.Object.Destroy(copy);
                return null;
            }
            var foot = new Vector3(b.center.x, b.min.y, b.center.z);
            var want = parent.TransformPoint(local);
            var origin = parent.position;
            const float gap = 0.03f;
            switch (against)
            {
                case 'E': want.x = origin.x + HalfWidth - b.extents.x - gap; break;
                case 'W': want.x = origin.x - HalfWidth + b.extents.x + gap; break;
                case 'N': want.z = origin.z + HalfLength - b.extents.z - gap; break;
                case 'S': want.z = origin.z - HalfLength + b.extents.z + gap; break;
            }
            copy.transform.position += want - foot;

            if (solid && Measure(copy, out b) && b.size.y > 0.12f)
            {
                var block = new GameObject("solid");
                block.transform.SetParent(parent, false);
                block.transform.position = b.center;
                block.transform.rotation = Quaternion.identity;
                var box = block.AddComponent<BoxCollider>();
                box.size = b.size;
                TraderRoom.MakeSolid(box, TraderRoom.SolidLayer());
                block.transform.SetParent(copy.transform, true);
            }
            return copy;
        }

        /// <summary>The pieces a name search finds, for writing layouts.</summary>
        public static List<string> Find(string word)
        {
            var found = new List<string>();
            if (!Index())
            {
                found.Add("FAILED the hideout interior is not loaded");
                return found;
            }
            foreach (var pair in _byName)
            {
                Bounds b;
                if (pair.Key.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 && pair.Value.GetComponent<LODGroup>() != null && Measure(pair.Value.gameObject, out b))
                {
                    found.Add(pair.Key + "  " + b.size.ToString("F2"));
                }
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        /// <summary>
        /// Writes every distinct object name in the hideout that draws something, with how many
        /// there are and how big the first one is, to a file: the catalogue layouts are chosen from.
        /// </summary>
        public static string Survey(string file)
        {
            if (!HideoutBunker.Loaded && !HideoutBunker.Adopt())
            {
                return "FAILED the hideout interior is not loaded";
            }
            var seen = new Dictionary<string, int>();
            var first = new Dictionary<string, Transform>();
            foreach (var root in HideoutBunker.Roots())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int count;
                    seen.TryGetValue(t.name, out count);
                    seen[t.name] = count + 1;
                    if (count == 0)
                    {
                        first[t.name] = t;
                    }
                }
            }
            var names = new List<string>(seen.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            var text = new StringBuilder();
            int written = 0;
            foreach (var name in names)
            {
                var t = first[name];
                var renderers = t.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length == 0)
                {
                    continue;
                }
                Bounds b;
                if (!Measure(t.gameObject, out b))
                {
                    continue;
                }
                text.Append(name).Append('\t').Append(seen[name].ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(b.size.x.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(' ').Append(b.size.y.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(' ').Append(b.size.z.ToString("F2", CultureInfo.InvariantCulture))
                    .Append('\t').Append("renderers ").Append(renderers.Length)
                    .Append(" colliders ").Append(t.GetComponentsInChildren<Collider>(true).Length)
                    .Append(" lights ").Append(t.GetComponentsInChildren<Light>(true).Length)
                    .Append(" children ").Append(t.childCount)
                    .Append(t.gameObject.activeInHierarchy ? " shown" : " hidden")
                    .Append(t.GetComponent<LODGroup>() != null ? " lod" : "")
                    .Append('\t').Append(Path(t)).Append('\n');
                written++;
            }
            File.WriteAllText(file, text.ToString());
            return "wrote " + written + " drawn object names (of " + names.Count + ") to " + file;
        }

        /// <summary>Every material in the hideout, with what uses it most: for choosing wall and floor skins.</summary>
        public static string SurveyMaterials(string file)
        {
            if (!HideoutBunker.Loaded && !HideoutBunker.Adopt())
            {
                return "FAILED the hideout interior is not loaded";
            }
            var uses = new Dictionary<Material, int>();
            var biggest = new Dictionary<Material, MeshRenderer>();
            foreach (var root in HideoutBunker.Roots())
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            continue;
                        }
                        int count;
                        uses.TryGetValue(material, out count);
                        uses[material] = count + 1;
                        MeshRenderer held;
                        if (!biggest.TryGetValue(material, out held) || renderer.bounds.size.sqrMagnitude > held.bounds.size.sqrMagnitude)
                        {
                            biggest[material] = renderer;
                        }
                    }
                }
            }
            var text = new StringBuilder();
            foreach (var pair in uses)
            {
                var m = pair.Key;
                var r = biggest[m];
                var size = r.bounds.size;
                string texture = m.HasProperty("_MainTex") && m.mainTexture != null ? m.mainTexture.name : "-";
                string scale = m.HasProperty("_MainTex") ? m.mainTextureScale.ToString("F1") : "-";
                text.Append(m.name).Append('\t').Append(pair.Value).Append('\t').Append(m.shader != null ? m.shader.name : "?")
                    .Append('\t').Append(texture).Append(' ').Append(scale)
                    .Append('\t').Append(r.name).Append(' ').Append(size.x.ToString("F1", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(size.y.ToString("F1", CultureInfo.InvariantCulture)).Append(' ').Append(size.z.ToString("F1", CultureInfo.InvariantCulture))
                    .Append('\t').Append(Path(r.transform)).Append('\n');
            }
            File.WriteAllText(file, text.ToString());
            return "wrote " + uses.Count + " materials to " + file;
        }

        /// <summary>The box around everything an object draws, whether or not it is switched on.</summary>
        public static bool Measure(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }
                // Worked out from the mesh, since a hidden renderer reports no bounds of its own.
                var local = filter.sharedMesh.bounds;
                var matrix = renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var world = matrix.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(world, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(world);
                    }
                }
            }
            return any;
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (var at = t.parent; at != null && parts.Count < 4; at = at.parent)
            {
                parts.Add(at.name);
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
