using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;

namespace ZeroMenuTime
{
    /// <summary>
    /// A tool for making the list of doors a trader's room can be put behind (see <see cref="DoorList"/>).
    /// It looks through the loaded map for doors that are scenery only: they cannot be opened, the
    /// player can walk up to one side of them and to nothing on the other, and no way out of the map
    /// is at them. What it finds is written to a file to be looked over by eye; nothing is used as found.
    /// </summary>
    public static class DoorSurvey
    {
        public class Found
        {
            public string Name = "";
            public string Path = "";
            /// <summary>"scenery": a door that is only a model. "locked": a door of the game that no key opens.</summary>
            public string Kind = "";
            /// <summary>On the ground at the foot of the door, in the middle.</summary>
            public float[] Position;
            /// <summary>The way the door faces: towards the side the player can stand on.</summary>
            public float Yaw;
            public float Width, Height;
            public string NearestExit = "";
            public float NearestExitDistance;
            /// <summary>Something to use or to loot within four metres behind the door: a sign of a room there.</summary>
            public string Behind = "";
            /// <summary>Free room for a standing person 1.2 m behind the door (a hollow building, or a real room).</summary>
            public bool HollowBehind;
            /// <summary>Its material says glass: one can see through it.</summary>
            public bool Glass;
        }

        private static int Solid => LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain", "Default", "DoorLowPolyCollider");

        public static string Dir => System.IO.Path.Combine(TraderConfig.Dir, "survey");

        private static readonly string[] NotADoor =
        {
            "frame", "handle", "knob", "lock", "hinge", "glass", "sign", "doorway", "jamb", "decal", "shadow", "bell",
            "_lod1", "_lod2", "_lod3", "_lod4", "mat", "step", "light", "lamp", "wire", "box", "closer", "stopper", "garage", "car", "truck", "bus", "van",
            "fridge", "refrigerator", "locker", "cabinet", "cupboard", "wardrobe", "safe", "oven", "washer", "container", "wagon", "train", "elevator", "lift", "shkaf",
        };

        /// <summary>When set, everything door-like within four metres of it says why it was taken or turned down.</summary>
        public static Vector3? Probe;
        private static readonly NavMeshPath Path_ = new NavMeshPath();

        /// <summary>
        /// The ground here is part of the ground of the map at large, not an island shut inside a
        /// hollow building: from it one can walk to somewhere at least 25 metres away. (Asking for a
        /// way to where the player stands does not work: across a map the game gives up on the way.)
        /// </summary>
        private static bool Reachable(Vector3 nav)
        {
            for (int step = 0; step < 8; step++)
            {
                var far = nav + Quaternion.Euler(0f, step * 45f, 0f) * Vector3.forward * 35f;
                NavMeshHit there;
                if (NavMesh.SamplePosition(far, out there, 8f, NavMesh.AllAreas) && (there.position - nav).sqrMagnitude > 25f * 25f
                    && NavMesh.CalculatePath(nav, there.position, NavMesh.AllAreas, Path_) && Path_.status == NavMeshPathStatus.PathComplete)
                {
                    return true;
                }
            }
            return false;
        }

