using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chapter01.Editor
{
    /// <summary>Editor-only, low-cost collision for reachable authored architecture.</summary>
    public static class CastleObstacleCollisionBuilder
    {
        const string Prefix = "CW_Obstacle_Authored_";
        const string CollisionFolder = "Assets/Chapter01/CastleWalkwayUpgrade/CollisionMeshes";
        const int MaximumBoxesPerSource = 12;
        const float RouteRadius = 21f;
        const float Cell = .12f;
        static readonly Regex AssetCode = new Regex(@"^([A-D]\d\d)__");
        static readonly Regex PartSuffix = new Regex(@"__\d+$");
        static readonly Regex CombatLampSupport = new Regex(@"^Six_Combat_Lantern_-?\d+(?:\.\d+)?_-?\d+(?:\.\d+)?_(?:stem|plinth)$");
        static readonly HashSet<string> Solid = new HashSet<string> { "A01", "A04", "C05", "C06", "C07", "C08" };
        static readonly HashSet<string> Sectioned = new HashSet<string> { "A03", "A09", "A11", "A15", "A16", "A17", "B03", "B07", "D01", "D02" };

        public sealed class Obstacle
        {
            public Collider collider;
            public string category, sourceName, method;
            public Renderer sourceRenderer;
            public Renderer[] sourceRenderers;
        }
        public sealed class Result
        {
            public readonly List<Obstacle> obstacles = new List<Obstacle>();
            public readonly Report report = new Report();
            public Collider[] Colliders => obstacles.Select(x => x.collider).ToArray();
        }
        public static IReadOnlyList<Obstacle> LastCreated { get; private set; } = Array.Empty<Obstacle>();
        [Serializable] public sealed class Record
        {
            public string source, category, method, status;
            public string[] rendererNames;
            public int colliders, nonClosedScanRows, mergedBoxShapes, collisionTriangles;
            public Vector3 boundsCenter, boundsSize;
        }
        [Serializable] public sealed class Report
        {
            public string scene, owner, contract = "Authored mesh-local OBBs for solid pieces; sampled near-ground cross sections for hollow forms. Groups above 12 boxes become one static non-convex mesh made only from those simplified boxes, preserving openings. collisionTriangles counts baked mesh triangles; boxes remain analytic shapes. No source mesh/importer edits or high-poly MeshColliders. New colliders follow their visible model parent.";
            public int sourceGroups, solidGroups, sectionedGroups, railGroups, boxColliders, skippedExisting, skippedUnreachable, unresolvedGroups;
            public int colliderComponents, meshColliders, mergedBoxShapes, collisionTriangles;
            public long examinedTriangles;
            public List<Record> entries = new List<Record>();
        }
        sealed class MeshData
        {
            public Vector3[] vertices;
            public int[] indices;
        }
        sealed class SectionMesh
        {
            public Vector3[] vertices;
            public int[] indices;
        }
        struct Line { public Vector2 a, b; }
        struct RectCells { public int x, z, width, depth; }
        struct Frame
        {
            public Vector3 origin;
            public Quaternion rotation;
            public Vector3 Local(Vector3 world) => Quaternion.Inverse(rotation) * (world - origin);
            public Vector3 World(Vector3 local) => origin + rotation * local;
        }

        // collisionRoot identifies this authoring batch; collision objects intentionally
        // become children of their source models so later layout edits carry them along.
        public static Report Build(Scene scene, Transform collisionRoot, Vector3[] route = null)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("A loaded castle scene is required.");
            route = route ?? Chapter01RuntimeValidation.ReadRoute().points;
            if (route.Length < 2) throw new ArgumentException("A route with at least two points is required.");
            var result = new Result(); result.report.scene = scene.path;
            LastCreated = result.obstacles;
            result.report.owner = collisionRoot ? collisionRoot.name : "Castle authored obstacles";
            var cache = new Dictionary<Mesh, MeshData>();
            var renderers = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MeshRenderer>(true))
                .Where(x => x.enabled && x.gameObject.activeInHierarchy && x.GetComponent<MeshFilter>()?.sharedMesh != null)
                .Where(x => !AssetDatabase.GetAssetPath(x.GetComponent<MeshFilter>().sharedMesh).Contains("/Distant/"))
                .Where(x => Eligible(x.name)).ToArray();
            // A03's repaired L wall may have several mesh parts sharing one authoring pivot.
            var groups = renderers.GroupBy(x => PartSuffix.Replace(x.name, ""), StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var sources = group.OrderBy(x => x.name, StringComparer.Ordinal).ToArray();
                var bounds = sources[0].bounds; foreach (var source in sources.Skip(1)) bounds.Encapsulate(source.bounds);
                var record = new Record { source = group.Key, rendererNames = sources.Select(x => x.name).ToArray(),
                    boundsCenter = bounds.center, boundsSize = bounds.size };
                result.report.entries.Add(record); ++result.report.sourceGroups;
                if (!Reachable(bounds, route, out float bottom, out float top))
                { record.status = "Outside reachable route height/radius"; ++result.report.skippedUnreachable; continue; }
                foreach (var old in sources.SelectMany(x => x.GetComponentsInChildren<Collider>(true))
                    .Where(c => c.name.StartsWith(Prefix, StringComparison.Ordinal)).Select(c => c.gameObject).Distinct().ToArray())
                    Object.DestroyImmediate(old);
                if (sources.Any(x => x.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy)))
                { record.status = "Existing solid collision retained"; ++result.report.skippedExisting; continue; }
                string code = AssetCode.Match(group.Key).Groups[1].Value;
                bool rail = code == "A07" || code == "A08";
                bool lampSupport = CombatLampSupport.IsMatch(group.Key);
                record.category = rail ? "rail" : lampSupport || code == "A16" || code.StartsWith("C", StringComparison.Ordinal) || code.StartsWith("D", StringComparison.Ordinal) ? "column" : "building";
                int before = result.obstacles.Count;
                if (Solid.Contains(code) || rail || lampSupport)
                {
                    record.method = rail ? "Model-local player barrier; Ignore Raycast" : lampSupport ? "Model-local gameplay lamp support; Ignore Raycast" : "Conservative model-local solid OBB";
                    // The six lamp supports have no D02 prefix. Keep their separate
                    // narrow solids, and let weapon rays reach the existing wick trigger.
                    foreach (var source in sources) LocalBox(source, sources, record, result, rail || lampSupport ? 2 : 0);
                    if (rail) ++result.report.railGroups; else ++result.report.solidGroups;
                }
                else
                {
                    record.method = "Actual-mesh horizontal sections, 12 cm occupancy cells, merged static boxes";
                    ++result.report.sectionedGroups;
                    // The six gameplay lamps retain an unobstructed flame trigger above the stem.
                    if (code == "D02") top = Mathf.Min(top, bounds.min.y + 2.65f);
                    CrossSectionBoxes(sources, bounds, bottom, top, record, result, cache,
                        code == "A15" || code == "A16" || code == "D01" || code == "D02", code == "D02" ? 2 : 0);
                }
                MergeDenseGroup(sources, record, result, before);
                record.colliders = result.obstacles.Count - before;
                if (record.colliders == 0)
                { record.status = "UNRESOLVED: no reliable solid section; no whole-bounds fallback"; ++result.report.unresolvedGroups; }
                else record.status = record.nonClosedScanRows > 0 ? "Built; some open contour rows skipped" : "Built";
            }
            result.report.colliderComponents = result.obstacles.Count;
            result.report.boxColliders = result.obstacles.Count(x => x.collider is BoxCollider);
            result.report.meshColliders = result.obstacles.Count(x => x.collider is MeshCollider);
            result.report.mergedBoxShapes = result.report.entries.Sum(x => x.mergedBoxShapes);
            result.report.collisionTriangles = result.report.entries.Sum(x => x.collisionTriangles);
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            Debug.Log($"CASTLE_AUTHORED_OBSTACLES colliders={result.report.colliderComponents} boxes={result.report.boxColliders} meshes={result.report.meshColliders} collisionTriangles={result.report.collisionTriangles} solidGroups={result.report.solidGroups} sectionGroups={result.report.sectionedGroups} rails={result.report.railGroups} unresolved={result.report.unresolvedGroups}");
            return result.report;
        }

        static void MergeDenseGroup(MeshRenderer[] sources, Record record, Result result, int first)
        {
            int count = result.obstacles.Count - first;
            if (count <= MaximumBoxesPerSource) return;
            var boxes = result.obstacles.Skip(first).Select(x => x.collider as BoxCollider).ToArray();
            if (boxes.Any(x => x == null)) throw new InvalidOperationException("Only generated simplified boxes may be baked: " + record.source);
            int layer = boxes[0].gameObject.layer;
            if (boxes.Any(x => x.gameObject.layer != layer || x.isTrigger))
                throw new InvalidOperationException("A collision group must retain one layer and solid semantics: " + record.source);

            // Bake each closed box separately: no hull, bridging faces or source-mesh
            // triangles may fill a doorway or change the sampled occupied volume.
            var corners = new[] {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),
                new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f)
            };
            var faces = new[] {
                0,2,1, 0,3,2, 4,5,6, 4,6,7,
                0,4,7, 0,7,3, 1,2,6, 1,6,5,
                0,1,5, 0,5,4, 3,7,6, 3,6,2
            };
            var vertices = new Vector3[boxes.Length * 8];
            var indices = new int[boxes.Length * faces.Length];
            for (int boxIndex = 0; boxIndex < boxes.Length; ++boxIndex)
            {
                var box = boxes[boxIndex]; int vertexOffset = boxIndex * 8;
                for (int i = 0; i < corners.Length; ++i)
                    vertices[vertexOffset + i] = box.transform.TransformPoint(box.center + Vector3.Scale(box.size, corners[i]));
                bool mirrored = box.transform.localToWorldMatrix.determinant < 0;
                for (int i = 0; i < faces.Length; i += 3)
                {
                    int offset = boxIndex * faces.Length + i;
                    indices[offset] = vertexOffset + faces[i];
                    indices[offset + 1] = vertexOffset + faces[i + (mirrored ? 2 : 1)];
                    indices[offset + 2] = vertexOffset + faces[i + (mirrored ? 1 : 2)];
                }
            }
            EnsureAssetFolder(CollisionFolder);
            string name = Prefix + Safe(record.source) + "_Merged";
            string path = CollisionFolder + "/" + Safe(record.source) + "_" + Hash128.Compute(record.source) + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh { name = name }; else mesh.Clear();
            mesh.indexFormat = vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices; mesh.triangles = indices; mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            if (create) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);

            // Vertices are in world metres. Parenting with worldPositionStays keeps
            // today's identity matrix and makes later source-model edits carry collision.
            var go = new GameObject(name) { layer = layer, isStatic = true };
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            go.transform.SetParent(sources[0].transform, true);
            var merged = go.AddComponent<MeshCollider>();
            merged.convex = false; merged.sharedMaterial = boxes[0].sharedMaterial;
            merged.contactOffset = boxes[0].contactOffset; merged.sharedMesh = mesh;
            foreach (var box in boxes) Object.DestroyImmediate(box.gameObject);
            result.obstacles.RemoveRange(first, count);
            record.mergedBoxShapes = count; record.collisionTriangles = indices.Length / 3;
            record.method += "; simplified boxes baked into one static non-convex mesh";
            AddResult(merged, sources[0], sources, record, result);
        }

        static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int slash = folder.LastIndexOf('/');
            if (slash < 0) throw new InvalidOperationException("Expected an Assets-relative collision folder: " + folder);
            string parent = folder.Substring(0, slash);
            EnsureAssetFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1))))
                throw new InvalidOperationException("Cannot create generated collision folder: " + folder);
        }

        static bool Eligible(string name)
        {
            string code = AssetCode.Match(name).Groups[1].Value;
            return Solid.Contains(code) || Sectioned.Contains(code) || code == "A07" || code == "A08" || CombatLampSupport.IsMatch(name);
        }

        static bool Reachable(Bounds b, Vector3[] route, out float bottom, out float top)
        {
            bottom = float.PositiveInfinity; top = float.NegativeInfinity;
            for (int i = 1; i < route.Length; ++i)
            {
                Vector3 a = route[i - 1], d = route[i] - a;
                int count = Mathf.Max(1, Mathf.CeilToInt(new Vector2(d.x, d.z).magnitude / 2));
                for (int j = 0; j <= count; ++j)
                {
                    Vector3 p = a + d * (j / (float)count);
                    float dx = Mathf.Max(Mathf.Max(b.min.x - p.x, 0), p.x - b.max.x);
                    float dz = Mathf.Max(Mathf.Max(b.min.z - p.z, 0), p.z - b.max.z);
                    if (dx * dx + dz * dz > RouteRadius * RouteRadius || b.max.y < p.y - .15f || b.min.y > p.y + 3.2f) continue;
                    bottom = Mathf.Min(bottom, Mathf.Max(b.min.y, p.y - .15f));
                    top = Mathf.Max(top, Mathf.Min(b.max.y, p.y + 3.2f));
                }
            }
            return top > bottom + .025f;
        }

        static void LocalBox(MeshRenderer source, MeshRenderer[] sources, Record record, Result result, int layer)
        {
            Bounds bounds = source.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 scale = source.transform.lossyScale, size = bounds.size;
            for (int axis = 0; axis < 3; ++axis)
                size[axis] = Mathf.Max(size[axis] * .85f, size[axis] - .025f / Mathf.Max(.00001f, Mathf.Abs(scale[axis])));
            if (Vector3.Scale(size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))).magnitude < .03f) return;
            var go = new GameObject(Prefix + Safe(record.source) + "_Solid_" + record.colliders++);
            go.transform.SetParent(source.transform, false); go.layer = layer;
            var collider = go.AddComponent<BoxCollider>(); collider.center = bounds.center; collider.size = size;
            AddResult(collider, source, sources, record, result);
        }

        static Frame GetFrame(MeshRenderer source, Bounds bounds)
        {
            // Use the model's horizontal axes, not its world AABB: oblique doors and walls
            // stay narrow. World Y remains vertical for the height-slice decomposition.
            var m = source.transform.localToWorldMatrix;
            Vector3 best = Vector3.forward; float largest = -1;
            Bounds local = source.GetComponent<MeshFilter>().sharedMesh.bounds;
            for (int axis = 0; axis < 3; ++axis)
            {
                Vector3 v = m.GetColumn(axis); float length = v.magnitude;
                if (length < 1e-6f || Mathf.Abs(v.y) / length > .2f) continue;
                v.y = 0; float score = length * local.size[axis];
                if (score > largest) { largest = score; best = v.normalized; }
            }
            return new Frame { origin = new Vector3(bounds.center.x, 0, bounds.center.z), rotation = Quaternion.LookRotation(Vector3.Cross(best, Vector3.up), Vector3.up) };
        }

        static void CrossSectionBoxes(MeshRenderer[] sources, Bounds worldBounds, float bottom, float top,
            Record record, Result result, Dictionary<Mesh, MeshData> cache, bool solidFootprint, int layer)
        {
            if (top <= bottom + .025f) return;
            Frame frame = GetFrame(sources[0], worldBounds);
            var sections = new List<SectionMesh>();
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            foreach (var source in sources)
            {
                var mesh = source.GetComponent<MeshFilter>().sharedMesh;
                if (!cache.TryGetValue(mesh, out var data))
                {
                    data = new MeshData { vertices = mesh.vertices,
                        indices = Enumerable.Range(0, mesh.subMeshCount).SelectMany(i => mesh.GetTriangles(i)).ToArray() };
                    cache.Add(mesh, data);
                }
                var matrix = Matrix4x4.TRS(frame.origin, frame.rotation, Vector3.one).inverse * source.transform.localToWorldMatrix;
                var vertices = data.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                foreach (var v in vertices) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
                sections.Add(new SectionMesh { vertices = vertices, indices = data.indices });
            }
            int width = Mathf.Max(1, Mathf.CeilToInt((hi.x - lo.x) / Cell));
            int depth = Mathf.Max(1, Mathf.CeilToInt((hi.z - lo.z) / Cell));
            if ((long)width * depth > 250000) throw new InvalidOperationException("Unexpectedly large architectural footprint: " + record.source);
            float cellX = (hi.x - lo.x) / width, cellZ = (hi.z - lo.z) / depth;
            if (cellX < .00001f || cellZ < .00001f) return;
            int slices = Mathf.Max(1, Mathf.CeilToInt((top - bottom) / .55f));
            var pending = new Dictionary<string, Bounds>();
            for (int slice = 0; slice < slices; ++slice)
            {
                float y0 = Mathf.Lerp(bottom, top, slice / (float)slices), y1 = Mathf.Lerp(bottom, top, (slice + 1f) / slices);
                var occupied = new bool[width, depth];
                foreach (var section in sections)
                {
                    var lines = LinesAt(section, (y0 + y1) * .5f + .000031f, result.report);
                    Raster(lines, occupied, lo, cellX, cellZ, record);
                }
                // A solid column/buttress/lamp needs one footprint per height band,
                // not dozens of small boxes following chips and decorative fluting.
                // Hollow doors, the L corner and apse retain the actual occupied cells.
                var rectangles = solidFootprint ? SolidRectangle(occupied) : Merge(occupied);
                var seen = new HashSet<string>();
                foreach (var rectangle in rectangles)
                {
                    string key = rectangle.x + ":" + rectangle.z + ":" + rectangle.width + ":" + rectangle.depth;
                    seen.Add(key);
                    var min = new Vector3(lo.x + rectangle.x * cellX, y0, lo.z + rectangle.z * cellZ);
                    var max = new Vector3(min.x + rectangle.width * cellX, y1, min.z + rectangle.depth * cellZ);
                    if (pending.TryGetValue(key, out var previous) && Mathf.Abs(previous.max.y - y0) < .001f)
                    { previous.SetMinMax(previous.min, max); pending[key] = previous; }
                    else
                    {
                        if (pending.TryGetValue(key, out previous)) Emit(previous);
                        var b = new Bounds(); b.SetMinMax(min, max); pending[key] = b;
                    }
                }
                foreach (string expired in pending.Keys.Where(x => !seen.Contains(x)).ToArray()) { Emit(pending[expired]); pending.Remove(expired); }
            }
            foreach (var box in pending.Values) Emit(box);

            void Emit(Bounds box)
            {
                Vector3 size = box.size; size.x = Mathf.Max(.015f, size.x - .012f); size.z = Mathf.Max(.015f, size.z - .012f);
                var go = new GameObject(Prefix + Safe(record.source) + "_Section_" + record.colliders++);
                go.layer = layer;
                go.transform.SetPositionAndRotation(frame.World(box.center), frame.rotation);
                go.transform.SetParent(sources[0].transform, true);
                var collider = go.AddComponent<BoxCollider>(); collider.size = size;
                AddResult(collider, sources[0], sources, record, result);
            }
        }

        static List<Line> LinesAt(SectionMesh mesh, float height, Report report)
        {
            var lines = new List<Line>(); var intersections = new Vector2[3];
            for (int i = 0; i < mesh.indices.Length; i += 3)
            {
                ++report.examinedTriangles;
                var a = mesh.vertices[mesh.indices[i]]; var b = mesh.vertices[mesh.indices[i + 1]]; var c = mesh.vertices[mesh.indices[i + 2]];
                if (height < Mathf.Min(a.y, Mathf.Min(b.y, c.y)) || height > Mathf.Max(a.y, Mathf.Max(b.y, c.y))) continue;
                int count = 0; Cut(a, b); Cut(b, c); Cut(c, a);
                if (count == 2 && (intersections[0] - intersections[1]).sqrMagnitude > 1e-12f)
                    lines.Add(new Line { a = intersections[0], b = intersections[1] });
                void Cut(Vector3 p, Vector3 q)
                {
                    if ((p.y > height) == (q.y > height)) return;
                    Vector3 hit = Vector3.LerpUnclamped(p, q, (height - p.y) / (q.y - p.y));
                    intersections[count++] = new Vector2(hit.x, hit.z);
                }
            }
            return lines;
        }

        static void Raster(List<Line> lines, bool[,] occupied, Vector3 min, float cellX, float cellZ, Record record)
        {
            var intersections = new List<float>(); int width = occupied.GetLength(0), depth = occupied.GetLength(1);
            for (int row = 0; row < depth; ++row)
            {
                float z = min.z + (row + .5f) * cellZ; intersections.Clear();
                foreach (var line in lines)
                    if ((line.a.y > z) != (line.b.y > z))
                        intersections.Add(line.a.x + (line.b.x - line.a.x) * ((z - line.a.y) / (line.b.y - line.a.y)));
                intersections.Sort();
                for (int i = intersections.Count - 1; i > 0; --i)
                    if (Mathf.Abs(intersections[i] - intersections[i - 1]) < .00005f) intersections.RemoveAt(i);
                if (intersections.Count % 2 != 0) { ++record.nonClosedScanRows; continue; }
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    int first = Mathf.Max(0, Mathf.CeilToInt((intersections[i] - min.x) / cellX - .5f));
                    int last = Mathf.Min(width - 1, Mathf.FloorToInt((intersections[i + 1] - min.x) / cellX - .5f));
                    for (int x = first; x <= last; ++x) occupied[x, row] = true;
                }
            }
        }

        static List<RectCells> Merge(bool[,] cells)
        {
            var rectangles = new List<RectCells>(); int width = cells.GetLength(0), depth = cells.GetLength(1);
            for (int z = 0; z < depth; ++z) for (int x = 0; x < width; ++x)
            {
                if (!cells[x, z]) continue;
                int w = 1; while (x + w < width && cells[x + w, z]) ++w;
                int h = 1;
                while (z + h < depth)
                {
                    bool full = true; for (int k = 0; k < w; ++k) if (!cells[x + k, z + h]) { full = false; break; }
                    if (!full) break; ++h;
                }
                for (int j = 0; j < h; ++j) for (int i = 0; i < w; ++i) cells[x + i, z + j] = false;
                rectangles.Add(new RectCells { x = x, z = z, width = w, depth = h });
            }
            return rectangles;
        }

        static List<RectCells> SolidRectangle(bool[,] cells)
        {
            int minX = cells.GetLength(0), minZ = cells.GetLength(1), maxX = -1, maxZ = -1;
            for (int z = 0; z < cells.GetLength(1); ++z) for (int x = 0; x < cells.GetLength(0); ++x)
                if (cells[x, z]) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z); }
            return maxX < minX ? new List<RectCells>() : new List<RectCells> { new RectCells {
                x = minX, z = minZ, width = maxX - minX + 1, depth = maxZ - minZ + 1 } };
        }

        static void AddResult(Collider collider, MeshRenderer source, MeshRenderer[] sources, Record record, Result result)
        {
            result.obstacles.Add(new Obstacle { collider = collider, category = record.category, sourceName = record.source,
                method = record.method, sourceRenderer = source, sourceRenderers = sources.Cast<Renderer>().ToArray() });
        }
        static string Safe(string value) => Regex.Replace(value, @"[^A-Za-z0-9_-]", "_");
    }
}
