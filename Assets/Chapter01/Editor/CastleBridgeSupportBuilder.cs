using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    /// <summary>Replaces overlapping support stacks with locally editable, rock-seated masonry.
    /// Source objects, imported FBX meshes, upper arches, buildings and walking collision are read only.</summary>
    public static class CastleBridgeSupportBuilder
    {
        const string Folder = "Assets/Chapter01/CastleWalkwayUpgrade/Meshes";
        const string PrefabFolder = "Assets/Chapter01/CastleWalkwayUpgrade/Prefabs/Supports";
        const string StonePath = "Assets/Chapter01/UpperBridgeSample/Materials/UB_02_WeatheredSideStone.mat";
        const string TrimPath = "Assets/Chapter01/UpperBridgeSample/Materials/UB_03_WornLimestoneEdges.mat";
        const string RootName = "CW_BridgeSupports";
        static readonly Regex Pier = new Regex(@"^C05__(?:Causeway_Vault_[345]_bearing_-?1_\d+_\d+|Skybridge_Gothic_Vault_2[789]_bearing_-?1_\d+_\d+|(?:Cloister_Arcade_Pier_(?:10|11|12|14|15)|Cloister_Floor_Pier_(?:-26|4)|Garden_Upper_Pier_2[012]|Lower_Road_Pier_[13456]|Rift_Bridge_Pier_(?:2[789]|3[0-5])|Upper_Outlook_Pier_(?:15|31)_(?:59|71))_tier_\d+)__0$");
        static readonly Regex Capital = new Regex(@"^C06__(?:Bridge_Pier_Capital_(?:2[789]|3[0-5])|Cloister_Floor_Bearing_(?:-26|4))_piece_\d+__0$");
        static readonly Regex DeepCore = new Regex(@"^(?:Causeway_Vault_[345]|Skybridge_Gothic_Vault_2[789])_(?:Masonry|Arch_Ring_-?1)$");
        static readonly HashSet<string> TerrainNames = new HashSet<string> {
            "Collision__Arrival_Ridge_Top", "Collision__Outer_Bailey_Rock_Top", "Collision__Garden_Rock_Top",
            "Collision__Chapel_Rock_Top", "Collision__Crown_Promontory_Top", "Collision__Upper_Stair_Rock_Top",
            "Collision__Connected_Lower_Massif", "Collision__Connected_Middle_Massif", "Collision__Crown_Massif_Foot", "Collision__Road_Return_Ridge" };

        [Serializable] public sealed class GroundPoint
        {
            public Vector3 point;
            public string collider, surfaceKind, surfaceName, meshAsset;
            public bool found;
        }
        [Serializable] public sealed class Support
        {
            public string name, kind, status, meshAsset, prefabAsset, upperBearing;
            public string[] sourceNames;
            public Vector3 pivot;
            public Vector3[] footprint;
            public GroundPoint[] ground;
            public float bottom, top, minimumGroundHeight, maximumGroundHeight, minimumFoundationEmbed;
            public int triangles, colliders;
            public bool allFootpointsSeated;
        }
        [Serializable] public sealed class Station
        {
            public int station;
            public float z, beamTop, floorUnderside;
            public Vector3 leftPier, rightPier;
            public bool leftSeated, rightSeated, beamTouchesFloor;
        }
        [Serializable] public sealed class Report
        {
            public string root = RootName;
            public string[] replacedNames, materialAssets, terrainColliders, visualRockSources;
            public int sourceRenderers, sourceTriangles, generatedGroups, generatedRenderers, generatedColliders, generatedTriangles, skippedBuriedSupports, skippedObsoleteSupports;
            public bool sourceTransformsPreserved, allGeneratedFoundationsSeated, cloisterStationsValidated;
            public List<Support> supports = new List<Support>();
            public List<Station> cloisterStations = new List<Station>();
            public string scope = "Only C05/C06 audited support stacks and 18 original deep-core meshes are replaced. C03/C04 upper arches, upper buildings, source transforms, shared FBX/materials, route geometry and all existing walking collision remain unchanged. New static obstacle meshes use layer 0; each centred group is an editable prefab.";
            public string foundationMethod = "Centre and every footprint corner are tested against the ten existing terrain colliders and the actual world-space LOD0 triangles of the retained visible Arrival_Ridge_Cliff (the current terrain collision set omits this sloping rock wall). The highest real surface intersection wins. Each sample states Collider or VisualMeshTriangle and its source. The base is 0.35m below the lowest actual surface hit. Missing surfaces still abort construction; wholly buried redundant supports are omitted. No invented floating foundations.";
        }
        sealed class Source
        {
            public MeshFilter filter;
            public string name;
            public Bounds bounds;
            public Vector3[] corners;
            public Matrix4x4 transform;
            public int triangles;
        }
        sealed class Context
        {
            public Scene scene;
            public Transform root;
            public Source[] sources, allSources;
            public Collider[] terrain, walk;
            public RockSurface[] rock;
            public Material[] materials;
            public Report report;
            public readonly HashSet<string> consumed = new HashSet<string>();
        }
        sealed class RockSurface
        {
            public string name, asset;
            public Bounds bounds;
            public Vector3[] vertices;
            public int[] triangles;
        }

        public static string[] GetSourceNames(Scene scene) => GetSourceObjectNames(scene);
        public static string[] GetSourceObjectNames(Scene scene)
        {
            return Filters(scene).Where(f => IsSourceName(f.name)).Select(f => f.name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }
        static bool IsSourceName(string name) => Pier.IsMatch(name) || Capital.IsMatch(name) || DeepCore.IsMatch(name);

        public static Report Build(Scene scene, GameObject newRoot)
        {
            if (!scene.IsValid() || !scene.isLoaded || !newRoot || newRoot.scene != scene)
                throw new ArgumentException("A loaded castle scene and its new walkway root are required.");
            var stone = AssetDatabase.LoadAssetAtPath<Material>(StonePath);
            var trim = AssetDatabase.LoadAssetAtPath<Material>(TrimPath);
            if (!stone || !trim) throw new InvalidOperationException("Approved stone/trim materials are missing.");
            // Read local mesh bounds, not Renderer.bounds: the installer is allowed
            // to have already deactivated every source renderer before this call.
            var all = Filters(scene).Where(f => f.sharedMesh && !f.transform.IsChildOf(newRoot.transform)).Select(ReadSource).ToArray();
            var sources = all.Where(s => IsSourceName(s.name)).ToArray();
            var duplicates = sources.GroupBy(s => s.name).Where(g => g.Count() != 1).Select(g => g.Key).ToArray();
            if (duplicates.Length != 0) throw new InvalidOperationException("Ambiguous support sources: " + string.Join(", ", duplicates));
            if (sources.Length != 324) throw new InvalidOperationException("Support scope changed: expected the audited 324 source renderers, found " + sources.Length);
            var old = newRoot.transform.Find(RootName);
            if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName); root.transform.SetParent(newRoot.transform, false);
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(PrefabFolder);
            var colliders = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)).ToArray();
            var report = new Report { replacedNames = sources.Select(s => s.name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                sourceRenderers = sources.Length, sourceTriangles = sources.Sum(s => s.triangles), materialAssets = new[] { StonePath, TrimPath } };
            var c = new Context { scene = scene, root = root.transform, sources = sources, allSources = all, materials = new[] { stone, trim }, report = report,
                terrain = colliders.Where(x => TerrainNames.Contains(x.name) && x.enabled && x.gameObject.activeInHierarchy).ToArray(),
                walk = colliders.Where(x => x.name.StartsWith("Collision__CW_", StringComparison.Ordinal) && x.enabled && x.gameObject.activeInHierarchy).ToArray() };
            report.terrainColliders = c.terrain.Select(t => t.name).OrderBy(n => n).ToArray();
            c.rock = all.Where(s => s.name == "Arrival_Ridge_Cliff" && s.filter.gameObject.activeInHierarchy && s.filter.GetComponent<Renderer>() && s.filter.GetComponent<Renderer>().enabled)
                .Select(s => new RockSurface { name = s.name, asset = AssetDatabase.GetAssetPath(s.filter.sharedMesh), bounds = s.bounds,
                    vertices = s.filter.sharedMesh.vertices.Select(v => s.filter.transform.TransformPoint(v)).ToArray(),
                    triangles = Enumerable.Range(0, s.filter.sharedMesh.subMeshCount).SelectMany(sub => s.filter.sharedMesh.GetTriangles(sub)).ToArray() }).ToArray();
            if (c.rock.Length != 1) throw new InvalidOperationException("The retained visible Arrival_Ridge_Cliff is required to verify the lower bridge's real sloping rock seat.");
            report.visualRockSources = c.rock.Select(r => r.name + " | " + r.asset + " | " + r.triangles.Length / 3 + " LOD0 triangles").ToArray();
            if (c.terrain.Length < 8 || c.walk.Length == 0) throw new InvalidOperationException("Build supports after the new walkways; retained rock collision must be active.");
            Physics.SyncTransforms();

            // One foundation per arch junction replaces the old transverse copies,
            // repeated vertical tiers and separate road-pier stack at that junction.
            for (int n = 3; n <= 6; n++)
            {
                var prefixes = new List<string> { "C05__Lower_Road_Pier_" + n + "_tier_" };
                if (n <= 5) prefixes.Add("C05__Causeway_Vault_" + n + "_bearing_-1_");
                if (n >= 4) prefixes.Add("C05__Causeway_Vault_" + (n - 1) + "_bearing_1_");
                BuildArchJunction(c, "LowerBridge_Junction_" + n, Select(c, prefixes.ToArray()), "C05__Lower_Road_Pier_" + n + "_tier_");
            }
            for (int n = 27; n <= 30; n++)
            {
                var prefixes = new List<string> { "C05__Rift_Bridge_Pier_" + n + "_tier_", "C06__Bridge_Pier_Capital_" + n + "_piece_" };
                if (n <= 29) prefixes.Add("C05__Skybridge_Gothic_Vault_" + n + "_bearing_-1_");
                if (n >= 28) prefixes.Add("C05__Skybridge_Gothic_Vault_" + (n - 1) + "_bearing_1_");
                BuildArchJunction(c, "UpperBridge_Junction_" + n, Select(c, prefixes.ToArray()), "C05__Rift_Bridge_Pier_" + n + "_tier_");
            }
            BuildCloister(c);

            var remaining = c.sources.Where(s => s.name.StartsWith("C05__", StringComparison.Ordinal) && !c.consumed.Contains(s.name))
                .GroupBy(s => Regex.Replace(s.name, @"_tier_\d+__0$", "")).OrderBy(g => g.Key).ToArray();
            foreach (var group in remaining)
            {
                var pieces = group.ToList();
                string key = group.Key.Substring(5);
                if (key.StartsWith("Rift_Bridge_Pier_", StringComparison.Ordinal))
                {
                    string n = key.Substring("Rift_Bridge_Pier_".Length);
                    pieces.AddRange(Select(c, "C06__Bridge_Pier_Capital_" + n + "_piece_"));
                }
                foreach (var s in pieces) c.consumed.Add(s.name);
                if (key == "Garden_Upper_Pier_22")
                {
                    report.skippedObsoleteSupports++;
                    report.supports.Add(new Support { name = key, kind = "obsolete pier", status = "Omitted: old node22 was removed by the approved straight 21-to23 stair flight.", sourceNames = pieces.Select(s => s.name).ToArray() });
                    continue;
                }
                var main = group.ToArray(); var center = Union(main).center;
                var footprint = Footprint(main);
                string bearing;
                float top = FindUpperBearing(c, center, key, out bearing);
                BuildPier(c, key, pieces.ToArray(), footprint, top, bearing, null);
            }
            // Deep original arch cores are replaced by the consolidated foundations;
            // the imported C03/C04 arch superstructures are deliberately untouched.
            foreach (var s in c.sources.Where(s => DeepCore.IsMatch(s.name))) c.consumed.Add(s.name);
            var unhandled = c.sources.Where(s => !c.consumed.Contains(s.name)).Select(s => s.name).ToArray();
            if (unhandled.Length != 0) throw new InvalidOperationException("Unaccounted support sources: " + string.Join(", ", unhandled));
            report.sourceTransformsPreserved = sources.All(s => s.filter && SameMatrix(s.transform, s.filter.transform.localToWorldMatrix));
            report.allGeneratedFoundationsSeated = report.supports.Where(s => s.colliders > 0 && s.ground != null).All(s => s.allFootpointsSeated);
            report.cloisterStationsValidated = report.cloisterStations.Count == 4 && report.cloisterStations.All(s => s.leftSeated && s.rightSeated && s.beamTouchesFloor);
            if (!report.sourceTransformsPreserved || !report.allGeneratedFoundationsSeated || !report.cloisterStationsValidated)
                throw new InvalidOperationException("Support validation failed; inspect the support report before publishing the scene.");
            AssetDatabase.SaveAssets(); Physics.SyncTransforms();
            return report;
        }

        static void BuildArchJunction(Context c, string key, Source[] sources, string centralPrefix)
        {
            foreach (var s in sources) c.consumed.Add(s.name);
            var feet = sources.Where(s => s.name.Contains("_bearing_")).ToArray();
            var stem = sources.Where(s => s.name.StartsWith(centralPrefix, StringComparison.Ordinal)).ToArray();
            if (feet.Length == 0 || stem.Length == 0) throw new InvalidOperationException("Incomplete arch junction " + key);
            float seat = feet.Min(s => s.bounds.max.y);
            // All vertical tiers in a bearing share one plan but not one top. Use
            // each complete bearing's maximum, then the lowest bearing seat.
            var bearings = feet.GroupBy(s => Regex.Replace(s.name, @"_\d+_\d+__0$", "")).Select(g => g.ToArray()).ToArray();
            seat = bearings.Min(g => g.Max(s => s.bounds.max.y));
            var center = Union(stem).center;
            string upper;
            float top = FindUpperBearing(c, center, key, out upper);
            var footprint = Footprint(feet.Concat(stem).ToArray());
            BuildPier(c, key, sources, footprint, top, upper + "; preserved arch spring seats", (geo, bottom) => {
                float lowerTop = Mathf.Min(seat + .025f, top);
                MasonryPier(geo, footprint, bottom, lowerTop, true);
                foreach (var bearing in bearings)
                {
                    float higherSeat = bearing.Max(s => s.bounds.max.y) + .025f;
                    if (higherSeat > lowerTop + .08f)
                        geo.Prism(Footprint(bearing), lowerTop - .025f, Mathf.Min(higherSeat, top), 0);
                }
                // Single narrow upper stem maintains the original junction's load
                // path to the new deck. It does not fill the neighbouring arch void.
                if (top > lowerTop + .04f)
                    MasonryPier(geo, Footprint(stem), Mathf.Max(bottom, lowerTop - .06f), top, false);
            });
        }

        static void BuildCloister(Context c)
        {
            var sources = Select(c, "C05__Cloister_Floor_Pier_", "C06__Cloister_Floor_Bearing_");
            foreach (var s in sources) c.consumed.Add(s.name);
            var floor = c.allSources.Single(s => s.name == "Cloister_Floor");
            float underside = floor.bounds.min.y;
            float beamBottom = underside - .90f, beamTop = underside + .01f;
            float[] stations = { -25, -15, -5, 3 };
            float[] rows = { 74.8f, 85.2f };
            for (int i = 0; i < stations.Length; i++)
            {
                var record = new Station { station = i, z = stations[i], beamTop = beamTop, floorUnderside = underside,
                    leftPier = new Vector3(rows[0], beamBottom, stations[i]), rightPier = new Vector3(rows[1], beamBottom, stations[i]),
                    beamTouchesFloor = Mathf.Abs(beamTop - underside) <= .025f };
                for (int row = 0; row < 2; row++)
                {
                    var footprint = ChamferRectangle(new Vector2(rows[row], stations[i]), new Vector2(3.0f, 3.4f), .19f);
                    var result = BuildPier(c, "Cloister_Station_" + i + "_Pier_" + row, sources, footprint, beamBottom + .035f,
                        "CW_Support_Cloister_Station_" + i + "_CrossBeam", null);
                    if (row == 0) record.leftSeated = result.allFootpointsSeated; else record.rightSeated = result.allFootpointsSeated;
                }
                var beam = new Geo();
                beam.Prism(ChamferRectangle(new Vector2(floor.bounds.center.x, stations[i]), new Vector2(floor.bounds.size.x, 3.0f), .12f), beamBottom, beamTop, 0);
                beam.Prism(ChamferRectangle(new Vector2(floor.bounds.center.x, stations[i]), new Vector2(floor.bounds.size.x + .10f, 3.10f), .14f), beamBottom + .07f, beamBottom + .25f, 1);
                SaveGroup(c, "Cloister_Station_" + i + "_CrossBeam", beam, new Support { kind = "transverse bearing", status = "Built: continuous bearing across both pier rows, embedded 0.01m into the retained floor.",
                    sourceNames = sources.Select(s => s.name).ToArray(), bottom = beamBottom, top = beamTop, upperBearing = "Cloister_Floor" });
                c.report.cloisterStations.Add(record);
            }
            // Two longitudinal open arcades carry the upper wall lines between
            // transverse frames. Each spandrel is a low-poly strip, not a solid wall.
            for (int row = 0; row < rows.Length; row++)
            {
                var geo = new Geo();
                for (int i = 0; i < stations.Length - 1; i++)
                    ArchSpandrel(geo, rows[row], stations[i] + 1.26f, stations[i + 1] - 1.26f,
                        beamTop, beamBottom - 3.25f, beamBottom - 1.0f, 2.28f);
                SaveGroup(c, "Cloister_Longitudinal_Arcade_" + row, geo, new Support { kind = "longitudinal open arch bearing", status = "Built: three pointed openings carry the retained cloister wall line to four rock-seated frames.",
                    sourceNames = sources.Select(s => s.name).ToArray(), bottom = beamBottom - 3.25f, top = beamTop, upperBearing = "Cloister transverse bearings and original floor" });
            }
        }

        static Support BuildPier(Context c, string key, Source[] sources, List<Vector2> footprint, float top, string bearing, Action<Geo, float> custom)
        {
            var center = footprint.Aggregate(Vector2.zero, (sum, p) => sum + p) / footprint.Count;
            var samples = footprint.Concat(new[] { center }).Select(p => SampleGround(c, p)).ToArray();
            if (samples.Any(p => !p.found)) throw new InvalidOperationException("No rock under support " + key + " at " + string.Join(",", samples.Where(p => !p.found).Select(p => p.point.ToString())));
            float minimum = samples.Min(s => s.point.y), maximum = samples.Max(s => s.point.y), bottom = minimum - .35f;
            var info = new Support { name = key, kind = "continuous stone pier", sourceNames = sources.Select(s => s.name).ToArray(),
                footprint = footprint.Select(p => new Vector3(p.x, bottom, p.y)).ToArray(), ground = samples, bottom = bottom, top = top,
                minimumGroundHeight = minimum, maximumGroundHeight = maximum, minimumFoundationEmbed = minimum - bottom,
                allFootpointsSeated = samples.All(p => p.found && p.point.y >= bottom + .34f), upperBearing = bearing };
            if (minimum > top - .08f)
            {
                c.report.skippedBuriedSupports++;
                info.status = "Omitted: retained rock already reaches or exceeds the bearing; the source pillar was buried/redundant.";
                c.report.supports.Add(info); return info;
            }
            if (top <= bottom + .1f) throw new InvalidOperationException("Invalid support height " + key);
            var geo = new Geo();
            if (custom != null) custom(geo, bottom); else MasonryPier(geo, footprint, bottom, top, true);
            info.status = "Built: one continuous shaft with restrained plinth/capital; foundation fully intersects the sampled rock.";
            SaveGroup(c, key, geo, info); return info;
        }

        static float FindUpperBearing(Context c, Vector3 position, string supportKey, out string name)
        {
            // These retained platforms have deeper original mesh solids than their
            // shallow gameplay collision proxy. Seat the pillar into the real base.
            // Plan overlap alone is insufficient: Rift33 stands above the lookout
            // but carries the new level-66 landing, not the lower level-64 balcony.
            foreach (var s in c.allSources.Where(s =>
                supportKey.StartsWith("Garden_Upper_Pier_", StringComparison.Ordinal) && s.name.StartsWith("C02__High_Garden_Balcony", StringComparison.Ordinal) ||
                supportKey.StartsWith("Upper_Outlook_Pier_", StringComparison.Ordinal) && s.name.StartsWith("C02__Upper_Outlook_Paving", StringComparison.Ordinal)))
                if (position.x >= s.bounds.min.x - .02f && position.x <= s.bounds.max.x + .02f && position.z >= s.bounds.min.z - .02f && position.z <= s.bounds.max.z + .02f)
                { name = s.name; return s.bounds.min.y + .025f; }
            float best = float.NegativeInfinity; string chosen = null;
            var ray = new Ray(new Vector3(position.x, 220, position.z), Vector3.down);
            foreach (var collider in c.walk)
                if (collider.Raycast(ray, out var hit, 450) && hit.point.y > best && hit.normal.y > .5f)
                { best = hit.point.y; chosen = collider.name; }
            if (chosen == null) throw new InvalidOperationException("No rebuilt walking surface above retained support at " + position);
            name = chosen;
            return best - .70f + .025f;
        }
        static GroundPoint SampleGround(Context c, Vector2 p)
        {
            var sample = new GroundPoint { point = new Vector3(p.x, -999, p.y) };
            var ray = new Ray(new Vector3(p.x, 220, p.y), Vector3.down);
            foreach (var collider in c.terrain)
                if (collider.Raycast(ray, out var hit, 450) && (!sample.found || hit.point.y > sample.point.y))
                { sample.found = true; sample.point = hit.point; sample.collider = collider.name; sample.surfaceKind = "Collider"; sample.surfaceName = collider.name; sample.meshAsset = collider is MeshCollider mc ? AssetDatabase.GetAssetPath(mc.sharedMesh) : null; }
            foreach (var surface in c.rock)
            {
                if (p.x < surface.bounds.min.x || p.x > surface.bounds.max.x || p.y < surface.bounds.min.z || p.y > surface.bounds.max.z) continue;
                for (int i = 0; i < surface.triangles.Length; i += 3)
                {
                    var a = surface.vertices[surface.triangles[i]]; var b = surface.vertices[surface.triangles[i+1]]; var d = surface.vertices[surface.triangles[i+2]];
                    var e = b-a; var f = d-a; float determinant = e.x*f.z-e.z*f.x;
                    if (Mathf.Abs(determinant) < .0000001f) continue;
                    float dx = p.x-a.x, dz = p.y-a.z;
                    float u = (dx*f.z-dz*f.x)/determinant, v = (e.x*dz-e.z*dx)/determinant;
                    if (u < -.00001f || v < -.00001f || u+v > 1.00001f) continue;
                    float height = a.y+u*e.y+v*f.y;
                    if (sample.found && height <= sample.point.y) continue;
                    sample.found = true; sample.point = new Vector3(p.x,height,p.y); sample.collider = null;
                    sample.surfaceKind = "VisualMeshTriangle"; sample.surfaceName = surface.name; sample.meshAsset = surface.asset;
                }
            }
            return sample;
        }

        static void MasonryPier(Geo geo, List<Vector2> footprint, float bottom, float top, bool plinth)
        {
            if (top <= bottom) return;
            float height = top - bottom;
            float baseHeight = plinth ? Mathf.Min(.55f, height * .16f) : 0;
            float capHeight = Mathf.Min(.35f, height * .18f);
            var body = Scale(footprint, .88f);
            // Flush stacked rings form a single continuous exterior. No repeated
            // coincident side faces or decorative cylinders are generated.
            var levels = new List<float> { bottom };
            var rings = new List<List<Vector2>> { plinth ? footprint : body };
            if (baseHeight > 0) { levels.Add(bottom + baseHeight); rings.Add(footprint); levels.Add(bottom + baseHeight + Mathf.Min(.18f, height * .08f)); rings.Add(body); }
            levels.Add(top - capHeight); rings.Add(body);
            levels.Add(top - Mathf.Min(.12f, capHeight * .5f)); rings.Add(footprint);
            levels.Add(top); rings.Add(footprint);
            geo.Loft(rings, levels);
        }
        static void ArchSpandrel(Geo g, float x, float from, float to, float top, float spring, float crown, float thickness)
        {
            const int divisions = 10;
            var lower = new Vector2[divisions + 1];
            for (int i = 0; i <= divisions; i++)
            {
                float t = i / (float)divisions;
                // Smooth shoulders with a restrained pointed crown at the centre.
                // Float sin(pi) can be slightly negative; a fractional power would produce NaN at the final spring.
                float raised = Mathf.Max(0f, Mathf.Sin(Mathf.PI * t));
                lower[i] = new Vector2(Mathf.Lerp(from, to, t), Mathf.Lerp(spring, crown, Mathf.Pow(raised, .72f)));
            }
            for (int i = 0; i < divisions; i++)
            {
                var a = lower[i]; var b = lower[i + 1];
                float left = x - thickness * .5f, right = x + thickness * .5f;
                g.Quad(new Vector3(left, a.y, a.x), new Vector3(left, top, a.x), new Vector3(left, top, b.x), new Vector3(left, b.y, b.x), Vector3.left, 0);
                g.Quad(new Vector3(right, a.y, a.x), new Vector3(right, b.y, b.x), new Vector3(right, top, b.x), new Vector3(right, top, a.x), Vector3.right, 0);
                g.Quad(new Vector3(left, a.y, a.x), new Vector3(left, b.y, b.x), new Vector3(right, b.y, b.x), new Vector3(right, a.y, a.x), Vector3.down, 1);
                g.Quad(new Vector3(left, top, a.x), new Vector3(right, top, a.x), new Vector3(right, top, b.x), new Vector3(left, top, b.x), Vector3.up, 0);
            }
            g.Quad(new Vector3(x-thickness*.5f,spring,from),new Vector3(x+thickness*.5f,spring,from),new Vector3(x+thickness*.5f,top,from),new Vector3(x-thickness*.5f,top,from),Vector3.back,0);
            g.Quad(new Vector3(x-thickness*.5f,spring,to),new Vector3(x-thickness*.5f,top,to),new Vector3(x+thickness*.5f,top,to),new Vector3(x+thickness*.5f,spring,to),Vector3.forward,0);
        }

        static void SaveGroup(Context c, string key, Geo geo, Support info)
        {
            string name = "CW_Support_" + key;
            var mesh = geo.Mesh(name); var pivot = mesh.bounds.center;
            var positions = mesh.vertices; for (int i = 0; i < positions.Length; i++) positions[i] -= pivot;
            mesh.vertices = positions; mesh.RecalculateBounds();
            mesh = Store(mesh, Folder + "/" + name + ".asset");
            var group = new GameObject(name); group.transform.SetParent(c.root, false); group.transform.position = pivot;
            var visible = new GameObject(name + "_Visual", typeof(MeshFilter), typeof(MeshRenderer)); visible.transform.SetParent(group.transform, false);
            visible.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visible.GetComponent<MeshRenderer>(); renderer.sharedMaterials = c.materials;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            var obstacle = new GameObject("CW_Obstacle_Support_" + key); obstacle.transform.SetParent(group.transform, false); obstacle.layer = 0;
            var collision = obstacle.AddComponent<MeshCollider>(); collision.sharedMesh = mesh; collision.convex = false;
            collision.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
            info.name = name; info.pivot = pivot; info.meshAsset = AssetDatabase.GetAssetPath(mesh);
            info.prefabAsset = PrefabFolder + "/" + name + ".prefab"; info.triangles = geo.Triangles; info.colliders = 1;
            var saved = PrefabUtility.SaveAsPrefabAssetAndConnect(group, info.prefabAsset, InteractionMode.AutomatedAction, out bool savedSuccessfully);
            if (!savedSuccessfully || !saved) throw new InvalidOperationException("Could not save/connect support prefab " + info.prefabAsset);
            c.report.supports.Add(info); c.report.generatedGroups++; c.report.generatedRenderers++; c.report.generatedColliders++; c.report.generatedTriangles += info.triangles;
        }
        static Source[] Select(Context c, params string[] prefixes) => c.sources.Where(s => prefixes.Any(p => s.name.StartsWith(p, StringComparison.Ordinal))).ToArray();
        static MeshFilter[] Filters(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true)).ToArray();
        static Source ReadSource(MeshFilter f)
        {
            var b = f.sharedMesh.bounds; var corners = new Vector3[8];
            for (int i = 0; i < 8; i++) corners[i] = f.transform.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
            var world = new Bounds(corners[0], Vector3.zero); foreach (var p in corners) world.Encapsulate(p);
            int triangles = 0; for (int i = 0; i < f.sharedMesh.subMeshCount; i++) triangles += (int)f.sharedMesh.GetIndexCount(i) / 3;
            return new Source { filter = f, name = f.name, bounds = world, corners = corners, transform = f.transform.localToWorldMatrix, triangles = triangles };
        }
        static Bounds Union(IEnumerable<Source> values)
        {
            var a = values.ToArray(); if (a.Length == 0) throw new InvalidOperationException("Empty support family.");
            var b = a[0].bounds; foreach (var s in a.Skip(1)) b.Encapsulate(s.bounds); return b;
        }
        static List<Vector2> Footprint(Source[] values) => Hull(values.SelectMany(s => s.corners).Select(p => new Vector2(p.x, p.z)));
        static List<Vector2> Scale(List<Vector2> points, float factor)
        { var center = points.Aggregate(Vector2.zero, (a,b) => a+b) / points.Count; return points.Select(p => center + (p-center)*factor).ToList(); }
        static List<Vector2> ChamferRectangle(Vector2 c, Vector2 size, float bevel)
        {
            float x = size.x*.5f, z = size.y*.5f;
            return new List<Vector2> { c+new Vector2(-x+bevel,-z),c+new Vector2(x-bevel,-z),c+new Vector2(x,-z+bevel),c+new Vector2(x,z-bevel),c+new Vector2(x-bevel,z),c+new Vector2(-x+bevel,z),c+new Vector2(-x,z-bevel),c+new Vector2(-x,-z+bevel) };
        }
        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
        static List<Vector2> Hull(IEnumerable<Vector2> values)
        {
            var p = values.Select(v => new Vector2(Mathf.Round(v.x*10000)/10000,Mathf.Round(v.y*10000)/10000)).Distinct().OrderBy(v=>v.x).ThenBy(v=>v.y).ToArray();
            var h = new List<Vector2>(); foreach (var v in p) { while(h.Count>=2&&Cross(h[h.Count-2],h[h.Count-1],v)<=.00001f)h.RemoveAt(h.Count-1);h.Add(v); }
            int count=h.Count;for(int i=p.Length-2;i>=0;i--){while(h.Count>count&&Cross(h[h.Count-2],h[h.Count-1],p[i])<=.00001f)h.RemoveAt(h.Count-1);h.Add(p[i]);}
            if(h.Count>1)h.RemoveAt(h.Count-1); if(h.Count<3)throw new InvalidOperationException("Degenerate support footprint."); return h;
        }
        static bool SameMatrix(Matrix4x4 a, Matrix4x4 b) { for(int i=0;i<16;i++)if(Mathf.Abs(a[i]-b[i])>.000001f)return false;return true; }

        sealed class Geo
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] indices = { new List<int>(), new List<int>() };
            public int Triangles => indices.Sum(x=>x.Count)/3;
            public void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 desired,int material)
            {
                var n=Vector3.Cross(b-a,c-a);if(n.sqrMagnitude<1e-12f)return;if(Vector3.Dot(n,desired)<0){var t=b;b=c;c=t;n=-n;}n.Normalize();int start=vertices.Count;
                var tangent=Vector3.Cross(Vector3.up,n).normalized;
                foreach(var p in new[]{a,b,c}){vertices.Add(p);normals.Add(n);uv.Add(Mathf.Abs(n.y)>.7f?new Vector2(p.x,p.z):new Vector2(Vector3.Dot(p,tangent),p.y));}
                indices[material].AddRange(new[]{start,start+1,start+2});
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal,int material){Triangle(a,b,c,normal,material);Triangle(a,c,d,normal,material);}
            public void Prism(List<Vector2> polygon,float bottom,float top,int material)
            {
                if(top<=bottom)return;var centre=polygon.Aggregate(Vector2.zero,(a,b)=>a+b)/polygon.Count;
                for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];var normal=new Vector3((a+b).x*.5f-centre.x,0,(a+b).y*.5f-centre.y);
                    Quad(new Vector3(a.x,bottom,a.y),new Vector3(b.x,bottom,b.y),new Vector3(b.x,top,b.y),new Vector3(a.x,top,a.y),normal,material);}
                for(int i=1;i<polygon.Count-1;i++){var a=polygon[0];var b=polygon[i];var d=polygon[i+1];Triangle(new Vector3(a.x,top,a.y),new Vector3(b.x,top,b.y),new Vector3(d.x,top,d.y),Vector3.up,material);Triangle(new Vector3(a.x,bottom,a.y),new Vector3(b.x,bottom,b.y),new Vector3(d.x,bottom,d.y),Vector3.down,material);}
            }
            public void Loft(List<List<Vector2>> rings,List<float> levels)
            {
                for(int k=0;k<rings.Count-1;k++)
                {
                    if(levels[k+1]<=levels[k]+.0001f)continue;
                    var centre=rings[k].Aggregate(Vector2.zero,(a,b)=>a+b)/rings[k].Count;
                    for(int i=0;i<rings[k].Count;i++){int j=(i+1)%rings[k].Count;var a=rings[k][i];var b=rings[k][j];var d=rings[k+1][i];var e=rings[k+1][j];
                        Quad(new Vector3(a.x,levels[k],a.y),new Vector3(b.x,levels[k],b.y),new Vector3(e.x,levels[k+1],e.y),new Vector3(d.x,levels[k+1],d.y),new Vector3((a+b).x*.5f-centre.x,0,(a+b).y*.5f-centre.y),(k==0&&rings.Count>4)||k>=rings.Count-3?1:0);}
                }
                foreach(int k in new[]{0,rings.Count-1})for(int i=1;i<rings[k].Count-1;i++){var a=rings[k][0];var b=rings[k][i];var d=rings[k][i+1];Triangle(new Vector3(a.x,levels[k],a.y),new Vector3(b.x,levels[k],b.y),new Vector3(d.x,levels[k],d.y),k==0?Vector3.down:Vector3.up,1);}
            }
            public Mesh Mesh(string name)
            {
                var mesh=new Mesh{name=name,indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.subMeshCount=2;
                for(int i=0;i<2;i++)mesh.SetTriangles(indices[i],i,false);mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
            }
        }
        static Mesh Store(Mesh made,string path)
        {
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!existing){made.UploadMeshData(false);AssetDatabase.CreateAsset(made,path);return made;}
            existing.Clear();existing.indexFormat=made.indexFormat;existing.vertices=made.vertices;existing.normals=made.normals;existing.uv=made.uv;existing.tangents=made.tangents;existing.subMeshCount=made.subMeshCount;
            for(int i=0;i<made.subMeshCount;i++)existing.SetTriangles(made.GetTriangles(i),i,false);existing.bounds=made.bounds;existing.UploadMeshData(false);EditorUtility.SetDirty(existing);UnityEngine.Object.DestroyImmediate(made);return existing;
        }
    }
}