        public static string Run(string map)
        {
            var world = Singleton<GameWorld>.Instance;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var why = new Dictionary<string, int>();
            var found = new List<Found>();

            // Ways out of the map: the game's exits and transits, and Vagabond's.
            var exits = new List<KeyValuePair<string, Bounds>>();
            foreach (var point in world.ExfiltrationController.ExfiltrationPoints ?? new ExfiltrationPoint[0])
            {
                if (point != null)
                {
                    exits.Add(new KeyValuePair<string, Bounds>(point.gameObject.name, BoundsOf(point.gameObject)));
                }
            }
            foreach (var point in world.ExfiltrationController.ScavExfiltrationPoints ?? new ScavExfiltrationPoint[0])
            {
                if (point != null)
                {
                    exits.Add(new KeyValuePair<string, Bounds>(point.gameObject.name, BoundsOf(point.gameObject)));
                }
            }
            foreach (var transit in UnityEngine.Object.FindObjectsOfType<TransitPoint>())
            {
                exits.Add(new KeyValuePair<string, Bounds>("transit " + transit.gameObject.name, BoundsOf(transit.gameObject)));
            }

            var things = new List<KeyValuePair<string, Vector3>>();
            foreach (var thing in UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>())
            {
                things.Add(new KeyValuePair<string, Vector3>(thing.GetType().Name + " " + thing.name, BoundsOf(thing.gameObject).center));
            }
            foreach (var loot in UnityEngine.Object.FindObjectsOfType<LootableContainer>())
            {
                things.Add(new KeyValuePair<string, Vector3>("container " + loot.name, loot.transform.position));
            }

            int looked = 0;
            var seen = new List<Vector3>();

            // Doors of the game that are shut for good: locked, no key, cannot be kicked in.
            foreach (var door in UnityEngine.Object.FindObjectsOfType<Door>())
            {
                bool forGood = !door.Operatable || (door.DoorState == EDoorState.Locked && string.IsNullOrEmpty(door.KeyId) && !door.CanBeBreached);
                if (!forGood)
                {
                    continue;
                }
                looked++;
                Consider(door.gameObject, BoundsOf(door.gameObject), "locked", exits, things, found, seen, why);
            }

            foreach (var renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            {
                var go = renderer.gameObject;
                if (!go.scene.IsValid())
                {
                    continue;
                }
                string name = go.name.ToLowerInvariant();
                string parent = go.transform.parent != null ? go.transform.parent.name.ToLowerInvariant() : "";
                if (!name.Contains("door") && !parent.Contains("door") && !name.Contains("dver"))
                {
                    continue;
                }
                if (NotADoor.Any(word => name.Contains(word)))
                {
                    Count(why, "name says it is not a door leaf", go, renderer.bounds.center);
                    continue;
                }
                if (go.GetComponentInParent<WorldInteractiveObject>() != null || go.GetComponentInParent<RoomDoor>() != null)
                {
                    Count(why, "part of a door that works", go, renderer.bounds.center);
                    continue;
                }
                looked++;
                Consider(go, renderer.bounds, "scenery", exits, things, found, seen, why);
            }

            found = found.OrderBy(f => f.Position[0]).ThenBy(f => f.Position[2]).ToList();
            Directory.CreateDirectory(Dir);
            // Named places of the map, to say where a door is: the ways out by the names players know, and the areas bots are given.
            var marks = new List<object>();
            foreach (var point in world.ExfiltrationController.ExfiltrationPoints ?? new ExfiltrationPoint[0])
            {
                if (point != null)
                {
                    string shown = point.Settings != null ? (point.Settings.Name ?? "") : "";
                    marks.Add(new { Kind = "exit", Name = point.gameObject.name, Shown = shown.Localized(), At = new[] { point.transform.position.x, point.transform.position.y, point.transform.position.z } });
                }
            }
            foreach (var zone in UnityEngine.Object.FindObjectsOfType<BotZone>())
            {
                var middle = BoundsOf(zone.gameObject).center;
                marks.Add(new { Kind = "zone", Name = zone.NameZone ?? zone.name, Shown = "", At = new[] { middle.x, middle.y, middle.z } });
            }
            foreach (var transit in UnityEngine.Object.FindObjectsOfType<TransitPoint>())
            {
                marks.Add(new { Kind = "transit", Name = transit.gameObject.name, Shown = "", At = new[] { transit.transform.position.x, transit.transform.position.y, transit.transform.position.z } });
            }
            File.WriteAllText(System.IO.Path.Combine(Dir, "marks_" + map + ".json"), JsonConvert.SerializeObject(marks, Formatting.Indented));
            string file = System.IO.Path.Combine(Dir, "doors_" + map + ".json");
            File.WriteAllText(file, JsonConvert.SerializeObject(found, Formatting.Indented));
            return "door survey of " + map + ": " + found.Count + " usable of " + looked + " looked at, in " + timer.ElapsedMilliseconds + " ms; turned down: "
                   + string.Join("; ", why.OrderByDescending(w => w.Value).Select(w => w.Value + " " + w.Key)) + "; written to " + file;
        }

        private static void Count(Dictionary<string, int> why, string reason, GameObject go = null, Vector3 at = default(Vector3))
        {
            if (Probe.HasValue && go != null && (at - Probe.Value).sqrMagnitude < 16f)
            {
                TestHarness.Report("  " + PathOf(go.transform) + " at " + at.ToString("F1") + ": " + reason);
            }
            int n;
            why.TryGetValue(reason, out n);
            why[reason] = n + 1;
        }

        private static Bounds BoundsOf(GameObject go)
        {
            bool any = false;
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
            {
                if (!any)
                {
                    bounds = collider.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            if (!any)
            {
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any)
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
            }
            return bounds;
        }

        private static void Consider(GameObject go, Bounds bounds, string kind, List<KeyValuePair<string, Bounds>> exits,
            List<KeyValuePair<string, Vector3>> things, List<Found> found, List<Vector3> seen, Dictionary<string, int> why)
        {
            float height = bounds.size.y;
            float across = Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z);
            if (height < 1.7f || height > 2.9f || across < 0.6f || across > 2.7f)
            {
                Count(why, "not the size of a door", go, bounds.center);
                return;
            }
            var centre = bounds.center;
            if (seen.Any(s => (s - centre).sqrMagnitude < 0.8f * 0.8f))
            {
                Count(why, "same door again", go, bounds.center);
                return;
            }
            seen.Add(centre);

            // Which way it faces: the wall it is in is met square-on from in front and from behind.
            Vector3 normal = Vector3.zero;
            float bestDot = 0.93f;
            for (int step = 0; step < 24; step++)
            {
                var d = Quaternion.Euler(0f, step * 15f, 0f) * Vector3.forward;
                RaycastHit hit;
                if (Physics.Raycast(centre + d * 1.3f, -d, out hit, 1.3f, Solid, QueryTriggerInteraction.Ignore)
                    && hit.distance > 0.75f && Mathf.Abs(hit.normal.y) < 0.2f)
                {
                    float dot = Vector3.Dot(new Vector3(hit.normal.x, 0f, hit.normal.z).normalized, d);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                    }
                }
            }
            if (normal == Vector3.zero)
            {
                Count(why, "no solid wall found at it", go, bounds.center);
                return;
            }

            var foot = new Vector3(centre.x, bounds.min.y, centre.z);
            Vector3 frontFloor, backFloor;
            // Ground shut inside a hollow building does not count: only what can be walked to from where the player is.
            bool front = Standable(foot, normal, out frontFloor);
            bool back = Standable(foot, -normal, out backFloor);
            if (front && back)
            {
                Count(why, "can be walked up to from both sides", go, bounds.center);
                return;
            }
            if (!front && !back)
            {
                Count(why, "cannot be walked up to", go, bounds.center);
                return;
            }
            var facing = front ? normal : -normal;
            var floor = front ? frontFloor : backFloor;

            // Nothing to walk on behind it, near or a little further in.
            foreach (float depth in new[] { 1.0f, 2.2f, 3.5f })
            {
                NavMeshHit nav;
                var behind = foot - facing * depth;
                if (NavMesh.SamplePosition(behind, out nav, 1.0f, NavMesh.AllAreas) && Mathf.Abs(nav.position.y - foot.y) < 1.6f
                    && Vector3.Dot(nav.position - foot, facing) < -0.35f && Reachable(nav.position))
                {
                    Count(why, "there is ground to walk on behind it", go, bounds.center);
                    return;
                }
            }

            string nearest = "";
            float nearestDistance = float.MaxValue;
            foreach (var exit in exits)
            {
                float distance = Mathf.Sqrt(exit.Value.SqrDistance(foot + Vector3.up));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = exit.Key;
                }
            }
            // A trader's own extract of Vagabond is no way out with this mod; any other is.
            if (nearestDistance < 8f && !nearest.StartsWith("VGB_EXT_", StringComparison.OrdinalIgnoreCase))
            {
                Count(why, "a way out of the map is at it", go, bounds.center);
                return;
            }

