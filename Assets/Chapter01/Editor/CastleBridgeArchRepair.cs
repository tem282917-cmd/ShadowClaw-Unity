using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    /// <summary>Fits closed masonry arches to the currently loaded, manually edited castle.
    /// Never opens or saves a scene and never moves an existing object.</summary>
    public static class CastleBridgeArchRepair
    {
        const string RootName = "CastleBridgeArchRepair";
        const string AssetRoot = "Assets/Chapter01/BridgeArchRepair";
        const string ReportFolder = "D:/Shadow/Builds/CastleSceneRepair-20261118";
        const string StonePath = "Assets/Chapter01/UpperBridgeSample/Materials/UB_02_WeatheredSideStone.mat";
        const string TrimPath = "Assets/Chapter01/UpperBridgeSample/Materials/UB_03_WornLimestoneEdges.mat";
        const float Embed = .025f;
        static bool UseCorbelledTop;
        static readonly Regex OldArch = new Regex(@"^C0[34]__(?:Causeway_Vault|Skybridge_Gothic_Vault)_\d+__0(?: \(\d+\))?$");
        static readonly Regex Segment = new Regex(@"^WalkSurface_CW_Segment_(\d+)$");
        static readonly Dictionary<int, string> Bearings = new Dictionary<int, string> {
            {1,"Lower_Road_Pier_1"},{3,"LowerBridge_Junction_3"},{4,"LowerBridge_Junction_4"},
            {5,"LowerBridge_Junction_5"},{6,"LowerBridge_Junction_6"},
            {10,"Cloister_Arcade_Pier_10"},{11,"Cloister_Arcade_Pier_11"},{12,"Cloister_Arcade_Pier_12"},
            {13,"Cloister_Station_0_CrossBeam"},{14,"Cloister_Arcade_Pier_14"},{15,"Cloister_Arcade_Pier_15"},
            {20,"Garden_Upper_Pier_20"},{21,"Garden_Upper_Pier_21"},
            {27,"UpperBridge_Junction_27"},{28,"UpperBridge_Junction_28"},{29,"UpperBridge_Junction_29"},{30,"UpperBridge_Junction_30"},
            {31,"Rift_Bridge_Pier_31"},{32,"Rift_Bridge_Pier_32"},{33,"Rift_Bridge_Pier_33"},
            {34,"Rift_Bridge_Pier_34"},{35,"Rift_Bridge_Pier_35"}
        };

        [Serializable] public sealed class SourceInfo
        {
            public string name, path, meshAsset;
            public Vector3 center, size;
            public int lod0Triangles, assignedSegment = -1;
            public float assignmentDistance;
            public bool active, disabled;
        }
        [Serializable] public sealed class SpanInfo
        {
            public int segment, endNode, triangles, topSamples, boundaryEdges, nonManifoldEdges, abutmentMeshes, abutmentSectionTriangles;
            public string status, reason, deck, startBearing, endBearing, meshAsset, prefabAsset;
            public Vector3 from, to;
            public float width, upperWidth, minimumUpperWidth, springStart, springEnd, crownDepth, maximumTopSeam, minimumEndEmbed;
            public double volume;
            public bool built, closed, fitted, endContacts;
            public string[] replacedSources = Array.Empty<string>();
        }
        [Serializable] public sealed class MeshIssue
        {
            public string meshAsset, exampleOwner;
            public int vertices, nonFiniteVertices, nonFiniteNormals, nonFiniteTangents, affectedTriangles;
            public bool finiteBounds;
        }
        [Serializable] public sealed class Report
        {
            public string timestampUtc, scene, action, generatedRoot = RootName, assetFolder;
            public int sourceInstances, activeSourceInstances, activeSourceLod0Triangles, replacementTriangles, generatedColliders;
            public int activeImportedArchInstances, activeImportedArchLod0Triangles, repairedArcades, repairedArcadeOpenings, scannedUniqueWalkwayMeshes;
            public bool allActiveSourcesMapped, allRequiredSpansReady, sourceTransformsPreserved, walkingCollisionPreserved, geometryValidated;
            public bool corbelledTop;
            public List<SourceInfo> sources = new List<SourceInfo>();
            public List<SpanInfo> spans = new List<SpanInfo>();
            public List<MeshIssue> meshIssues = new List<MeshIssue>();
            public string[] unmappedSources = Array.Empty<string>();
            public string method = "World-space triangles from the current active deck underside and the current support meshes. No source transform is moved; source arch instances are only disabled after a closed, fitted replacement is ready. Additional arches require two verified physical bearings. Thin rock-supported landings and existing cloister arcades are retained. Closed two-material meshes use metre-scaled UVs and static layer-0 obstacle collision. Scene saving belongs to the caller.";
        }
        [Serializable] sealed class RouteData { public Vector3[] points; }
        sealed class Surface
        {
            public MeshFilter filter;
            public Vector3[] v;
            public List<int> underside = new List<int>();
            public int[] triangles;
            public Bounds bounds;
            public Vector3 topCenter;
        }
        sealed class BottomPlane
        {
            public Vector3 normal, point;
            public float area;
            public readonly List<int> indices = new List<int>();
        }
        sealed class Bearing
        {
            public string name;
            public Surface solid;
            public Vector3 point;
            public float rockHeight;
            public float springOverride = float.NaN;
            public bool IsRock => solid == null;
        }
        sealed class Plan
        {
            public SpanInfo info;
            public Surface deck;
            public Bearing a, b;
            public Bearing startOverride, endOverride;
            public Vector3 direction, side;
            public Vector3 deckAxis, deckLinePoint;
            public float length, halfWidth, topHalfWidth;
            public float[] stations;
            public Vector2[] upperWidths;
            public Vector3[] topL, topR, lowerL, lowerR;
            public Mesh mesh;
        }
        sealed class Context
        {
            public Scene scene;
            public Transform[] transforms;
            public MeshFilter[] filters;
            public Surface[] decks;
            public Collider[] ground;
            public Vector3[] route;
            public Report report;
            public readonly List<Plan> plans = new List<Plan>();
            public readonly Dictionary<SourceInfo, GameObject> sources = new Dictionary<SourceInfo, GameObject>();
        }
        [Serializable] sealed class FitGeometryDump
        {
            public string project, scene;
            public List<FitGeometryItem> items = new List<FitGeometryItem>();
        }
        [Serializable] sealed class FitGeometryItem
        {
            public string name, path, meshAsset;
            public Vector3 position, scale;
            public Quaternion rotation;
            public float[] worldMatrix;
            public Vector3[] vertices;
            public int[] triangles;
        }
        [Serializable] public sealed class AbutmentHit
        {
            public string source, meshAsset;
            public Vector3 point, normal;
            public float sourceMinY, sourceMaxY;
        }
        [Serializable] public sealed class AbutmentSample
        {
            public string kind;
            public float station, lateral, deckBottom;
            public Vector3 point;
            public bool deckFound;
            public List<AbutmentHit> hits = new List<AbutmentHit>();
        }
        [Serializable] public sealed class SupplementSpan
        {
            public int segment, endNode, candidateMeshes, candidateTriangles;
            public string currentStatus, startSupport, endSupport;
            public Vector3 from, to;
            public List<AbutmentSample> samples = new List<AbutmentSample>();
        }
        [Serializable] public sealed class SupplementReport
        {
            public string project, scene, timestampUtc;
            public List<SupplementSpan> spans = new List<SupplementSpan>();
            public string method = "Read-only actual visible structural LOD0 mesh triangle ray audit, including rock skins, original floor slabs, paving and foundation masonry. Each hit records source and world position; proxies and generated walking meshes are not treated as physical abutments. Endpoints extend only along the already-existing next route, so true cliff/floor edges can be distinguished from a missing collider-name match.";
        }

        public static void AuditSupplementalActive()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.name != "Chapter01_Main") throw new InvalidOperationException("Use the already-open main scene.");
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var filters = transforms.Select(t => t.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh && f.gameObject.activeInHierarchy).ToArray();
            var route = JsonUtility.FromJson<RouteData>(File.ReadAllText("Assets/Chapter01/Runtime/Testing/castle-route.json")).points;
            var c = new Context { scene = scene, filters = filters, route = route, ground = Array.Empty<Collider>(),
                decks = filters.Where(f => f.name.StartsWith("WalkSurface_CW_", StringComparison.Ordinal)).Select(f => Read(f, true)).ToArray() };
            var report = new SupplementReport { project = Directory.GetParent(Application.dataPath).FullName, scene = scene.path, timestampUtc = DateTime.UtcNow.ToString("o") };
            var candidates = filters.Where(StructuralVisual).Select(f => new { filter = f, bounds = WorldBounds(f) }).ToArray();
            foreach (int segment in new[] { 1, 2, 6, 21, 35 })
            {
                var deck = c.decks.SingleOrDefault(d => d.filter.name == "WalkSurface_CW_Segment_" + segment);
                if (deck == null) continue;
                int endNode = segment == 21 ? 23 : segment + 1;
                var span = new SupplementSpan { segment = segment, endNode = endNode,
                    from = LiveNode(deck.filter.transform.parent, route[segment]), to = LiveNode(deck.filter.transform.parent, route[endNode]) };
                report.spans.Add(span);
                if (transforms.Any(t => t.name == "CW_FittedArch_Segment_" + segment.ToString("00") && t.gameObject.activeInHierarchy))
                { span.currentStatus = "Already has an active fitted arch; no duplicate proposed."; continue; }
                var startBearing = FindBearing(c, segment, span.from, 9); var endBearing = FindBearing(c, endNode, span.to, 9);
                span.startSupport = startBearing?.name ?? "No named consolidated pier"; span.endSupport = endBearing?.name ?? "No named consolidated pier";
                var direction = Horizontal(span.to - span.from).normalized; var side = Vector3.Cross(Vector3.up, direction);
                float width = DeckWidth(deck, direction);
                for (int i = 0; i <= 16; i++) foreach (float lateral in new[] { -width * .35f, 0, width * .35f })
                {
                    var point = Vector3.Lerp(span.from, span.to, i / 16f) + side * lateral;
                    span.samples.Add(new AbutmentSample { kind = "span", station = i / 16f, lateral = lateral, point = point });
                }
                if (endNode + 1 < route.Length)
                {
                    var nextGroup = filters.FirstOrDefault(f => f.name == "WalkSurface_CW_Segment_" + endNode)?.transform.parent;
                    Vector3 next = nextGroup ? LiveNode(nextGroup, route[endNode + 1]) : route[endNode + 1];
                    Vector3 extension = Horizontal(next - span.to).normalized;
                    float run = Horizontal(next - span.to).magnitude;
                    for (int i = 0; i <= 20; i++)
                    {
                        float metres = i * .4f; if (metres > run) break;
                        foreach (float lateral in new[] { -width * .32f, 0, width * .32f })
                        {
                            var point = span.to + extension * metres + Vector3.Cross(Vector3.up, extension) * lateral;
                            point.y = Mathf.Lerp(span.to.y, next.y, metres / Mathf.Max(run, .01f));
                            span.samples.Add(new AbutmentSample { kind = "existing next flight", station = metres, lateral = lateral, point = point });
                        }
                    }
                }
                var bounds = new Bounds(span.samples[0].point, Vector3.zero); foreach (var sample in span.samples) bounds.Encapsulate(sample.point);
                var nearby = candidates.Where(item => item.bounds.max.x >= bounds.min.x - .01f && item.bounds.min.x <= bounds.max.x + .01f &&
                    item.bounds.max.z >= bounds.min.z - .01f && item.bounds.min.z <= bounds.max.z + .01f &&
                    item.bounds.min.y <= bounds.max.y + .6f && item.bounds.max.y >= bounds.min.y - 80).Select(item => Read(item.filter, false)).ToArray();
                span.candidateMeshes = nearby.Length; span.candidateTriangles = nearby.Sum(s => s.triangles.Length / 3);
                foreach (var sample in span.samples)
                {
                    sample.deckFound = TryBottom(c, sample.point, sample.point.y - .7f, out sample.deckBottom);
                    foreach (var solid in nearby)
                    {
                        var point = sample.point;
                        if (point.x < solid.bounds.min.x - .0001f || point.x > solid.bounds.max.x + .0001f || point.z < solid.bounds.min.z - .0001f || point.z > solid.bounds.max.z + .0001f) continue;
                        bool found = false; float highest = float.NegativeInfinity; Vector3 chosenNormal = Vector3.zero;
                        for (int i = 0; i < solid.triangles.Length; i += 3)
                        {
                            var a = solid.v[solid.triangles[i]]; var b = solid.v[solid.triangles[i + 1]]; var d = solid.v[solid.triangles[i + 2]];
                            var normal = Vector3.Cross(b - a, d - a).normalized;
                            if (normal.y < .12f || !Height(point, a, b, d, out float y) || y > point.y + .5f || y < point.y - 80 || y <= highest) continue;
                            highest = y; chosenNormal = normal; found = true;
                        }
                        if (found) sample.hits.Add(new AbutmentHit { source = PathOf(solid.filter.transform), meshAsset = AssetDatabase.GetAssetPath(solid.filter.sharedMesh),
                            point = new Vector3(point.x, highest, point.z), normal = chosenNormal, sourceMinY = solid.bounds.min.y, sourceMaxY = solid.bounds.max.y });
                    }
                    sample.hits = sample.hits.OrderByDescending(h => h.point.y).Take(8).ToList();
                }
                span.currentStatus = "Read-only visible triangle audit complete; inspect heights and source thickness before constructing an abutment or arch.";
            }
            Directory.CreateDirectory(ReportFolder); File.WriteAllText(ReportFolder + "/bridge-supplemental-audit.json", JsonUtility.ToJson(report, true));
            Debug.Log("[CastleBridgeArchRepair] Audited " + report.spans.Count + " supplemental spans against actual structural meshes; scene unchanged.");
        }

        static bool StructuralVisual(MeshFilter filter)
        {
            var renderer = filter.GetComponent<Renderer>();
            if (!renderer || !renderer.enabled || filter.name.StartsWith("CW_", StringComparison.Ordinal) ||
                Ancestors(filter.transform).Any(t => t.name == RootName || t.name == "CastleWalkwayUpgrade")) return false;
            string asset = AssetDatabase.GetAssetPath(filter.sharedMesh), name = filter.name;
            if (Regex.IsMatch(name + " " + asset, "Tree|Shrub|Grass|Foliage|Vegetation|Leaf|Window|Roof|Lantern", RegexOptions.IgnoreCase)) return false;
            if (!Regex.IsMatch(name, "Rock|Cliff|Massif|Ridge|Floor|Paving|Foundation|Masonry|Ground|Terrain", RegexOptions.IgnoreCase) &&
                !asset.Contains("/Models/Rocks/") && !asset.Contains("BridgeStairsTerrain")) return false;
            var lod = filter.GetComponentInParent<LODGroup>();
            if (lod)
            {
                var levels = lod.GetLODs();
                if (levels.Length > 0 && levels[0].renderers.Length > 0 && !levels[0].renderers.Contains(renderer)) return false;
            }
            return true;
        }

        public static void InspectSupplementalActive()
        {
            var c = SupplementalPlan();
            WriteReport(c.report, "bridge-supplemental-plan.json"); DisposePlans(c);
            Debug.Log("[CastleBridgeArchRepair] Supplemental rock/foundation plan written; existing scene unchanged.");
        }

        public static void AttachSupplementalActive()
        {
            var c = SupplementalPlan(); WriteReport(c.report, "bridge-supplemental-plan.json");
            if (c.plans.Any(p => !p.mesh || !p.info.closed || !p.info.fitted || !p.info.endContacts))
            { DisposePlans(c); throw new InvalidOperationException("Supplemental rock/foundation contact validation failed; see bridge-supplemental-plan.json. No scene objects were changed."); }
            if (c.plans.Count == 0) { Debug.Log("[CastleBridgeArchRepair] All three supplemental spans already exist; no duplicates added."); return; }
            var root = c.transforms.Single(t => t.name == RootName && t.gameObject.activeInHierarchy);
            var stone = AssetDatabase.LoadAssetAtPath<Material>(StonePath); var trim = AssetDatabase.LoadAssetAtPath<Material>(TrimPath);
            if (!stone || !trim) { DisposePlans(c); throw new InvalidOperationException("Approved arch materials are missing."); }
            var matrices = c.transforms.ToDictionary(t => t, t => t.localToWorldMatrix);
            var parents = c.transforms.ToDictionary(t => t, t => t.parent);
            var walking = c.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true))
                .Where(x => x.name.StartsWith("Collision__CW_", StringComparison.Ordinal)).Select(x => new {
                    collider = x, matrix = x.transform.localToWorldMatrix, enabled = x.enabled,
                    active = x.gameObject.activeInHierarchy, mesh = (x as MeshCollider)?.sharedMesh }).ToArray();
            string folder = AssetRoot + "/Generated/Supplemental-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(folder + "/Meshes"); Directory.CreateDirectory(folder + "/Prefabs"); AssetDatabase.Refresh();
            c.report.assetFolder = folder; c.report.action = "attach supplemental";
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Add rock-seated supplemental bridge arches");
            try
            {
                foreach (var plan in c.plans)
                {
                    string name = "CW_FittedArch_Segment_" + plan.info.segment.ToString("00");
                    if (root.GetComponentsInChildren<Transform>(true).Any(t => t.name == name))
                        throw new InvalidOperationException("Refused to duplicate an existing fitted span: " + name);
                    var mesh = plan.mesh; var pivot = mesh.bounds.center; var vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] -= pivot;
                    mesh.vertices = vertices; mesh.RecalculateBounds(); mesh.name = name;
                    string meshPath = folder + "/Meshes/" + name + ".asset"; AssetDatabase.CreateAsset(mesh, meshPath); plan.mesh = null;
                    var group = new GameObject(name); group.transform.SetParent(root, false); group.transform.position = pivot;
                    Undo.RegisterCreatedObjectUndo(group, "Add verified missing bridge arch");
                    var visual = new GameObject(name + "_Visual", typeof(MeshFilter), typeof(MeshRenderer)); visual.transform.SetParent(group.transform, false);
                    visual.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = visual.GetComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { stone, trim };
                    renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                    var obstacle = new GameObject("CW_Obstacle_FittedArch_Segment_" + plan.info.segment.ToString("00")); obstacle.transform.SetParent(group.transform, false); obstacle.layer = 0;
                    var collision = obstacle.AddComponent<MeshCollider>(); collision.sharedMesh = mesh; collision.convex = false;
                    collision.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning |
                        MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
                    string prefab = folder + "/Prefabs/" + name + ".prefab";
                    PrefabUtility.SaveAsPrefabAssetAndConnect(group, prefab, InteractionMode.AutomatedAction, out bool saved);
                    if (!saved) throw new InvalidOperationException("Could not save supplemental arch prefab: " + prefab);
                    plan.info.meshAsset = meshPath; plan.info.prefabAsset = prefab; plan.info.built = true; plan.info.status = "Built";
                    c.report.generatedColliders++;
                }
                Physics.SyncTransforms();
                c.report.sourceTransformsPreserved = matrices.All(kv => kv.Key && Same(kv.Value, kv.Key.localToWorldMatrix) && kv.Key.parent == parents[kv.Key]);
                c.report.walkingCollisionPreserved = walking.All(x => x.collider && x.collider.enabled == x.enabled && x.collider.gameObject.activeInHierarchy == x.active &&
                    Same(x.matrix, x.collider.transform.localToWorldMatrix) && (x.collider as MeshCollider)?.sharedMesh == x.mesh);
                c.report.geometryValidated = c.plans.All(p => p.info.closed && p.info.fitted && p.info.endContacts);
                if (!c.report.sourceTransformsPreserved || !c.report.walkingCollisionPreserved || !c.report.geometryValidated)
                    throw new InvalidOperationException("Supplemental preservation/geometry verification failed.");
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(c.scene); Undo.CollapseUndoOperations(undo);
                WriteReport(c.report, "bridge-supplemental-applied.json");
                Debug.Log("[CastleBridgeArchRepair] Added " + c.report.generatedColliders + " verified rock/foundation arches without new piers or route edits. Scene remains unsaved.");
            }
            catch { Undo.RevertAllDownToGroup(undo); WriteReport(c.report, "bridge-supplemental-failed.json"); throw; }
            finally { DisposePlans(c); }
        }

        static Context SupplementalPlan()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Supplemental fitting requires edit mode.");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.name != "Chapter01_Main") throw new InvalidOperationException("Use the already-open main scene.");
            var c = new Context { scene = scene, route = JsonUtility.FromJson<RouteData>(File.ReadAllText("Assets/Chapter01/Runtime/Testing/castle-route.json")).points,
                ground = Array.Empty<Collider>(), report = new Report { scene = scene.path, timestampUtc = DateTime.UtcNow.ToString("o"), action = "inspect supplemental", corbelledTop = true,
                    allActiveSourcesMapped = true, method = "Only spans 6,21,35 are appended to the existing repair root. Actual retained rock/foundation mesh cross-sections replace incomplete collision proxies as the end bearing. Queries retain only real source triangles crossing the bearing plane and relevant height range. No source transforms, existing arches, floors, piers or collision are replaced. Low terrain stairs 1/2 are explicitly outside this high-bridge supplement." } };
            c.transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            if (!c.transforms.Any(t => t.name == RootName && t.gameObject.activeInHierarchy)) throw new InvalidOperationException("Apply the main fitted-arch repair before attaching supplemental spans.");
            c.filters = c.transforms.Select(t => t.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh && f.gameObject.activeInHierarchy).ToArray();
            c.decks = c.filters.Where(f => f.name.StartsWith("WalkSurface_CW_", StringComparison.Ordinal)).Select(f => Read(f, true)).Where(s => s.underside.Count > 0).ToArray();
            bool previous = UseCorbelledTop; UseCorbelledTop = true;
            try
            {
                foreach (int segment in new[] { 6, 21, 35 })
                {
                    int endNode = segment == 21 ? 23 : segment + 1;
                    var info = new SpanInfo { segment = segment, endNode = endNode, status = "Blocked" }; c.report.spans.Add(info);
                    if (c.transforms.Any(t => t.name == "CW_FittedArch_Segment_" + segment.ToString("00")))
                    { info.status = "Retained existing"; info.reason = "Existing fitted span found; duplicate generation refused."; continue; }
                    var deck = c.decks.SingleOrDefault(s => s.filter.name == "WalkSurface_CW_Segment_" + segment);
                    var plan = new Plan { deck = deck, info = info }; c.plans.Add(plan);
                    try
                    {
                        if (deck == null) throw new InvalidOperationException("Missing current flight mesh.");
                        info.deck = PathOf(deck.filter.transform);
                        var a = LiveNode(deck.filter.transform.parent, c.route[segment]); var b = LiveNode(deck.filter.transform.parent, c.route[endNode]);
                        var bearing = FindBearing(c, segment, a, 9);
                        if (bearing == null) throw new InvalidOperationException("The existing start pier is missing.");
                        var direction = Horizontal(b - bearing.point).normalized; var side = Vector3.Cross(Vector3.up, direction);
                        float bottom = Bottom(c, b, b.y - .7f);
                        string[] names = segment == 6 ? new[] { "Outer_Bailey_Rock_Cliff", "Outer_Bailey_Rock_Top", "E01c__Outer_Bailey_Rock_Crest_0_1__0", "E01c__Outer_Bailey_Rock_Crest_1_0__0",
                            "E01a__Outer_Bailey_Rock_Wall_0_1_0__0", "E01a__Outer_Bailey_Rock_Wall_1_0_0__0" } :
                            segment == 21 ? new[] { "E01c__Chapel_Rock_Crest_2_0__0", "E01c__Chapel_Rock_Crest_2_1__0", "E01a__Chapel_Rock_Wall_2_0_0__0", "E01a__Chapel_Rock_Wall_2_1_0__0" } :
                            new[] { "Foundation__Boss_Porch", "C02__Boss_Porch__0" };
                        var solids = names.Select(name => c.filters.SingleOrDefault(f => f.name == name)).ToArray();
                        if (solids.Any(f => !f)) throw new InvalidOperationException("A previously audited abutment source is missing: " + string.Join(", ", names.Where((n, i) => !solids[i])));
                        var union = BearingSection(solids, b, direction, bottom - 10, bottom + .5f);
                        info.abutmentMeshes = solids.Length; info.abutmentSectionTriangles = union.triangles.Length / 3;
                        float depth = Mathf.Clamp(Horizontal(b - bearing.point).magnitude * .30f, 3.5f, 8);
                        float chosen = float.NaN;
                        foreach (float d in new[] { depth, depth * .8f, depth * .6f, 2f, 1.2f, .85f })
                        {
                            float height = bottom - d;
                            if (height <= union.bounds.min.y + .05f || height >= union.bounds.max.y - .05f) continue;
                            if (SolidInterval(union, AtY(b, height), side, out float lo, out float hi) && Mathf.Min(-lo, hi) >= 1.05f)
                            { chosen = height; break; }
                        }
                        if (float.IsNaN(chosen)) throw new InvalidOperationException("No sufficiently wide physical rock/foundation cross-section at the current landing.");
                        plan.startOverride = bearing;
                        plan.endOverride = new Bearing { name = "Actual mesh union: " + string.Join(" + ", solids.Select(f => PathOf(f.transform))),
                            point = b, solid = union, springOverride = chosen };
                        Fit(c, plan);
                        info.reason += " Existing native abutment mesh triangles were verified directly; no new pier or terrain change.";
                    }
                    catch (Exception e) { info.status = "Blocked"; info.reason = e.Message; if (plan.mesh) UnityEngine.Object.DestroyImmediate(plan.mesh); plan.mesh = null; }
                }
            }
            finally { UseCorbelledTop = previous; }
            c.report.replacementTriangles = c.plans.Where(p => p.mesh).Sum(p => p.info.triangles);
            c.report.geometryValidated = c.report.allRequiredSpansReady = c.plans.All(p => p.mesh && p.info.closed && p.info.fitted && p.info.endContacts);
            return c;
        }

        static Surface BearingSection(MeshFilter[] sources, Vector3 anchor, Vector3 forward, float minimumY, float maximumY)
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            var bounds = WorldBounds(sources[0]);
            foreach (var source in sources)
            {
                var mesh = source.sharedMesh; var points = mesh.vertices.Select(source.transform.TransformPoint).ToArray(); var triangles = mesh.triangles;
                bounds.Encapsulate(WorldBounds(source));
                // All fitting rays are coplanar with this landing's bearing section.
                // Keep the exact intersecting triangles instead of rescaning millions
                // of unrelated rock faces for each foot or spring-height candidate.
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = points[triangles[i]]; var b = points[triangles[i + 1]]; var d = points[triangles[i + 2]];
                    if (Mathf.Max(a.y, Mathf.Max(b.y, d.y)) < minimumY || Mathf.Min(a.y, Mathf.Min(b.y, d.y)) > maximumY) continue;
                    float da = Vector3.Dot(a - anchor, forward), db = Vector3.Dot(b - anchor, forward), dc = Vector3.Dot(d - anchor, forward);
                    if (Mathf.Min(da, Mathf.Min(db, dc)) > .0001f || Mathf.Max(da, Mathf.Max(db, dc)) < -.0001f) continue;
                    int first = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(d); indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
                }
            }
            if (indices.Count == 0) throw new InvalidOperationException("No actual source triangles cross the proposed abutment section.");
            return new Surface { filter = sources[0], v = vertices.ToArray(), triangles = indices.ToArray(), bounds = bounds, topCenter = anchor };
        }

        public static void DumpActiveFitGeometry()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.name != "Chapter01_Main") throw new InvalidOperationException("Use the already-open main scene.");
            var dump = new FitGeometryDump { project = Directory.GetParent(Application.dataPath).FullName, scene = scene.path };
            var filters = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true));
            foreach (var f in filters.Where(f => f && f.sharedMesh && f.gameObject.activeInHierarchy &&
                (Regex.IsMatch(f.name, "^WalkSurface_CW_(?:Segment|Platform)_(?:2[789]|3[0-5])$") || f.name == "WalkSurface_CW_LandingCluster_33_34" ||
                 Regex.IsMatch(f.name, "^CW_Support_(?:UpperBridge_Junction_30|Rift_Bridge_Pier_3[1-5])_Visual$"))))
            {
                bool deck = f.name.StartsWith("WalkSurface_CW_"); var s = Read(f, deck);
                var triangles = deck ? s.underside.ToArray() : s.triangles;
                var used = triangles.Distinct().ToArray(); var map = used.Select((n, i) => new { n, i }).ToDictionary(x => x.n, x => x.i);
                var matrix = f.transform.localToWorldMatrix; var numbers = new float[16]; for (int i = 0; i < 16; i++) numbers[i] = matrix[i];
                dump.items.Add(new FitGeometryItem { name = f.name, path = PathOf(f.transform), meshAsset = AssetDatabase.GetAssetPath(f.sharedMesh),
                    position = f.transform.position, scale = f.transform.lossyScale, rotation = f.transform.rotation, worldMatrix = numbers,
                    vertices = used.Select(i => s.v[i]).ToArray(), triangles = triangles.Select(i => map[i]).ToArray() });
            }
            Directory.CreateDirectory(ReportFolder); File.WriteAllText(ReportFolder + "/bridge-fit-geometry.json", JsonUtility.ToJson(dump, true));
            Debug.Log("[CastleBridgeArchRepair] Dumped " + dump.items.Count + " current world-space surfaces for fitting diagnostics.");
        }

        public static void InspectActive()
        {
            var c = Inspect();
            WriteReport(c.report, "bridge-arches-inspect.json");
            DisposePlans(c);
            Debug.Log("[CastleBridgeArchRepair] Inspected active scene: " + c.report.activeImportedArchInstances + " imported arch instances; " +
                c.report.spans.Count(s => s.status == "Ready") + " ready fitted/repaired groups; " + c.report.meshIssues.Count +
                " invalid shared meshes; see " + ReportFolder + "/bridge-arches-inspect.json");
        }

        // Optional reviewed variant: spring feet still sit inside the true piers,
        // while one continuous sloped masonry face carries the wider road underside.
        public static void InspectCorbelledActive()
        { UseCorbelledTop = true; try { InspectActive(); } finally { UseCorbelledTop = false; } }
        public static void ApplyCorbelledActive()
        { UseCorbelledTop = true; try { ApplyActive(); } finally { UseCorbelledTop = false; } }

        public static void ApplyActive()
        {
            var c = Inspect();
            c.report.action = "apply";
            WriteReport(c.report, "bridge-arches-preapply.json");
            if (c.transforms.Any(t => t.name == RootName))
            { DisposePlans(c); throw new InvalidOperationException("Arch repair root already exists. Inspect its manual edits before explicitly undoing/replacing that one generated root."); }
            if (!c.report.allActiveSourcesMapped || !c.report.allRequiredSpansReady)
            { DisposePlans(c); throw new InvalidOperationException("An active source arch has no geometrically validated replacement. See bridge-arches-preapply.json; the scene was not changed."); }
            var stone = AssetDatabase.LoadAssetAtPath<Material>(StonePath);
            var trim = AssetDatabase.LoadAssetAtPath<Material>(TrimPath);
            if (!stone || !trim) { DisposePlans(c); throw new InvalidOperationException("Approved stone and trim materials are required."); }
            var oldMatrices = c.transforms.ToDictionary(t => t, t => t.localToWorldMatrix);
            var oldParents = c.transforms.ToDictionary(t => t, t => t.parent);
            var walking = c.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true))
                .Where(x => x.name.StartsWith("Collision__CW_", StringComparison.Ordinal)).ToArray();
            var walkState = walking.Select(x => new { collider = x, matrix = x.transform.localToWorldMatrix,
                enabled = x.enabled, active = x.gameObject.activeInHierarchy, mesh = (x as MeshCollider)?.sharedMesh }).ToArray();
            string run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            string folder = AssetRoot + "/Generated/" + run;
            Directory.CreateDirectory(folder + "/Meshes"); Directory.CreateDirectory(folder + "/Prefabs");
            AssetDatabase.Refresh();
            c.report.assetFolder = folder;
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Repair castle bridge arches");
            GameObject root = null;
            try
            {
                root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, c.scene);
                Undo.RegisterCreatedObjectUndo(root, "Create fitted bridge arches");
                foreach (var plan in c.plans.Where(p => p.mesh && p.info.fitted && p.info.closed && p.info.endContacts))
                {
                    string name = "CW_FittedArch_Segment_" + plan.info.segment.ToString("00");
                    var mesh = plan.mesh; var pivot = mesh.bounds.center;
                    var vertices = mesh.vertices; for (int i = 0; i < vertices.Length; i++) vertices[i] -= pivot;
                    mesh.vertices = vertices; mesh.RecalculateBounds(); mesh.name = name;
                    string asset = folder + "/Meshes/" + name + ".asset";
                    AssetDatabase.CreateAsset(mesh, asset); plan.mesh = null;
                    var group = new GameObject(name); group.transform.SetParent(root.transform, false); group.transform.position = pivot;
                    var visual = new GameObject(name + "_Visual", typeof(MeshFilter), typeof(MeshRenderer));
                    visual.transform.SetParent(group.transform, false); visual.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = visual.GetComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { stone, trim };
                    renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                    var obstacle = new GameObject("CW_Obstacle_FittedArch_Segment_" + plan.info.segment.ToString("00"));
                    obstacle.transform.SetParent(group.transform, false); obstacle.layer = 0;
                    var collision = obstacle.AddComponent<MeshCollider>(); collision.sharedMesh = mesh; collision.convex = false;
                    collision.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning |
                        MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
                    string prefab = folder + "/Prefabs/" + name + ".prefab";
                    PrefabUtility.SaveAsPrefabAssetAndConnect(group, prefab, InteractionMode.AutomatedAction, out bool saved);
                    if (!saved) throw new InvalidOperationException("Could not create fitted arch prefab " + prefab);
                    plan.info.meshAsset = asset; plan.info.prefabAsset = prefab; plan.info.built = true; plan.info.status = "Built";
                    c.report.generatedColliders++;
                }
                foreach (var entry in c.sources.Where(kv => kv.Key.active && kv.Key.assignedSegment >= 0))
                {
                    var replacement = c.plans.Single(p => p.info.segment == entry.Key.assignedSegment);
                    if (!replacement.info.built) throw new InvalidOperationException("Required replacement was not built: " + entry.Key.name);
                    Undo.RecordObject(entry.Value, "Disable superseded arch instance"); entry.Value.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(entry.Value); entry.Key.disabled = true;
                }
                Physics.SyncTransforms();
                c.report.sourceTransformsPreserved = oldMatrices.All(kv => kv.Key && Same(kv.Value, kv.Key.localToWorldMatrix) && kv.Key.parent == oldParents[kv.Key]);
                c.report.walkingCollisionPreserved = walkState.All(x => x.collider && x.enabled == x.collider.enabled &&
                    x.active == x.collider.gameObject.activeInHierarchy && Same(x.matrix, x.collider.transform.localToWorldMatrix) &&
                    x.mesh == (x.collider as MeshCollider)?.sharedMesh);
                c.report.geometryValidated = c.plans.Where(p => p.info.built).All(p => p.info.closed && p.info.fitted && p.info.endContacts);
                if (!c.report.sourceTransformsPreserved || !c.report.walkingCollisionPreserved || !c.report.geometryValidated)
                    throw new InvalidOperationException("Post-apply preservation/geometry validation failed.");
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(c.scene);
                Undo.CollapseUndoOperations(undo); WriteReport(c.report, "bridge-arches-applied.json");
                Debug.Log("[CastleBridgeArchRepair] Applied " + c.report.generatedColliders + " closed fitted arches. Scene remains open and unsaved.");
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo); WriteReport(c.report, "bridge-arches-failed.json"); throw;
            }
            finally { DisposePlans(c); }
        }

        static Context Inspect()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run arch repair in edit mode.");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.name != "Chapter01_Main")
                throw new InvalidOperationException("The current active scene must be the already-open Chapter01_Main. This helper never opens a scene.");
            string routePath = "Assets/Chapter01/Runtime/Testing/castle-route.json";
            var route = JsonUtility.FromJson<RouteData>(File.ReadAllText(routePath)).points;
            if (route == null || route.Length != 41) throw new InvalidOperationException("Missing route reference metadata.");
            var c = new Context { scene = scene, route = route, report = new Report { scene = scene.path,
                timestampUtc = DateTime.UtcNow.ToString("o"), action = "inspect", corbelledTop = UseCorbelledTop } };
            c.transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            c.filters = c.transforms.Select(t => t.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh).ToArray();
            c.decks = c.filters.Where(f => f.gameObject.activeInHierarchy && f.name.StartsWith("WalkSurface_CW_", StringComparison.Ordinal))
                .Select(f => Read(f, true)).Where(s => s.underside.Count > 0).ToArray();
            c.ground = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Where(x =>
                x.enabled && x.gameObject.activeInHierarchy && x.name.StartsWith("Collision__", StringComparison.Ordinal) &&
                !x.name.StartsWith("Collision__CW_", StringComparison.Ordinal) &&
                Regex.IsMatch(x.name, "Rock|Massif|Ridge|Floor|Ground|Paving", RegexOptions.IgnoreCase)).ToArray();
            Physics.SyncTransforms();
            foreach (var group in c.filters.Where(f => f.gameObject.activeInHierarchy && Ancestors(f.transform).Any(t => t.name == "CastleWalkwayUpgrade"))
                .GroupBy(f => f.sharedMesh))
            {
                c.report.scannedUniqueWalkwayMeshes++;
                var mesh = group.Key; var vertices = mesh.vertices; var bad = vertices.Select(v => !Finite(v)).ToArray();
                int badVertices = bad.Count(x => x), badNormals = mesh.normals.Count(v => !Finite(v)),
                    badTangents = mesh.tangents.Count(v => !Finite(v.x) || !Finite(v.y) || !Finite(v.z) || !Finite(v.w));
                bool boundsFinite = Finite(mesh.bounds.center) && Finite(mesh.bounds.extents);
                if (badVertices > 0 || badNormals > 0 || badTangents > 0 || !boundsFinite)
                {
                    var tris = mesh.triangles; int affected = 0;
                    for (int i = 0; i < tris.Length; i += 3) if (bad[tris[i]] || bad[tris[i + 1]] || bad[tris[i + 2]]) affected++;
                    c.report.meshIssues.Add(new MeshIssue { meshAsset = AssetDatabase.GetAssetPath(mesh), exampleOwner = PathOf(group.First().transform),
                        vertices = vertices.Length, nonFiniteVertices = badVertices, nonFiniteNormals = badNormals, nonFiniteTangents = badTangents,
                        affectedTriangles = affected, finiteBounds = boundsFinite });
                }
            }
            foreach (var t in c.transforms.Where(t => OldArch.IsMatch(t.name) && !Ancestors(t).Any(a => OldArch.IsMatch(a.name))))
            {
                var f = t.GetComponent<MeshFilter>() ?? t.GetComponentInChildren<MeshFilter>(true);
                if (!f || !f.sharedMesh) continue;
                var bounds = WorldBounds(f); var info = new SourceInfo { name = t.name, path = PathOf(t),
                    meshAsset = AssetDatabase.GetAssetPath(f.sharedMesh), center = bounds.center, size = bounds.size,
                    lod0Triangles = Triangles(f.sharedMesh), active = t.gameObject.activeInHierarchy };
                c.report.sources.Add(info); c.sources[info] = t.gameObject;
            }
            c.report.activeImportedArchInstances = c.report.sources.Count(s => s.active);
            c.report.activeImportedArchLod0Triangles = c.report.sources.Where(s => s.active).Sum(s => s.lod0Triangles);
            foreach (var deck in c.decks.Where(d => Segment.IsMatch(d.filter.name)).OrderBy(d => int.Parse(Segment.Match(d.filter.name).Groups[1].Value)))
            {
                int segment = int.Parse(Segment.Match(deck.filter.name).Groups[1].Value);
                if (segment >= 36) continue;
                int endNode = segment == 21 ? 23 : segment + 1;
                var info = new SpanInfo { segment = segment, endNode = endNode, deck = PathOf(deck.filter.transform), status = "Retained" };
                c.report.spans.Add(info);
                if (!Bearings.ContainsKey(segment) && !Bearings.ContainsKey(endNode))
                { info.reason = "No free-standing pier at either end; this ground/courtyard section is outside the arch-support network."; continue; }
                var plan = new Plan { deck = deck, info = info };
                c.plans.Add(plan);
                try { Fit(c, plan); }
                catch (Exception e) { info.status = "Blocked"; info.reason = e.Message; if (plan.mesh) UnityEngine.Object.DestroyImmediate(plan.mesh); plan.mesh = null; }
            }
            foreach (var source in c.report.sources.Where(s => s.active))
            {
                var closest = c.plans.Select(p => new { plan = p, distance = DistanceToSpan(source.center, p.info.from, p.info.to) })
                    .Where(x => (x.plan.info.to - x.plan.info.from).sqrMagnitude > 1).OrderBy(x => x.distance).FirstOrDefault();
                if (closest != null && closest.distance <= Mathf.Max(4.5f, source.size.y * .5f))
                { source.assignedSegment = closest.plan.info.segment; source.assignmentDistance = closest.distance; }
            }
            for (int row = 0; row < 2; row++)
            {
                string name = "CW_Support_Cloister_Longitudinal_Arcade_" + row;
                var source = c.filters.SingleOrDefault(f => f.name == name + "_Visual" && f.gameObject.activeInHierarchy);
                if (!source) continue;
                var sourceInfo = new SourceInfo { name = name, path = PathOf(source.transform.parent), meshAsset = AssetDatabase.GetAssetPath(source.sharedMesh),
                    active = true, lod0Triangles = Triangles(source.sharedMesh), assignedSegment = 100 + row };
                c.report.sources.Add(sourceInfo); c.sources[sourceInfo] = source.transform.parent.gameObject;
                var plan = new Plan { info = new SpanInfo { segment = 100 + row, endNode = -1, deck = "Cloister_Floor", status = "Blocked" } };
                c.plans.Add(plan); c.report.spans.Add(plan.info);
                try { RepairArcade(c, source, row, plan); sourceInfo.center = plan.mesh.bounds.center; sourceInfo.size = plan.mesh.bounds.size;
                    c.report.repairedArcades++; c.report.repairedArcadeOpenings += 3; }
                catch (Exception e) { plan.info.reason = e.Message; if (plan.mesh) UnityEngine.Object.DestroyImmediate(plan.mesh); plan.mesh = null; }
            }
            foreach (var p in c.plans) p.info.replacedSources = c.report.sources.Where(s => s.active && s.assignedSegment == p.info.segment).Select(s => s.name).ToArray();
            c.report.unmappedSources = c.report.sources.Where(s => s.active && s.assignedSegment < 0).Select(s => s.name).ToArray();
            c.report.sourceInstances = c.report.sources.Count;
            c.report.activeSourceInstances = c.report.sources.Count(s => s.active);
            c.report.activeSourceLod0Triangles = c.report.sources.Where(s => s.active).Sum(s => s.lod0Triangles);
            c.report.replacementTriangles = c.plans.Where(p => p.mesh).Sum(p => p.info.triangles);
            c.report.allActiveSourcesMapped = c.report.unmappedSources.Length == 0;
            c.report.allRequiredSpansReady = c.plans.Where(p => p.info.replacedSources.Length > 0).All(p => p.mesh && p.info.fitted && p.info.closed && p.info.endContacts);
            c.report.geometryValidated = c.plans.Where(p => p.mesh).All(p => p.info.fitted && p.info.closed && p.info.endContacts);
            return c;
        }

        static void Fit(Context c, Plan p)
        {
            var info = p.info;
            // A lower balcony must not accidentally satisfy a ray that missed this
            // bridge. Only this current segment and its own landing meshes can carry
            // its arch. The 33/34 landing is deliberately a shared existing mesh.
            var names = new HashSet<string> { p.deck.filter.name, "WalkSurface_CW_Platform_" + info.segment,
                "WalkSurface_CW_Platform_" + info.endNode };
            if (info.segment == 32 || info.segment == 34) names.Add("WalkSurface_CW_LandingCluster_33_34");
            c = new Context { scene = c.scene, filters = c.filters, ground = c.ground, route = c.route,
                decks = c.decks.Where(d => names.Contains(d.filter.name)).ToArray() };
            // Prefab-local reference points only locate the current landing. All fitted
            // heights, widths and bearing tests below use the live scene's triangles.
            Vector3 nominalA = LiveNode(p.deck.filter.transform.parent, c.route[info.segment]);
            Vector3 nominalB = LiveNode(p.deck.filter.transform.parent, c.route[info.endNode]);
            p.deckAxis = Horizontal(nominalB - nominalA).normalized;
            var floorVertices = p.deck.underside.Select(i => p.deck.v[i]).Distinct().ToArray();
            p.deckLinePoint = floorVertices.Aggregate(Vector3.zero, (sum, v) => sum + v) / floorVertices.Length;
            float approximateWidth = DeckWidth(p.deck, nominalB - nominalA);
            p.a = p.startOverride ?? FindBearing(c, info.segment, nominalA, approximateWidth);
            p.b = p.endOverride ?? FindBearing(c, info.endNode, nominalB, approximateWidth);
            info.from = p.a != null ? p.a.point : nominalA; info.to = p.b != null ? p.b.point : nominalB;
            info.startBearing = p.a?.name ?? "unverified"; info.endBearing = p.b?.name ?? "unverified";
            if (p.a == null || p.b == null) throw new InvalidOperationException("No verified solid bearing at " + (p.a == null ? "start" : "end") + "; no floating arch is generated.");
            p.direction = Horizontal(p.b.point - p.a.point).normalized; p.side = Vector3.Cross(Vector3.up, p.direction);
            p.length = Horizontal(p.b.point - p.a.point).magnitude;
            if (p.length < 5) { info.status = "Retained"; info.reason = "Short landing, already carried directly by adjacent supports."; return; }
            float ya = Bottom(c, p.a.point, nominalA.y - .7f), yb = Bottom(c, p.b.point, nominalB.y - .7f);
            p.a.point.y = ya; p.b.point.y = yb; info.from = p.a.point; info.to = p.b.point;
            float depth = Mathf.Clamp(p.length * .30f, 3.5f, 8f);
            info.springStart = Spring(p.a, ya, depth); info.springEnd = Spring(p.b, yb, depth);
            if (Mathf.Min(ya - info.springStart, yb - info.springEnd) < .58f)
                throw new InvalidOperationException("Bearing has less than 0.58 m vertical room beneath the deck; retain the direct bearing instead of forcing an arch.");
            float leftRoom = BearingHalfWidth(p.a, p.side, info.springStart);
            float rightRoom = BearingHalfWidth(p.b, p.side, info.springEnd);
            p.halfWidth = Mathf.Min(approximateWidth * .5f - .12f, leftRoom - .10f, rightRoom - .10f);
            if (p.halfWidth < .65f) throw new InvalidOperationException("Current bearing cross-section is too narrow for a closed masonry arch.");
            if (!LineCovered(c, p.a.point, p.b.point))
                throw new InvalidOperationException("The current deck has an uncovered interval between the actual bearings; no floating arch is generated.");
            info.width = p.halfWidth * 2; info.crownDepth = Mathf.Min(.78f, Mathf.Min(ya - info.springStart, yb - info.springEnd) * .65f);
            p.topHalfWidth = UseCorbelledTop ? Mathf.Max(p.halfWidth, approximateWidth * .5f - .15f) : p.halfWidth;
            FitUpperProfile(c, p);
            info.upperWidth = p.upperWidths.Max(w => w.x + w.y);
            info.minimumUpperWidth = p.upperWidths.Min(w => w.x + w.y);
            int count = p.stations.Length;
            p.topL = new Vector3[count]; p.topR = new Vector3[count]; p.lowerL = new Vector3[count]; p.lowerR = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = p.stations[i] / p.length;
                var center = Vector3.Lerp(p.a.point, p.b.point, t);
                float raised = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(Mathf.PI * t)), .72f);
                float centerTop = Bottom(c, center, center.y);
                float springLine = Mathf.Lerp(info.springStart, info.springEnd, t);
                float lower = Mathf.Lerp(springLine, centerTop - info.crownDepth, raised);
                var left = center - p.side * p.upperWidths[i].x; var right = center + p.side * p.upperWidths[i].y;
                left.y = Bottom(c, left, center.y) + Embed; right.y = Bottom(c, right, center.y) + Embed;
                p.topL[i] = left; p.topR[i] = right;
                p.lowerL[i] = AtY(center - p.side * p.halfWidth, lower); p.lowerR[i] = AtY(center + p.side * p.halfWidth, lower);
                if (Mathf.Min(left.y, right.y) - lower < .32f) throw new InvalidOperationException("Arch opening intersects the live deck underside.");
            }
            var geo = new Geo(); float band = Mathf.Min(.28f, info.crownDepth * .40f);
            for (int i = 0; i < count - 1; i++)
            {
                var al = p.lowerL[i]; var bl = p.lowerL[i + 1]; var ar = p.lowerR[i]; var br = p.lowerR[i + 1];
                var alBand = al + Vector3.up * band; var blBand = bl + Vector3.up * band;
                var arBand = ar + Vector3.up * band; var brBand = br + Vector3.up * band;
                geo.Quad(al, bl, blBand, alBand, -p.side, 1);
                geo.Quad(alBand, blBand, p.topL[i + 1], p.topL[i], -p.side, 0);
                geo.Quad(ar, arBand, brBand, br, p.side, 1);
                geo.Quad(arBand, p.topR[i], p.topR[i + 1], brBand, p.side, 0);
                geo.Quad(al, ar, br, bl, Vector3.down, 1);
                geo.Quad(p.topL[i], p.topL[i + 1], p.topR[i + 1], p.topR[i], Vector3.up, 0);
            }
            foreach (int i in new[] { 0, count - 1 })
            {
                Vector3 desired = i == 0 ? -p.direction : p.direction;
                geo.Quad(p.lowerL[i], p.lowerR[i], p.lowerR[i] + Vector3.up * band, p.lowerL[i] + Vector3.up * band, desired, 1);
                geo.Quad(p.lowerL[i] + Vector3.up * band, p.lowerR[i] + Vector3.up * band, p.topR[i], p.topL[i], desired, 0);
            }
            p.mesh = geo.Make("FittedArch_" + info.segment);
            CheckTopology(p.mesh, out info.boundaryEdges, out info.nonManifoldEdges, out info.volume);
            info.triangles = Triangles(p.mesh); info.closed = info.boundaryEdges == 0 && info.nonManifoldEdges == 0 && info.volume > .01;
            info.maximumTopSeam = 0;
            for (int i = 0; i < count - 1; i++) for (int sub = 0; sub <= 2; sub++) foreach (bool right in new[] { false, true })
            {
                var v = right ? p.topR : p.topL; var point = Vector3.Lerp(v[i], v[i + 1], sub / 2f);
                float actual = Bottom(c, point, point.y); info.maximumTopSeam = Mathf.Max(info.maximumTopSeam, Mathf.Abs(point.y - actual)); info.topSamples++;
            }
            info.minimumEndEmbed = Mathf.Min(leftRoom - p.halfWidth, rightRoom - p.halfWidth);
            info.endContacts = EndContact(c, p.a, p.side, info.springStart, p.halfWidth) && EndContact(c, p.b, p.side, info.springEnd, p.halfWidth);
            info.fitted = info.maximumTopSeam <= .035f;
            if (!info.closed || !info.endContacts || !info.fitted)
                throw new InvalidOperationException("Geometry check failed: closed=" + info.closed + ", endContacts=" + info.endContacts + ", seam=" + info.maximumTopSeam);
            info.status = "Ready";
            info.reason = "Closed arch matches current underside to 0.025 m embed; spring cross-sections sit inside both existing bearings. No source transform changes.";
            if (UseCorbelledTop) info.reason += " Continuous upper side faces follow each measured deck cross-section, including asymmetric manual pier offsets; the lower feet retain the measured pier width.";
        }

        static Bearing FindBearing(Context c, int node, Vector3 nominal, float width)
        {
            if (Bearings.TryGetValue(node, out string suffix))
            {
                string name = "CW_Support_" + suffix;
                var f = c.filters.SingleOrDefault(x => x.name == name + "_Visual" && x.gameObject.activeInHierarchy);
                if (f)
                {
                    var solid = Read(f, false);
                    if (Horizontal(solid.topCenter - nominal).magnitude > Mathf.Max(8, width * 1.5f))
                        throw new InvalidOperationException("Current support moved outside its deck bearing: " + name);
                    return new Bearing { name = PathOf(f.transform), point = solid.topCenter, solid = solid };
                }
            }
            if (Ground(c, nominal, nominal.y, out var hit) && hit.point.y >= nominal.y - 1.4f)
                return new Bearing { name = hit.collider.name + " (rock abutment)", point = nominal, rockHeight = hit.point.y };
            return null;
        }

        static void RepairArcade(Context c, MeshFilter source, int row, Plan p)
        {
            var mesh = source.sharedMesh; var local = mesh.vertices;
            if (local.Any(v => !Finite(v.x) || !Finite(v.z)))
                throw new InvalidOperationException("Arcade horizontal coordinates are corrupt; cannot infer its three original spans.");
            var finite = local.Where(v => Finite(v)).ToArray();
            if (finite.Length == 0) throw new InvalidOperationException("Arcade contains no finite reference vertices.");
            float spring = finite.Min(v => v.y), top = finite.Max(v => v.y), left = finite.Min(v => v.x), right = finite.Max(v => v.x);
            int repaired = 0;
            // Each original opening starts at its spring height. The same height is
            // the exact t=1 limit; only that endpoint was damaged by Pow(negative,.72).
            for (int i = 0; i < local.Length; i++) if (!Finite(local[i].y)) { local[i].y = spring; repaired++; }
            var levels = local.Select(v => Mathf.Round(v.z * 10000) / 10000).Distinct().OrderBy(z => z).ToArray();
            var spacings = levels.Skip(1).Select((z, i) => z - levels[i]).Where(d => d > .001f).OrderBy(d => d).ToArray();
            float breakGap = spacings[spacings.Length / 2] * 2;
            var spans = new List<Vector2>(); float start = levels[0];
            for (int i = 1; i < levels.Length; i++) if (levels[i] - levels[i - 1] > breakGap)
            { spans.Add(new Vector2(start, levels[i - 1])); start = levels[i]; }
            spans.Add(new Vector2(start, levels[levels.Length - 1]));
            if (spans.Count != 3) throw new InvalidOperationException("Expected three independent retained cloister openings; found " + spans.Count);
            var geo = new Geo();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var indices = mesh.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = local[indices[i]]; var b = local[indices[i + 1]]; var d = local[indices[i + 2]];
                    Vector3 normal;
                    if (Mathf.Abs(a.x - b.x) < .0001f && Mathf.Abs(a.x - d.x) < .0001f)
                        normal = a.x < (left + right) * .5f ? Vector3.left : Vector3.right;
                    else if (Mathf.Abs(a.y - top) < .0001f && Mathf.Abs(b.y - top) < .0001f && Mathf.Abs(d.y - top) < .0001f)
                        normal = Vector3.up;
                    else if (Mathf.Abs(a.z - b.z) < .0001f && Mathf.Abs(a.z - d.z) < .0001f)
                    {
                        var span = spans.OrderBy(s => Mathf.Min(Mathf.Abs(a.z - s.x), Mathf.Abs(a.z - s.y))).First();
                        normal = Mathf.Abs(a.z - span.x) < Mathf.Abs(a.z - span.y) ? Vector3.back : Vector3.forward;
                    }
                    else normal = Vector3.down;
                    geo.Triangle(source.transform.TransformPoint(a), source.transform.TransformPoint(b), source.transform.TransformPoint(d),
                        source.transform.TransformDirection(normal), Mathf.Min(sub, 1));
                }
            }
            p.mesh = geo.Make("Repaired_Cloister_Arcade_" + row);
            if (p.mesh.vertices.Any(v => !Finite(v)) || p.mesh.normals.Any(v => !Finite(v)))
                throw new InvalidOperationException("Non-finite cloister vertex/normal remained after regeneration.");
            var info = p.info;
            CheckTopology(p.mesh, out info.boundaryEdges, out info.nonManifoldEdges, out info.volume);
            info.triangles = Triangles(p.mesh); info.closed = info.boundaryEdges == 0 && info.nonManifoldEdges == 0 && info.volume > .01;
            var floor = c.filters.SingleOrDefault(f => f.name == "Cloister_Floor" && f.gameObject.activeInHierarchy);
            if (!floor) throw new InvalidOperationException("The retained active cloister floor is required to verify the arcade top.");
            var floorContext = new Context { decks = new[] { Read(floor, true) } };
            info.maximumTopSeam = 0;
            foreach (var v in local.Where(v => Mathf.Abs(v.y - top) < .0001f).Distinct())
            {
                var world = source.transform.TransformPoint(v);
                info.maximumTopSeam = Mathf.Max(info.maximumTopSeam, Mathf.Abs(world.y - Bottom(floorContext, world, world.y)));
                info.topSamples++;
            }
            var side = source.transform.TransformDirection(Vector3.right).normalized;
            var piers = c.filters.Where(f => Regex.IsMatch(f.name, "^CW_Support_Cloister_Station_\\d+_Pier_" + row + "_Visual$") && f.gameObject.activeInHierarchy)
                .Select(f => Read(f, false)).ToArray();
            info.endContacts = true; info.minimumEndEmbed = float.PositiveInfinity;
            foreach (var span in spans) foreach (float z in new[] { span.x, span.y })
            {
                var point = source.transform.TransformPoint(new Vector3((left + right) * .5f, spring, z));
                var pier = piers.OrderBy(s => Horizontal(s.topCenter - point).sqrMagnitude).FirstOrDefault();
                if (pier == null || Horizontal(pier.topCenter - point).magnitude > 3) { info.endContacts = false; continue; }
                foreach (float x in new[] { left, (left + right) * .5f, right })
                {
                    var edge = source.transform.TransformPoint(new Vector3(x, spring, z));
                    if (!SolidInterval(pier, edge, side, out float lo, out float hi) || lo > -.035f || hi < .035f) info.endContacts = false;
                    else info.minimumEndEmbed = Mathf.Min(info.minimumEndEmbed, -lo, hi);
                }
            }
            info.fitted = info.maximumTopSeam <= .04f;
            info.from = source.transform.TransformPoint(new Vector3((left + right) * .5f, top, spans[0].x));
            info.to = source.transform.TransformPoint(new Vector3((left + right) * .5f, top, spans[2].y));
            info.width = Vector3.Distance(source.transform.TransformPoint(new Vector3(left, top, 0)), source.transform.TransformPoint(new Vector3(right, top, 0)));
            info.upperWidth = info.width;
            info.startBearing = "Current cloister row " + row + " / four existing station piers";
            info.endBearing = "Current Cloister_Floor underside";
            if (!info.closed || !info.fitted || !info.endContacts)
                throw new InvalidOperationException("Repaired cloister verification failed: closed=" + info.closed + ", fitted=" + info.fitted + ", contacts=" + info.endContacts);
            info.status = "Ready";
            info.reason = "Reconstructed all three retained openings with " + repaired + " non-finite endpoint vertices repaired; corrected endpoint winding, regenerated finite UVs/normals/tangents; verified current floor and all six pier contacts.";
        }
        static float Spring(Bearing bearing, float top, float depth)
        {
            if (!float.IsNaN(bearing.springOverride)) return bearing.springOverride;
            if (bearing.IsRock) return Mathf.Min(top - .85f, bearing.rockHeight - .18f);
            return Mathf.Clamp(top - depth, bearing.solid.bounds.min.y + .22f, bearing.solid.bounds.max.y - .16f);
        }
        static float BearingHalfWidth(Bearing bearing, Vector3 side, float height)
        {
            if (bearing.IsRock) return 20;
            if (!SolidInterval(bearing.solid, AtY(bearing.point, height), side, out float low, out float high))
                throw new InvalidOperationException("Arch spring centre is outside the live support mesh: " + bearing.name);
            return Mathf.Min(-low, high);
        }
        static bool EndContact(Context c, Bearing bearing, Vector3 side, float height, float halfWidth)
        {
            foreach (float w in new[] { -halfWidth, 0, halfWidth })
            {
                var point = AtY(bearing.point + side * w, height);
                if (bearing.IsRock)
                { if (!Ground(c, point, bearing.point.y + 1, out var hit) || point.y > hit.point.y - .04f) return false; }
                else if (!SolidInterval(bearing.solid, point, side, out float low, out float high) || low > -.04f || high < .04f) return false;
            }
            return true;
        }
        static bool Ground(Context c, Vector3 p, float limit, out RaycastHit result)
        {
            result = default; bool found = false;
            var ray = new Ray(new Vector3(p.x, limit + 1, p.z), Vector3.down);
            foreach (var collider in c.ground) if (collider.Raycast(ray, out var hit, 400) && hit.normal.y > .3f && (!found || hit.point.y > result.point.y))
            { result = hit; found = true; }
            return found;
        }
        static Vector3 LiveNode(Transform currentGroup, Vector3 originalWorld)
        {
            var original = PrefabUtility.GetCorrespondingObjectFromSource(currentGroup);
            if (original) return currentGroup.TransformPoint(original.InverseTransformPoint(originalWorld));
            // Unpacked generated sections retain their original mesh-local coordinate
            // offset; use the mesh asset's original centre instead of assuming its pivot.
            var f = currentGroup.GetComponentInChildren<MeshFilter>();
            if (!f || !f.sharedMesh) throw new InvalidOperationException("No source frame for " + currentGroup.name);
            // Generated mesh vertices are authored in world space; CentrePivot only
            // counter-shifts the child transform. In that case its source-frame offset
            // is the inverse of the child's local position.
            return currentGroup.TransformPoint(originalWorld + f.transform.localPosition);
        }
        static float DeckWidth(Surface deck, Vector3 direction)
        {
            var side = Vector3.Cross(Vector3.up, Horizontal(direction).normalized);
            var projected = deck.underside.Select(i => Vector3.Dot(deck.v[i], side)).ToArray();
            return projected.Max() - projected.Min();
        }
        static float Bottom(Context c, Vector3 p, float expected)
        {
            if (TryBottom(c, p, expected, out float value)) return value;
            throw new InvalidOperationException("No live deck underside at " + p.ToString("F3") + ", expected height " + expected.ToString("F3"));
        }
        static bool TryBottom(Context c, Vector3 p, float expected, out float value)
        {
            value = 0; float distance = float.PositiveInfinity;
            foreach (var deck in c.decks)
            {
                if (p.x < deck.bounds.min.x - .002f || p.x > deck.bounds.max.x + .002f || p.z < deck.bounds.min.z - .002f || p.z > deck.bounds.max.z + .002f) continue;
                var list = deck.underside;
                for (int k = 0; k < list.Count; k += 3)
                    if (Height(p, deck.v[list[k]], deck.v[list[k + 1]], deck.v[list[k + 2]], out float y))
                    { float d = Mathf.Abs(y - expected); if (d < distance && d < 4) { distance = d; value = y; } }
            }
            return !float.IsPositiveInfinity(distance);
        }

        static void FitUpperProfile(Context c, Plan p)
        {
            var stations = Stations(c, p).ToList();
            var widths = stations.Select(s => UpperWidthAt(c, p, s)).ToList();
            SmoothUpperProfile(stations, widths);
            // Refine a corner only where the complete edge segment, not sparse point
            // samples, exits the actual triangulated underside footprint.
            for (int pass = 0; pass < 7; pass++)
            {
                var nextS = new List<float>(); var nextW = new List<Vector2>(); bool refined = false;
                for (int i = 0; i < stations.Count - 1; i++)
                {
                    nextS.Add(stations[i]); nextW.Add(widths[i]);
                    var a = Vector3.Lerp(p.a.point, p.b.point, stations[i] / p.length);
                    var b = Vector3.Lerp(p.a.point, p.b.point, stations[i + 1] / p.length);
                    bool covered = LineCovered(c, a - p.side * widths[i].x, b - p.side * widths[i + 1].x) &&
                        LineCovered(c, a + p.side * widths[i].y, b + p.side * widths[i + 1].y);
                    if (!covered)
                    {
                        if (pass == 6 || stations[i + 1] - stations[i] < .002f)
                            throw new InvalidOperationException("Upper profile crosses an actual uncovered deck edge near station " + stations[i].ToString("F3"));
                        float s = (stations[i] + stations[i + 1]) * .5f;
                        nextS.Add(s); nextW.Add(UpperWidthAt(c, p, s)); refined = true;
                    }
                }
                nextS.Add(stations[stations.Count - 1]); nextW.Add(widths[widths.Count - 1]);
                stations = nextS; widths = nextW;
                SmoothUpperProfile(stations, widths);
                if (!refined) break;
            }
            // Intersections of the final variable-width outline with every real deck
            // triangle edge are explicit stations. A landing/slope join therefore has
            // identical top-edge vertices in the arch and in its bearing surface.
            var finalS = new List<float>(); var finalW = new List<Vector2>();
            for (int i = 0; i < stations.Count - 1; i++)
            {
                var centerA = Vector3.Lerp(p.a.point, p.b.point, stations[i] / p.length);
                var centerB = Vector3.Lerp(p.a.point, p.b.point, stations[i + 1] / p.length);
                var cuts = new List<float> { 0 };
                foreach (int side in new[] { -1, 0, 1 })
                {
                    var a = centerA + p.side * (side < 0 ? -widths[i].x : side > 0 ? widths[i].y : 0);
                    var b = centerB + p.side * (side < 0 ? -widths[i + 1].x : side > 0 ? widths[i + 1].y : 0);
                    foreach (var deck in c.decks) for (int k = 0; k < deck.underside.Count; k += 3) for (int edge = 0; edge < 3; edge++)
                    {
                        var q = deck.v[deck.underside[k + edge]]; var r = deck.v[deck.underside[k + (edge + 1) % 3]];
                        if (SegmentIntersection(a, b, q, r, out float t) && t > .00001f && t < .99999f) cuts.Add(t);
                    }
                }
                cuts.Sort();
                foreach (float t in cuts)
                {
                    float s = Mathf.Lerp(stations[i], stations[i + 1], t);
                    if (finalS.Count > 0 && s - finalS[finalS.Count - 1] < .0002f) continue;
                    finalS.Add(s); finalW.Add(Vector2.Lerp(widths[i], widths[i + 1], t));
                }
            }
            finalS.Add(p.length); finalW.Add(widths[widths.Count - 1]);
            p.stations = finalS.ToArray(); p.upperWidths = finalW.ToArray();
        }

        static void SmoothUpperProfile(List<float> stations, List<Vector2> widths)
        {
            // A wide corner platform can begin abruptly next to a narrower flight.
            // The new corbel flares gradually inside that outline instead of copying
            // its discontinuity into a sideways notch. This only reduces new geometry.
            const float maximumFlarePerMetre = .65f;
            for (int i = 1; i < widths.Count; i++)
            {
                float allowance = (stations[i] - stations[i - 1]) * maximumFlarePerMetre;
                widths[i] = new Vector2(Mathf.Min(widths[i].x, widths[i - 1].x + allowance), Mathf.Min(widths[i].y, widths[i - 1].y + allowance));
            }
            for (int i = widths.Count - 2; i >= 0; i--)
            {
                float allowance = (stations[i + 1] - stations[i]) * maximumFlarePerMetre;
                widths[i] = new Vector2(Mathf.Min(widths[i].x, widths[i + 1].x + allowance), Mathf.Min(widths[i].y, widths[i + 1].y + allowance));
            }
        }

        static Vector2 UpperWidthAt(Context c, Plan p, float station)
        {
            var center = Vector3.Lerp(p.a.point, p.b.point, station / p.length);
            float search = Mathf.Max(12, p.topHalfWidth * 3);
            var intervals = LineIntervals(c, center - p.side * search, center + p.side * search);
            var interval = intervals.FirstOrDefault(v => v.x <= .5f + .000002f && v.y >= .5f - .000002f);
            if (interval.y <= interval.x) throw new InvalidOperationException("No current deck cross-section at station " + station);
            float left = search - 2 * search * interval.x, right = 2 * search * interval.y - search;
            if (left < .05f || right < .05f) throw new InvalidOperationException("The actual pier-centre spine leaves the deck footprint at station " + station);
            left -= Mathf.Min(.15f, left * .25f); right -= Mathf.Min(.15f, right * .25f);
            if (!UseCorbelledTop) return new Vector2(Mathf.Min(left, p.halfWidth), Mathf.Min(right, p.halfWidth));
            float maximumWidth = p.topHalfWidth * 2;
            if (left + right > maximumWidth)
            {
                float minimumLeft = Mathf.Max(.04f, maximumWidth - right);
                float maximumLeft = Mathf.Min(left, maximumWidth - .04f);
                // Continue the actual flight's centreline through its landings. A
                // shared switchback platform must not pull the arch sideways towards
                // the other flight; a manually offset pier instead receives a genuine
                // asymmetric corbel back to the current road centreline.
                float denominator = Cross(p.side, p.deckAxis);
                if (Mathf.Abs(denominator) < .15f) throw new InvalidOperationException("Current pier-to-pier span is almost perpendicular to its deck.");
                float preferredCenter = Cross(p.deckLinePoint - center, p.deckAxis) / denominator;
                left = Mathf.Clamp(maximumWidth * .5f - preferredCenter, minimumLeft, maximumLeft);
                right = maximumWidth - left;
            }
            return new Vector2(left, right);
        }

        static bool LineCovered(Context c, Vector3 from, Vector3 to)
        {
            var intervals = LineIntervals(c, from, to);
            return intervals.Any(i => i.x <= .000002f && i.y >= .999998f);
        }
        static List<Vector2> LineIntervals(Context c, Vector3 from, Vector3 to)
        {
            var intervals = new List<Vector2>();
            foreach (var deck in c.decks) for (int i = 0; i < deck.underside.Count; i += 3)
            {
                var a = deck.v[deck.underside[i]]; var e = deck.v[deck.underside[i + 1]] - a; var f = deck.v[deck.underside[i + 2]] - a;
                float determinant = Cross(e, f); if (Mathf.Abs(determinant) < 1e-8f) continue;
                var wa = from - a; var wb = to - a;
                float ua = Cross(wa, f) / determinant, ub = Cross(wb, f) / determinant;
                float va = Cross(e, wa) / determinant, vb = Cross(e, wb) / determinant;
                float lo = 0, hi = 1;
                if (!ClipPositive(ref lo, ref hi, ua + .000001f, ub - ua) ||
                    !ClipPositive(ref lo, ref hi, va + .000001f, vb - va) ||
                    !ClipPositive(ref lo, ref hi, 1.000001f - ua - va, ua + va - ub - vb)) continue;
                float da = a.y + ua * e.y + va * f.y - from.y, db = a.y + ub * e.y + vb * f.y - to.y;
                if (!ClipPositive(ref lo, ref hi, 4 - da, da - db) || !ClipPositive(ref lo, ref hi, 4 + da, db - da)) continue;
                if (hi > lo) intervals.Add(new Vector2(lo, hi));
            }
            intervals.Sort((a, b) => a.x.CompareTo(b.x)); var merged = new List<Vector2>();
            foreach (var value in intervals)
            {
                if (merged.Count == 0 || value.x > merged[merged.Count - 1].y + .000002f) merged.Add(value);
                else { var last = merged[merged.Count - 1]; last.y = Mathf.Max(last.y, value.y); merged[merged.Count - 1] = last; }
            }
            return merged;
        }
        static bool ClipPositive(ref float lo, ref float hi, float value, float slope)
        {
            if (Mathf.Abs(slope) < 1e-9f) return value >= 0;
            float crossing = -value / slope;
            if (slope > 0) lo = Mathf.Max(lo, crossing); else hi = Mathf.Min(hi, crossing);
            return hi >= lo;
        }

        static float[] Stations(Context c, Plan p)
        {
            var values = new List<float> { 0, p.length, p.length * .5f };
            int divisions = Mathf.CeilToInt(p.length / .75f);
            for (int i = 1; i < divisions; i++) values.Add(p.length * i / divisions);
            foreach (var deck in c.decks) foreach (int index in deck.underside)
            {
                float station = Vector3.Dot(Horizontal(deck.v[index] - p.a.point), p.direction);
                if (station > .001f && station < p.length - .001f) values.Add(station);
            }
            // Every intersection with an actual underside triangle edge is a station.
            // This prevents an interpolated top from cutting across landing/slope joints.
            foreach (float offset in new[] { -p.topHalfWidth, 0, p.topHalfWidth })
            {
                var a = p.a.point + p.side * offset; var b = p.b.point + p.side * offset;
                foreach (var deck in c.decks) for (int i = 0; i < deck.underside.Count; i += 3) for (int k = 0; k < 3; k++)
                {
                    var q = deck.v[deck.underside[i + k]]; var r = deck.v[deck.underside[i + (k + 1) % 3]];
                    if (Mathf.Max(q.y, r.y) < Mathf.Min(a.y, b.y) - 4 || Mathf.Min(q.y, r.y) > Mathf.Max(a.y, b.y) + 4) continue;
                    if (SegmentIntersection(a, b, q, r, out float t) && t > .0001f && t < .9999f) values.Add(t * p.length);
                }
            }
            values.Sort(); var unique = new List<float>();
            foreach (float x in values) if (unique.Count == 0 || x - unique[unique.Count - 1] > .001f) unique.Add(x);
            if (p.length - unique[unique.Count - 1] > .0001f) unique.Add(p.length); else unique[unique.Count - 1] = p.length;
            return unique.ToArray();
        }
        static bool SegmentIntersection(Vector3 a, Vector3 b, Vector3 q, Vector3 r, out float t)
        {
            var u = b - a; var v = r - q; var w = q - a; float denominator = Cross(u, v); t = 0;
            if (Mathf.Abs(denominator) < 1e-7f) return false;
            t = Cross(w, v) / denominator; float s = Cross(w, u) / denominator;
            return t >= 0 && t <= 1 && s >= -.00001f && s <= 1.00001f;
        }
        static bool Height(Vector3 p, Vector3 a, Vector3 b, Vector3 d, out float height)
        {
            var e = b - a; var f = d - a; float determinant = Cross(e, f); height = 0;
            if (Mathf.Abs(determinant) < 1e-8f) return false;
            var w = p - a; float u = Cross(w, f) / determinant, v = Cross(e, w) / determinant;
            if (u < -.00002f || v < -.00002f || u + v > 1.00002f) return false;
            height = a.y + u * e.y + v * f.y; return true;
        }
        static bool SolidInterval(Surface solid, Vector3 point, Vector3 side, out float low, out float high)
        {
            // Junction piers contain a broad bearing and a narrower upper stem whose
            // closed volumes overlap. Even/odd ray pairing would call that overlap a
            // hole. Integrate oriented entry/exit events to test their solid union.
            var origin = point - side * 100; var hits = new List<Vector2>(); low = high = 0;
            for (int i = 0; i < solid.triangles.Length; i += 3)
            {
                var a = solid.v[solid.triangles[i]]; var b = solid.v[solid.triangles[i + 1]]; var d = solid.v[solid.triangles[i + 2]];
                if (RayTriangle(origin, side, a, b, d, out float distance))
                    hits.Add(new Vector2(distance - 100, Vector3.Dot(Vector3.Cross(b - a, d - a), side) < 0 ? 1 : -1));
            }
            hits.Sort((a, b) => a.x.CompareTo(b.x)); var unique = new List<Vector2>();
            foreach (var hit in hits) if (!unique.Any(v => Mathf.Abs(v.x - hit.x) < .001f && v.y == hit.y)) unique.Add(hit);
            int depth = 0; float start = 0;
            foreach (var hit in unique)
            {
                int before = depth; depth += (int)hit.y;
                if (before == 0 && depth > 0) start = hit.x;
                if (before > 0 && depth == 0 && start <= 0 && hit.x >= 0) { low = start; high = hit.x; return true; }
            }
            return false;
        }
        static bool RayTriangle(Vector3 o, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(direction, e2); float determinant = Vector3.Dot(e1, p); distance = 0;
            if (Mathf.Abs(determinant) < 1e-7f) return false;
            float inverse = 1 / determinant; var t = o - a; float u = Vector3.Dot(t, p) * inverse;
            if (u < -.00001f || u > 1.00001f) return false; var q = Vector3.Cross(t, e1); float v = Vector3.Dot(direction, q) * inverse;
            if (v < -.00001f || u + v > 1.00001f) return false; distance = Vector3.Dot(e2, q) * inverse; return distance >= 0;
        }
        static Surface Read(MeshFilter f, bool underside)
        {
            var mesh = f.sharedMesh; var s = new Surface { filter = f, v = mesh.vertices.Select(f.transform.TransformPoint).ToArray(), triangles = mesh.triangles };
            s.bounds = new Bounds(s.v[0], Vector3.zero); foreach (var v in s.v) s.bounds.Encapsulate(v);
            var highest = s.v.Where(v => v.y >= s.bounds.max.y - .012f).Distinct().ToArray();
            s.topCenter = highest.Aggregate(Vector3.zero, (sum, v) => sum + v) / highest.Length;
            if (underside)
            {
                // Stone rail plinths ALSO use submesh 1, but their narrow bottom plane
                // is above the actual road underside. Select the largest continuous
                // coplanar underside by area instead of accepting every stone face.
                // Stair bottoms are one sloping plane even though their top is stepped.
                var planes = new List<BottomPlane>();
                var tris = f.name.StartsWith("WalkSurface_CW_", StringComparison.Ordinal) && mesh.subMeshCount > 1
                    ? mesh.GetTriangles(1) : mesh.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = s.v[tris[i]]; var b = s.v[tris[i + 1]]; var c = s.v[tris[i + 2]];
                    var cross = Vector3.Cross(b - a, c - a); var normal = cross.normalized;
                    if (normal.y >= -.65f) continue;
                    var center = (a + b + c) / 3;
                    var plane = planes.FirstOrDefault(p => Vector3.Dot(p.normal, normal) > .9999f && Mathf.Abs(Vector3.Dot(p.normal, center - p.point)) < .002f);
                    if (plane == null) { plane = new BottomPlane { normal = normal, point = center }; planes.Add(plane); }
                    plane.area += cross.magnitude * .5f; plane.indices.AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
                }
                if (planes.Count > 0) s.underside.AddRange(planes.OrderByDescending(p => p.area).First().indices);
            }
            return s;
        }
        static void CheckTopology(Mesh mesh, out int boundary, out int nonManifold, out double volume)
        {
            var vertices = mesh.vertices; var tris = mesh.triangles; var welded = new Dictionary<string, int>(); var ids = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i]; string key = Mathf.RoundToInt(p.x * 10000) + "/" + Mathf.RoundToInt(p.y * 10000) + "/" + Mathf.RoundToInt(p.z * 10000);
                if (!welded.TryGetValue(key, out int id)) { id = welded.Count; welded.Add(key, id); } ids[i] = id;
            }
            var edges = new Dictionary<ulong, int>(); volume = 0;
            var origin = mesh.bounds.center;
            for (int i = 0; i < tris.Length; i += 3)
            {
                var a = vertices[tris[i]] - origin; var b = vertices[tris[i + 1]] - origin; var c = vertices[tris[i + 2]] - origin;
                volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                for (int k = 0; k < 3; k++)
                {
                    int x = ids[tris[i + k]], y = ids[tris[i + (k + 1) % 3]];
                    ulong key = ((ulong)(uint)Mathf.Min(x, y) << 32) | (uint)Mathf.Max(x, y);
                    edges.TryGetValue(key, out int value); edges[key] = value + 1;
                }
            }
            boundary = edges.Count(kv => kv.Value == 1); nonManifold = edges.Count(kv => kv.Value != 1 && kv.Value != 2);
        }
        sealed class Geo
        {
            readonly List<Vector3> v = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] indices = { new List<int>(), new List<int>() };
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int material)
            { Triangle(a, b, c, outward, material); Triangle(a, c, d, outward, material); }
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, int material)
            {
                var normal = Vector3.Cross(b - a, c - a); if (normal.sqrMagnitude < 1e-14f) return;
                if (Vector3.Dot(normal, outward) < 0) { var temp = b; b = c; c = temp; normal = -normal; }
                normal.Normalize(); int first = v.Count; var tangent = Vector3.Cross(Vector3.up, normal).normalized;
                foreach (var point in new[] { a, b, c }) { v.Add(point); normals.Add(normal); uv.Add(Mathf.Abs(normal.y) > .85f ? new Vector2(point.x, point.z) : new Vector2(Vector3.Dot(point, tangent), point.y)); }
                indices[material].AddRange(new[] { first, first + 1, first + 2 });
            }
            public Mesh Make(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = v.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(v); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.subMeshCount = 2;
                for (int i = 0; i < 2; i++) mesh.SetTriangles(indices[i], i, false);
                mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
            }
        }
        static Bounds WorldBounds(MeshFilter f)
        {
            var b = f.sharedMesh.bounds; var result = new Bounds(f.transform.TransformPoint(b.center), Vector3.zero);
            foreach (float x in new[] { b.min.x, b.max.x }) foreach (float y in new[] { b.min.y, b.max.y }) foreach (float z in new[] { b.min.z, b.max.z })
                result.Encapsulate(f.transform.TransformPoint(new Vector3(x, y, z))); return result;
        }
        static IEnumerable<Transform> Ancestors(Transform t) { for (var p = t.parent; p; p = p.parent) yield return p; }
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
        static float DistanceToSpan(Vector3 p, Vector3 a, Vector3 b)
        { var d = Horizontal(b - a); if (d.sqrMagnitude < .01f) return float.PositiveInfinity; return Horizontal(p - Vector3.Lerp(a, b, Mathf.Clamp01(Vector3.Dot(Horizontal(p - a), d) / d.sqrMagnitude))).magnitude; }
        static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static Vector3 Horizontal(Vector3 p) => new Vector3(p.x, 0, p.z);
        static Vector3 AtY(Vector3 p, float y) => new Vector3(p.x, y, p.z);
        static int Triangles(Mesh mesh) { int count = 0; for (int i = 0; i < mesh.subMeshCount; i++) count += (int)mesh.GetIndexCount(i) / 3; return count; }
        static bool Same(Matrix4x4 a, Matrix4x4 b) { for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .000001f) return false; return true; }
        static void DisposePlans(Context c) { foreach (var p in c.plans) if (p.mesh) UnityEngine.Object.DestroyImmediate(p.mesh); }
        static void WriteReport(Report report, string file) { Directory.CreateDirectory(ReportFolder); File.WriteAllText(System.IO.Path.Combine(ReportFolder, file), JsonUtility.ToJson(report, true)); }
    }
}
