using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Chapter01.Rendering;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chapter01.Editor
{
    /// <summary>Read-only verification of foundation seats against actual imported rendering LODs.</summary>
    public static class CastleSupportLodAudit
    {
        const string ScenePath = "Assets/Chapter01/Scenes/Chapter01_CastleWalkwayCandidate.unity";
        const string InputPath = "D:/Shadow/Builds/CastleWalkway-Verification/bridge-supports.json";
        const string Output = "D:/Shadow/Builds/CastleWalkway-Verification/support-lod-audit";
        [Serializable] sealed class InputReport { public List<InputSupport> supports; }
        [Serializable] sealed class InputSupport { public string name; public float bottom; public InputGround[] ground; }
        [Serializable] sealed class InputGround { public Vector3 point; public string collider, surfaceName, surfaceKind; public bool found; }
        [Serializable] sealed class Range
        {
            public int submesh, lod, indexStart, indexCount, baseVertex;
            public bool nativeIndicesMatchRangeCount;
        }
        [Serializable] sealed class SurfaceReport
        {
            public string objectName, mesh, meshAsset;
            public int vertices, lodCount, forceMeshLod;
            public float meshLodSelectionBias;
            public bool rendererEnabled, objectActive;
            public List<Range> ranges = new List<Range>();
        }
        [Serializable] sealed class PointResult
        {
            public string support, surface, reportedKind;
            public int footpoint, lod, hitSubmesh;
            public Vector3 xzAndReportedY;
            public float foundationBottom, surfaceY, embed, differenceFromReported, differenceFromLod0, frontFacingNormalY;
            public bool found, foundationBelowActualSurface, frontFacingFromAbove;
        }
        [Serializable] sealed class LevelSummary
        {
            public int lod, points, missingHits, floatingPoints, cloisterPoints, cloisterMissingHits, cloisterFloatingPoints;
            public float minimumEmbed = float.PositiveInfinity, maximumAbsoluteLodDisplacement;
        }
        [Serializable] sealed class Report
        {
            public string timestampUtc, scene, sceneHashBefore, sceneHashAfter;
            public bool savedSceneUnchanged, nativeLodIndicesVerified;
            public string method = "Actual imported visible-mesh vertex buffer with Unity's authoritative Mesh.GetIndices(submesh,lod,true) for each native rendering LOD. Unity applies its own native LOD/submesh index offsets and baseVertex; GetLod ranges are retained as metadata and their index counts are cross-checked, never manually reinterpreted as raw-buffer offsets. All source-report foundation corners and centres are tested at every available rendering LOD; no proxy collider is used for these height results. Camera comparison changes only Connected_Lower_Massif.forceMeshLod in memory and restores it; no scene or asset is saved.";
            public List<SurfaceReport> surfaces = new List<SurfaceReport>();
            public List<PointResult> points = new List<PointResult>();
            public List<LevelSummary> summaries = new List<LevelSummary>();
            public List<string> captures = new List<string>(), errors = new List<string>();
        }
        sealed class Surface
        {
            public MeshRenderer renderer;
            public SurfaceReport report;
            public Vector3[] vertices;
            public int[][][] indices; // LOD, submesh, flattened triangle indices.
            public bool mirrored;
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run the read-only support audit outside Play mode.");
            if (!Application.isBatchMode && Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save existing edits before opening the saved candidate for a read-only audit.");
            Directory.CreateDirectory(Output);
            var report = new Report { timestampUtc = DateTime.UtcNow.ToString("o"), scene = ScenePath, sceneHashBefore = Hash(ScenePath) };
            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var input = JsonUtility.FromJson<InputReport>(File.ReadAllText(InputPath));
                var supports = input.supports.Where(s => s.ground != null && s.ground.Length > 0).ToArray();
                var names = supports.SelectMany(s => s.ground).Select(SourceName).Distinct().OrderBy(n => n).ToArray();
                var filters = All<MeshFilter>(scene).Where(f => f.sharedMesh).ToArray();
                var surfaces = new Dictionary<string, Surface>();
                foreach (string name in names)
                {
                    var matches = filters.Where(f => f.name == name).ToArray();
                    if (matches.Length != 1) { report.errors.Add("Expected one visible source " + name + ", found " + matches.Length); continue; }
                    var surface = ReadSurface(matches[0]); surfaces.Add(name, surface); report.surfaces.Add(surface.report);
                    if (!surface.report.rendererEnabled || !surface.report.objectActive) report.errors.Add("Reported foundation source is not currently visible: " + name);
                }
                foreach (var support in supports)
                for (int point = 0; point < support.ground.Length; point++)
                {
                    var ground = support.ground[point]; var name = SourceName(ground);
                    if (!surfaces.TryGetValue(name, out var surface)) continue;
                    float baseline = 0;
                    for (int lod = 0; lod < surface.indices.Length; lod++)
                    {
                        var result = Sample(surface, support, ground, point, lod);
                        if (lod == 0) baseline = result.surfaceY;
                        result.differenceFromLod0 = result.found ? result.surfaceY - baseline : 0;
                        report.points.Add(result);
                    }
                }
                foreach (var group in report.points.GroupBy(p => p.lod).OrderBy(g => g.Key))
                {
                    var cloister = group.Where(p => p.support.Contains("Cloister_Station_") && p.support.Contains("_Pier_")).ToArray();
                    report.summaries.Add(new LevelSummary { lod = group.Key, points = group.Count(), missingHits = group.Count(p => !p.found),
                        floatingPoints = group.Count(p => p.found && p.embed < 0), minimumEmbed = group.Where(p => p.found).Select(p => p.embed).DefaultIfEmpty(0).Min(),
                        maximumAbsoluteLodDisplacement = group.Where(p => p.found).Select(p => Mathf.Abs(p.differenceFromLod0)).DefaultIfEmpty(0).Max(),
                        cloisterPoints = cloister.Length, cloisterMissingHits = cloister.Count(p => !p.found), cloisterFloatingPoints = cloister.Count(p => p.found && p.embed < 0) });
                }
                report.nativeLodIndicesVerified = report.surfaces.All(s => s.ranges.All(r => r.nativeIndicesMatchRangeCount));
                CaptureComparison(scene, report);
            }
            catch (Exception e) { report.errors.Add(e.ToString()); }
            finally
            {
                report.sceneHashAfter = Hash(ScenePath); report.savedSceneUnchanged = report.sceneHashBefore == report.sceneHashAfter;
                File.WriteAllText(Path.Combine(Output, "support-visible-lods.json"), JsonUtility.ToJson(report, true));
                if (!Application.isBatchMode && previousSetup.Length > 0 && previousSetup.All(s => !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
            if (!report.savedSceneUnchanged || !report.nativeLodIndicesVerified || report.errors.Count > 0)
                throw new InvalidOperationException("SUPPORT_LOD_AUDIT_FAILED: " + string.Join("\n", report.errors));
            Debug.Log("SUPPORT_LOD_AUDIT_COMPLETE " + Output + " points=" + report.points.Count + " sceneUnchanged=" + report.savedSceneUnchanged);
        }

        static Surface ReadSurface(MeshFilter filter)
        {
            var mesh = filter.sharedMesh; var renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer) throw new InvalidOperationException("No MeshRenderer for foundation source " + filter.name);
            var result = new Surface { renderer = renderer, mirrored = filter.transform.localToWorldMatrix.determinant < 0,
                report = new SurfaceReport { objectName = filter.name, mesh = mesh.name, meshAsset = AssetDatabase.GetAssetPath(mesh),
                    vertices = mesh.vertexCount, lodCount = Mathf.Max(1, mesh.lodCount), forceMeshLod = renderer.forceMeshLod,
                    meshLodSelectionBias = renderer.meshLodSelectionBias, rendererEnabled = renderer.enabled, objectActive = renderer.gameObject.activeInHierarchy } };
            using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var data = snapshot[0];
                using (var vertexData = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(vertexData); result.vertices = new Vector3[data.vertexCount];
                    for (int i = 0; i < vertexData.Length; i++) result.vertices[i] = filter.transform.TransformPoint(vertexData[i]);
                }
                result.indices = new int[result.report.lodCount][][];
                for (int lod = 0; lod < result.indices.Length; lod++)
                {
                    result.indices[lod] = new int[mesh.subMeshCount][];
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        var submesh = mesh.GetSubMesh(sub);
                        if (submesh.topology != MeshTopology.Triangles) throw new InvalidOperationException("Nontriangle terrain " + filter.name);
                        int first = mesh.lodCount > 0 ? checked((int)mesh.GetLod(sub, lod).indexStart) : submesh.indexStart;
                        int count = mesh.lodCount > 0 ? checked((int)mesh.GetLod(sub, lod).indexCount) : submesh.indexCount;
                        // Native LOD range offsets are not assumed to be absolute
                        // offsets into MeshData's flattened buffer. Let Unity resolve
                        // its submesh/LOD layout and apply baseVertex exactly once.
                        var indices = mesh.GetIndices(sub, lod, true);
                        bool matches = indices.Length == count;
                        result.report.ranges.Add(new Range { submesh = sub, lod = lod, indexStart = first, indexCount = count, baseVertex = submesh.baseVertex, nativeIndicesMatchRangeCount = matches });
                        if (!matches) throw new InvalidOperationException("Unity LOD index count disagrees with GetLod: " + filter.name + " sub=" + sub + " lod=" + lod);
                        if (indices.Any(i => i < 0 || i >= result.vertices.Length)) throw new InvalidOperationException("LOD index outside terrain vertex buffer.");
                        result.indices[lod][sub] = indices;
                    }
                }
            }
            return result;
        }

        static PointResult Sample(Surface surface, InputSupport support, InputGround ground, int point, int lod)
        {
            var result = new PointResult { support = support.name, surface = surface.report.objectName, reportedKind = ground.surfaceKind,
                footpoint = point, lod = lod, xzAndReportedY = ground.point, foundationBottom = support.bottom, hitSubmesh = -1 };
            for (int sub = 0; sub < surface.indices[lod].Length; sub++)
            {
                var indices = surface.indices[lod][sub];
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    var a = surface.vertices[indices[i]]; var b = surface.vertices[indices[i+1]]; var c = surface.vertices[indices[i+2]];
                    float x = ground.point.x, z = ground.point.z;
                    if (x < Mathf.Min(a.x, Mathf.Min(b.x,c.x))-.0001f || x > Mathf.Max(a.x,Mathf.Max(b.x,c.x))+.0001f ||
                        z < Mathf.Min(a.z,Mathf.Min(b.z,c.z))-.0001f || z > Mathf.Max(a.z,Mathf.Max(b.z,c.z))+.0001f) continue;
                    var e = b-a; var f = c-a; float determinant = e.x*f.z-e.z*f.x;
                    if (Mathf.Abs(determinant) < .0000001f) continue;
                    float dx = x-a.x, dz = z-a.z, u = (dx*f.z-dz*f.x)/determinant, v = (e.x*dz-e.z*dx)/determinant;
                    if (u < -.00001f || v < -.00001f || u+v > 1.00001f) continue;
                    float height = a.y+u*e.y+v*f.y;
                    if (result.found && height <= result.surfaceY) continue;
                    result.found = true; result.surfaceY = height; result.hitSubmesh = sub;
                    result.frontFacingNormalY = Vector3.Cross(e,f).normalized.y * (surface.mirrored ? -1 : 1);
                }
            }
            if (result.found)
            {
                result.embed = result.surfaceY-support.bottom; result.differenceFromReported = result.surfaceY-ground.point.y;
                result.foundationBelowActualSurface = result.embed >= 0; result.frontFacingFromAbove = result.frontFacingNormalY > 0;
            }
            return result;
        }

        static void CaptureComparison(Scene scene, Report report)
        {
            var original = All<Camera>(scene).FirstOrDefault(c => c.GetComponent<Chapter01NightPostProcess>());
            var terrain = All<MeshRenderer>(scene).Single(r => r.name == "Connected_Lower_Massif");
            var budget = All<Chapter01SceneRenderBudget>(scene).Single();
            if (!original) throw new InvalidOperationException("No source night camera for comparison.");
            short savedForce = terrain.forceMeshLod;
            var go = new GameObject("ReadOnly support LOD observation") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, scene);
            try
            {
                var camera = go.AddComponent<Camera>(); camera.CopyFrom(original); camera.enabled = false; camera.targetTexture = null;
                camera.transform.SetPositionAndRotation(new Vector3(119,43,-44),Quaternion.LookRotation(new Vector3(80,24,-11)-new Vector3(119,43,-44),Vector3.up));
                camera.fieldOfView = 52; camera.nearClipPlane = .06f; camera.farClipPlane = 2600; camera.aspect = 2560f/1440;
                camera.orthographic = false; camera.renderingPath = budget.deferredShading ? RenderingPath.DeferredShading : RenderingPath.Forward;
                camera.allowHDR = true; camera.allowMSAA = !budget.deferredShading;
                var post = go.AddComponent<Chapter01NightPostProcess>(); EditorUtility.CopySerialized(original.GetComponent<Chapter01NightPostProcess>(),post); post.fxaa = budget.fxaa;
                var sky = original.GetComponent<Skybox>(); if (sky) { var copy = go.AddComponent<Skybox>();copy.material=sky.material;copy.enabled=sky.enabled; }
                using (budget.BeginPreviewQualityScope())
                {
                    foreach (int force in new[] { -1, 0 })
                    {
                        terrain.forceMeshLod = (short)force;
                        string path = Path.Combine(Output, force < 0 ? "cloister-terrain-auto.png" : "cloister-terrain-lod0.png");
                        Render(camera,path,budget.deferredShading?1:Mathf.Max(1,budget.antiAliasing));report.captures.Add(path);
                    }
                }
            }
            finally { terrain.forceMeshLod = savedForce; Object.DestroyImmediate(go); }
        }
        static void Render(Camera camera,string path,int samples)
        {
            var previous=RenderTexture.active;var target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Default){antiAliasing=samples};
            var pixels=new Texture2D(2560,1440,TextureFormat.RGB24,false,false);
            try { camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,2560,1440),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG()); }
            finally { camera.targetTexture=null;RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels); }
        }
        static string SourceName(InputGround p)
        {
            string name=string.IsNullOrEmpty(p.surfaceName)?p.collider:p.surfaceName;
            return name!=null&&name.StartsWith("Collision__",StringComparison.Ordinal)?name.Substring("Collision__".Length):name;
        }
        static IEnumerable<T> All<T>(Scene scene) where T:Component => scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true));
        static string Hash(string path) { using(var hash=SHA256.Create()) using(var stream=File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant(); }
    }
}
