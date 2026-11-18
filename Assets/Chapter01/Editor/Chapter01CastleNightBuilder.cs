using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Chapter01.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    public static class Chapter01CastleNightBuilder
    {
        public const string ScenePath = "Assets/Chapter01/Scenes/Chapter01_CastleNight.unity";
        public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Design/Chapter01-Production-v03/unity-castle-v025"));
        [Serializable] public sealed class RouteFile { public Vector3[] points; public float eyeHeight; public string sourceSha256; }
        [Serializable] public sealed class Report
        {
            public string scenePath = ScenePath, sourceScene = "Assets/Chapter01/Scenes/Chapter01_Review.unity", sourceBlendSha256, builtAt;
            public int clonedMaterials;
            public float coordinateError;
            public Chapter01CollisionBuilder.Report collision;
            public bool savedAndReopened;
            public string scope = "Castle night/ground/player-route scene v025; original Review and main gameplay scenes remain separate.";
        }
        public static Matrix4x4 BlenderToUnity()
        {
            var m = Matrix4x4.zero; m[0, 0] = -1; m[1, 2] = 1; m[2, 1] = -1; m[3, 3] = 1; return m;
        }
        public static void BuildAndPlayer()
        {
            Build();
            Chapter01NightLook.VerifySavedSceneLook();
            var physics = Chapter01CollisionDiagnostics.RunGate(SceneManager.GetActiveScene(), Chapter01RuntimeValidation.ReadRoute().points);
            File.WriteAllText(Path.Combine(Output, "physics-gate-final-build.json"), JsonUtility.ToJson(physics, true));
            if (!physics.passed) throw new InvalidOperationException("Saved scene failed its full-route static physics gate.");
            Chapter01RuntimeValidation.BuildPlayer();
        }
        [MenuItem("Shadow Lab/Chapter01/Build castle night v025")]
        public static void Build()
        {
            if (EditorBuildSettings.scenes.Any(x => x.enabled && x.path == ScenePath))
                throw new InvalidOperationException("CastleNight is now the authored main combat scene. Open it to edit the layout; use ShiyinBuild.BuildWindows to build. The old route-scene generator cannot overwrite the main scene.");
            Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.OpenScene("Assets/Chapter01/Scenes/Chapter01_Review.unity", OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not create v025 scene copy.");
            var export = JsonUtility.FromJson<Chapter01ReviewScene.Export>(File.ReadAllText(Path.Combine(Application.dataPath, "Chapter01/chapter01-export.json")));
            var route = JsonUtility.FromJson<RouteFile>(File.ReadAllText(Path.Combine(Application.dataPath, "Chapter01/Data/castle-route-v025.json")));
            var report = new Report { sourceBlendSha256 = export.sourceBlendSha256, builtAt = DateTime.UtcNow.ToString("o") };
            var map = BlenderToUnity();
            var transforms = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach (var anchor in export.coordinateAnchors)
            {
                var target = transforms.Single(x => x.name == anchor.name);
                var expected = map.MultiplyPoint3x4(new Vector3(anchor.worldPositionBlender[0], anchor.worldPositionBlender[1], anchor.worldPositionBlender[2]));
                report.coordinateError = Mathf.Max(report.coordinateError, Vector3.Distance(expected, target.position));
            }
            if (report.coordinateError > .001f) throw new InvalidOperationException("Coordinate map no longer matches the imported scene.");
            report.clonedMaterials = CloneMaterials(scene);
            report.collision = Chapter01CollisionBuilder.Apply(scene, route.points);
            File.WriteAllText(Path.Combine(Output, "collision-validation.json"), JsonUtility.ToJson(report.collision, true));
            if (report.collision.unacceptableFloors > 0) throw new InvalidOperationException("Route collision contains gaps, excessive height errors or steep surfaces; inspect collision-validation.json.");
            var ground = Chapter01GroundUpgrade.Apply(scene, map);
            File.WriteAllText(Path.Combine(Output, "ground-upgrade.json"), JsonUtility.ToJson(ground, true));
            var cameras = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).ToArray();
            var overview = cameras.Single(x => x.name == "Chapter01 Review Camera");
            var playerCamera = cameras.Single(x => x.name.StartsWith("Perspective LOD inspection", StringComparison.Ordinal));
            overview.enabled = false; overview.tag = "Untagged";
            var rig = Chapter01RuntimeValidation.InstallPlayerRig(scene, playerCamera, "Chapter01_Collision");
            playerCamera = rig.ViewCamera;
            var moon = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Light>(true)).Single(x => x.name == "Review Moon");
            var night = Chapter01NightLook.Apply(new[] { playerCamera, overview }, moon, map, export.skyTexture);
            File.WriteAllText(Path.Combine(Output, "night-look.json"), JsonUtility.ToJson(night, true));
            ConfigureRenderBudget(scene, rig.gameObject, playerCamera, overview);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save castle night scene.");
            Capture(overview, Path.Combine(Output, "01-unity-overall.png"), 1440, 1000);
            Capture(playerCamera, Path.Combine(Output, "02-unity-player-start.png"), 1920, 1080);
            CaptureGroundDetails(scene, playerCamera, route.points[0]);
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            report.savedAndReopened = scene.GetRootGameObjects().Any(x => x.name == "Chapter01_Collision") &&
                scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).Any(x => x.enabled && x.CompareTag("MainCamera"));
            if (!report.savedAndReopened) throw new InvalidOperationException("Saved runtime scene did not reopen with its player and collision.");
            File.WriteAllText(Path.Combine(Output, "scene-build.json"), JsonUtility.ToJson(report, true));
            Debug.Log("CHAPTER01_CASTLE_NIGHT_SCENE_PASS");
        }
        static void ConfigureRenderBudget(Scene scene, GameObject player, params Camera[] cameras)
        {
            var profile = player.GetComponent<Chapter01SceneRenderBudget>();
            if (!profile) profile = player.AddComponent<Chapter01SceneRenderBudget>();
            profile.cameras = cameras;
            int supports = 0;
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MeshRenderer>(true)))
            {
                if (!renderer.name.StartsWith("C05_", StringComparison.Ordinal) && !renderer.name.StartsWith("C07_", StringComparison.Ordinal)) continue;
                renderer.meshLodSelectionBias = 1;
                renderer.forceMeshLod = -1;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                ++supports;
            }
            profile.expectedSupportRenderers = supports;
            profile.ConfigureCameras();
            EditorUtility.SetDirty(profile);
            File.WriteAllText(Path.Combine(Output, "scene-render-budget.json"), JsonUtility.ToJson(new RenderBudgetReport {
                supportRenderers = supports, shadowDistance = profile.shadowDistance, shadowCascades = profile.shadowCascades,
                meshLodThreshold = profile.meshLodThreshold, supportBias = profile.savedSupportSelectionBias
            }, true));
        }
        [Serializable] sealed class RenderBudgetReport
        {
            public string renderingPath = "DeferredShading", antiAliasing = "FXAA after tone mapping; MSAA disabled";
            public string scope = "Scene defaults active in a normal Player/Play Mode without diagnostic CLI. Project quality restored after editor preview/Play Mode.";
            public string evidence = "diagnostic-matrix-results.json; selection based on real 1080p start-segment comparison, pending full-route validation.";
            public int supportRenderers, shadowCascades;
            public float shadowDistance, meshLodThreshold, supportBias;
        }
        static void CaptureGroundDetails(Scene scene, Camera camera, Vector3 arrival)
        {
            var root = scene.GetRootGameObjects().Single(x => x.name == "V025 Castle Ground Dressing");
            var patches = Enumerable.Range(0, root.transform.childCount).Select(i => root.transform.GetChild(i))
                .OrderBy(x => Vector3.Distance(x.position, arrival)).ToArray();
            if (patches.Length == 0) throw new InvalidOperationException("Ground dressing contains no plants.");
            var savedPosition = camera.transform.position; var savedRotation = camera.transform.rotation;
            try
            {
                var patch = patches[0];
                camera.transform.position = patch.position + new Vector3(2.5f, 1.7f, -2.5f);
                camera.transform.LookAt(patch.position + Vector3.up * .15f);
                Capture(camera, Path.Combine(Output, "03-unity-ground-detail.png"), 1920, 1080);
            }
            finally { camera.transform.SetPositionAndRotation(savedPosition, savedRotation); }
        }
        static int CloneMaterials(Scene scene)
        {
            const string folder = "Assets/Chapter01/Materials/NightV025";
            Directory.CreateDirectory(folder);
            var cache = new Dictionary<Material, Material>();
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Renderer>(true)))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; ++i)
                {
                    var source = slots[i]; if (source == null) continue;
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    if (!sourcePath.StartsWith("Assets/Chapter01/Materials/", StringComparison.Ordinal)) continue;
                    if (!cache.TryGetValue(source, out var copy))
                    {
                        string path = folder + "/" + Regex.Replace(source.name, "[^A-Za-z0-9_-]", "_") + "_" + AssetDatabase.AssetPathToGUID(sourcePath).Substring(0, 8) + ".mat";
                        copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (copy == null) { copy = new Material(source); AssetDatabase.CreateAsset(copy, path); }
                        else EditorUtility.CopySerialized(source, copy);
                        copy.name = source.name; copy.enableInstancing = false; EditorUtility.SetDirty(copy);
                        cache[source] = copy;
                    }
                    slots[i] = copy;
                }
                renderer.sharedMaterials = slots;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            AssetDatabase.SaveAssets(); return cache.Count;
        }
        public static void Capture(Camera camera, string path, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var profile = camera.gameObject.scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Chapter01SceneRenderBudget>(true)).FirstOrDefault();
            using var qualityScope = profile ? profile.BeginPreviewQualityScope() : null;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default) {
                antiAliasing = profile ? Mathf.Max(1, profile.antiAliasing) : 4 };
            var previous = RenderTexture.active; var oldTarget = camera.targetTexture; float aspect = camera.aspect;
            bool oldHdr = camera.allowHDR, oldMsaa = camera.allowMSAA;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            try
            {
                camera.aspect = width / (float)height; camera.targetTexture = target; camera.allowHDR = true; camera.allowMSAA = !profile || !profile.deferredShading;
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.aspect = aspect; camera.allowHDR = oldHdr; camera.allowMSAA = oldMsaa; RenderTexture.active = previous;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
