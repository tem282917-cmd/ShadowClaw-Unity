using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chapter01.Rendering;
using Chapter01.Runtime.Testing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chapter01.Editor
{
    /// <summary>Saved candidate geometry rendered by observation cameras, never a Player/FPS test.</summary>
    public static class CastleWalkwayCapture
    {
        const int Width = 2560, Height = 1440;
        const string Output = "D:/Shadow/Builds/CastleWalkway-Verification/views";

        [Serializable] sealed class View
        {
            public string name, file, pngSha256, renderingPath;
            public Vector3 position, target;
            public Quaternion rotation;
            public float fieldOfView = 52, orthographicSize;
            public bool orthographic, fxaa;
        }
        [Serializable] sealed class Report
        {
            public string timestampUtc, buildId, scene, sceneSha256Before, sceneSha256After;
            public string method = "Unity Editor observation-camera RenderTexture capture of the saved candidate, using its existing night postprocess, sky, materials and lights. Not an AI image, Player framebuffer or FPS measurement. Runtime gameplay/light-selection scripts are not simulated.";
            public int width = Width, height = Height, shadowCascades, qualityAntiAliasing, renderTextureSamples;
            public float shadowDistance, meshLodThreshold;
            public bool savedSceneUnchanged;
            public Bounds overviewBounds;
            public List<View> views = new List<View>();
        }

        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture the saved candidate outside Play mode.");
            if (!Application.isBatchMode && Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save current scene edits before loading the saved candidate for observation captures.");
            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            string before = CastleWalkwayInstaller.Hash(CastleWalkwayInstaller.CandidateScene);
            var scene = EditorSceneManager.OpenScene(CastleWalkwayInstaller.CandidateScene, OpenSceneMode.Single);
            var descriptor = CastleWalkwayInstaller.All<CastleWalkwayDescriptor>(scene).Single();
            var hall = CastleWalkwayInstaller.All<IronHallEncounter>(scene).Single();
            var budget = CastleWalkwayInstaller.All<Chapter01SceneRenderBudget>(scene).Single();
            var original = hall.GameplayCamera;
            var sourcePost = original ? original.GetComponent<Chapter01NightPostProcess>() : null;
            if (!descriptor.root || descriptor.path == null || descriptor.path.Length < 2 || string.IsNullOrWhiteSpace(descriptor.buildId))
                throw new InvalidOperationException("Saved candidate descriptor/path/build ID is incomplete.");
            if (!original || !sourcePost || !sourcePost.shader || sourcePost.diagnosticMode != 0)
                throw new InvalidOperationException("Saved gameplay camera must have its normal night atmosphere shader, not a diagnostic output.");
            Directory.CreateDirectory(Output);
            var report = new Report { timestampUtc = DateTime.UtcNow.ToString("o"), buildId = descriptor.buildId,
                scene = scene.path, sceneSha256Before = before };
            var go = new GameObject("CastleWalkway temporary observation camera") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, scene);
            try
            {
                var camera = go.AddComponent<Camera>(); camera.CopyFrom(original); camera.enabled = false;
                camera.targetTexture = null; camera.aspect = Width / (float)Height;
                camera.nearClipPlane = .06f; camera.farClipPlane = 2600;
                camera.renderingPath = budget.deferredShading ? RenderingPath.DeferredShading : RenderingPath.Forward;
                camera.allowHDR = true; camera.allowMSAA = !budget.deferredShading;
                var post = go.AddComponent<Chapter01NightPostProcess>(); EditorUtility.CopySerialized(sourcePost, post);
                // Match the scene-local runtime budget on the temporary camera only.
                post.fxaa = budget.fxaa;
                var originalSky = original.GetComponent<Skybox>();
                if (originalSky) { var sky = go.AddComponent<Skybox>(); sky.material = originalSky.material; sky.enabled = originalSky.enabled; }
                report.views.Add(new View { name = "cloister-supports", position = new Vector3(119, 43, -44), target = new Vector3(80, 24, -11) });
                report.views.Add(new View { name = "highbridge-piers", position = new Vector3(115, 63, 100), target = new Vector3(60, 35, 77) });
                report.views.Add(new View { name = "lower-causeway", position = new Vector3(-60, 25, -160), target = new Vector3(-15, 3, -125) });
                report.overviewBounds = FindOverviewBounds(scene, descriptor, hall);
                report.views.Add(Overview(report.overviewBounds));
                // Keep the original four views stable. This lower observation puts
                // all eight pier foundations inside the 60m shadow range and keeps
                // both their ground contacts and the upper bearing beams in frame.
                report.views.Add(new View { name = "cloister-supports-close", position = new Vector3(110, 18, -38), target = new Vector3(80, 11, -11), fieldOfView = 65 });
                using (budget.BeginPreviewQualityScope())
                {
                    report.shadowDistance = QualitySettings.shadowDistance; report.shadowCascades = QualitySettings.shadowCascades;
                    report.meshLodThreshold = QualitySettings.meshLodThreshold; report.qualityAntiAliasing = QualitySettings.antiAliasing;
                    report.renderTextureSamples = budget.deferredShading ? 1 : Mathf.Max(1, budget.antiAliasing);
                    for (int i = 0; i < report.views.Count; ++i)
                    {
                        var view = report.views[i];
                        camera.orthographic = view.orthographic;
                        camera.fieldOfView = view.fieldOfView; camera.orthographicSize = view.orthographicSize > 0 ? view.orthographicSize : 5;
                        view.rotation = Quaternion.LookRotation(view.target - view.position, Vector3.up);
                        camera.transform.SetPositionAndRotation(view.position, view.rotation);
                        view.file = "after-" + descriptor.buildId.Substring(0, Math.Min(8, descriptor.buildId.Length)) + "-" + (i + 1).ToString("D2") + "-" + view.name + ".png";
                        Render(camera, Path.Combine(Output, view.file), report.renderTextureSamples);
                        view.renderingPath = camera.actualRenderingPath.ToString(); view.fxaa = post.FxaaActive;
                        view.pngSha256 = CastleWalkwayInstaller.Hash(Path.Combine(Output, view.file));
                        Debug.Log("CASTLE_WALKWAY_OBSERVATION_CAPTURE " + view.file + " " + Width + "x" + Height + "; not a Player/FPS test");
                    }
                }
                report.sceneSha256After = CastleWalkwayInstaller.Hash(CastleWalkwayInstaller.CandidateScene);
                report.savedSceneUnchanged = before == report.sceneSha256After;
                if (!report.savedSceneUnchanged) throw new InvalidOperationException("Saved candidate changed while capturing; do not use these images as its review evidence.");
                File.WriteAllText(Path.Combine(Output, "capture-" + descriptor.buildId + ".json"), JsonUtility.ToJson(report, true));
                Debug.Log("CASTLE_WALKWAY_OBSERVATION_CAPTURE_PASS build=" + descriptor.buildId + " images=" + report.views.Count + " savedSceneUnchanged=true");
            }
            finally
            {
                Object.DestroyImmediate(go);
                // No SaveScene/SaveAssets call: only temporary observation objects were used.
                if (!Application.isBatchMode && previousSetup.Length > 0 && previousSetup.All(x => !string.IsNullOrEmpty(x.path)))
                {
                    EditorSceneManager.OpenScene(CastleWalkwayInstaller.CandidateScene, OpenSceneMode.Single);
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        static Bounds FindOverviewBounds(Scene scene, CastleWalkwayDescriptor descriptor, IronHallEncounter hall)
        {
            var bounds = new Bounds(descriptor.path[0], Vector3.zero);
            foreach (var point in descriptor.path) bounds.Encapsulate(point);
            if (hall.BattleArea) bounds.Encapsulate(hall.BattleArea.bounds);
            var footprint = bounds; footprint.Expand(new Vector3(50, 0, 50));
            foreach (var renderer in CastleWalkwayInstaller.All<MeshRenderer>(scene))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                bool walkway = renderer.transform.IsChildOf(descriptor.root);
                bool castle = renderer.name.StartsWith("A", StringComparison.Ordinal) || renderer.name.StartsWith("B", StringComparison.Ordinal);
                if (!walkway && !castle) continue;
                var b = renderer.bounds;
                if (b.size.sqrMagnitude < .0001f || Mathf.Max(b.size.x, b.size.y, b.size.z) > 400) continue;
                if (b.max.x < footprint.min.x || b.min.x > footprint.max.x || b.max.z < footprint.min.z || b.min.z > footprint.max.z) continue;
                bounds.Encapsulate(b);
            }
            return bounds;
        }
        static View Overview(Bounds bounds)
        {
            var outward = new Vector3(1.15f, .95f, -1.5f).normalized;
            var rotation = Quaternion.LookRotation(-outward, Vector3.up);
            var right = rotation * Vector3.right; var up = rotation * Vector3.up;
            float halfWidth = 0, halfHeight = 0, halfDepth = 0;
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                var corner = Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(corner, right)));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(corner, up)));
                halfDepth = Mathf.Max(halfDepth, Mathf.Abs(Vector3.Dot(corner, outward)));
            }
            return new View { name = "castle-route-overview", orthographic = true, target = bounds.center,
                position = bounds.center + outward * (halfDepth + 80),
                orthographicSize = Mathf.Max(halfHeight, halfWidth / (Width / (float)Height)) * 1.10f };
        }
        static void Render(Camera camera, string path, int samples)
        {
            var previous = RenderTexture.active;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default) { antiAliasing = samples };
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = target; camera.aspect = Width / (float)Height;
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