            string behindThing = "";
            foreach (var thing in things)
            {
                var to = thing.Value - foot;
                if (to.sqrMagnitude < 16f && Vector3.Dot(to, facing) < -0.4f && Mathf.Abs(to.y) < 2.5f)
                {
                    behindThing = thing.Key;
                    break;
                }
            }
            if (behindThing.Length > 0)
            {
                Count(why, "something to use or loot lies behind it", go, bounds.center);
                return;
            }

            var inside = foot - facing * 1.2f;
            bool hollow = !Physics.CheckCapsule(inside + Vector3.up * 0.5f, inside + Vector3.up * 1.5f, 0.25f, Solid, QueryTriggerInteraction.Ignore);

            // Width along the wall, from the box round it and the way it faces.
            var along = Vector3.Cross(Vector3.up, facing);
            float width = Mathf.Abs(along.x) * bounds.size.x + Mathf.Abs(along.z) * bounds.size.z;
            if (Probe.HasValue && (bounds.center - Probe.Value).sqrMagnitude < 16f)
            {
                TestHarness.Report("  " + PathOf(go.transform) + " at " + bounds.center.ToString("F1") + ": USABLE");
            }
            found.Add(new Found
            {
                Name = go.name,
                Path = PathOf(go.transform),
                Kind = kind,
                Position = new[] { (float)Math.Round(foot.x, 2), (float)Math.Round(floor.y, 2), (float)Math.Round(foot.z, 2) },
                Yaw = (float)Math.Round(Quaternion.LookRotation(facing).eulerAngles.y, 0),
                Width = (float)Math.Round(width, 2),
                Height = (float)Math.Round(height, 2),
                NearestExit = nearest,
                NearestExitDistance = (float)Math.Round(nearestDistance, 0),
                Behind = behindThing,
                HollowBehind = hollow,
                Glass = SeeThrough(go),
            });
        }

        /// <summary>A person can stand a step off the door on this side, on ground bots walk on, level with the door's foot.</summary>
        private static bool Standable(Vector3 foot, Vector3 side, out Vector3 floorPoint)
        {
            floorPoint = foot;
            var stand = foot + side * 0.9f;
            RaycastHit floor;
            if (!Physics.Raycast(stand + Vector3.up * 1.2f, Vector3.down, out floor, 2.0f, Solid, QueryTriggerInteraction.Ignore)
                || Mathf.Abs(floor.point.y - foot.y) > 0.5f)
            {
                return false;
            }
            floorPoint = floor.point;
            if (Physics.CheckCapsule(floor.point + Vector3.up * 0.55f, floor.point + Vector3.up * 1.6f, 0.25f, Solid, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            NavMeshHit nav;
            return NavMesh.SamplePosition(floor.point, out nav, 0.9f, NavMesh.AllAreas) && Mathf.Abs(nav.position.y - floor.point.y) < 0.6f
                   && Vector3.Dot(nav.position - foot, side) > 0.3f && Reachable(nav.position);
        }

        private static bool SeeThrough(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }
                    string text = (material.name + " " + (material.shader != null ? material.shader.name : "")).ToLowerInvariant();
                    if (text.Contains("glass") || text.Contains("transparent") || text.Contains("window"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; i < 4 && t != null; i++, t = t.parent)
            {
                parts.Insert(0, t.name);
            }
            return string.Join("/", parts);
        }
    }
}
