using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    public static class Chapter01LodValidation
    {
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../Design/Chapter01-Production-v03/engine-lod-validation-v001"));

        [Serializable] sealed class ExportManifest { public Sample[] samples; }
        [Serializable] sealed class Sample { public string source; public string assetPath; public int sourceTriangles; }
        [Serializable] sealed class Result
        {
            public string assetPath;
            public string profile;
            public int sourceTriangles;
            public long[] trianglesByLod;
            public int meshCount;
            public int rendererCount;
            public int maximumGeneratedLevel;
            public int selectionBias;
            public bool originalLod0Preserved;
            public bool automaticSelection;
            public bool staticBatchingDisabled;
            public bool classicInstancingDisabled;
            public DistanceProof[] automaticDistanceProof;
        }
        [Serializable] sealed class DistanceProof
        {
            public string view;
            public float distanceFromBoundsCenter;
            public int[] pixelIdenticalForcedLevels;
            public long[] possibleRenderedTriangles;
            public int changedPixelsComparedWithLod0;
        }
        [Serializable] sealed class Report
        {
            public string unityVersion;
            public string timestampUtc;
            public float meshLodThreshold;
            public string note;
            public bool passed;
            public List<Result> samples = new List<Result>();
            public List<string> failures = new List<string>();
        }

        [MenuItem("Shadow Lab/Chapter01/Validate actual Mesh LOD samples")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            var report = new Report {
                unityVersion = Application.unityVersion,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                meshLodThreshold = QualitySettings.meshLodThreshold,
                note = "Actual imported FBX index ranges; automatic near/far renders identified by pixel-identical comparison with each forced LOD at the same camera. Distances use the individual source sample scales, not final scene meter thresholds. No full-frame triangle counter or frame-rate result is implied."
            };
            try
            {
                var manifest = JsonUtility.FromJson<ExportManifest>(File.ReadAllText(Path.Combine(Output, "sample-export.json")));
                foreach (var sample in manifest.samples)
                {
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(sample.assetPath);
                    if (root == null)
                    {
                        AssetDatabase.ImportAsset(sample.assetPath, ImportAssetOptions.ForceSynchronousImport);
                        root = AssetDatabase.LoadAssetAtPath<GameObject>(sample.assetPath);
                    }
                    if (root == null) throw new InvalidOperationException("Import did not produce a model: " + sample.assetPath);
                    var meshes = root.GetComponentsInChildren<MeshFilter>(true).Select(x => x.sharedMesh).Where(x => x != null).ToArray();
                    var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                    Chapter01LodImportPolicy.TryGetProfile(sample.assetPath, out var profile);
                    int levelCount = meshes.Max(mesh => mesh.lodCount);
                    var triangles = new long[levelCount];
                    for (int level = 0; level < levelCount; ++level)
                        foreach (var mesh in meshes)
                            for (int sub = 0; sub < mesh.subMeshCount; ++sub)
                                triangles[level] += mesh.GetLod(sub, Math.Min(level, mesh.lodCount - 1)).indexCount / 3;
                    var result = new Result {
                        assetPath = sample.assetPath, profile = profile.name, sourceTriangles = sample.sourceTriangles,
                        trianglesByLod = triangles, meshCount = meshes.Length, rendererCount = renderers.Length,
                        maximumGeneratedLevel = levelCount - 1, selectionBias = profile.selectionBias,
                        originalLod0Preserved = triangles[0] == sample.sourceTriangles,
                        automaticSelection = renderers.All(x => x.forceMeshLod == -1),
                        staticBatchingDisabled = renderers.All(x => (GameObjectUtility.GetStaticEditorFlags(x.gameObject) & StaticEditorFlags.BatchingStatic) == 0),
                        classicInstancingDisabled = renderers.All(x => x.sharedMaterials.All(m => m == null || !m.enableInstancing))
                    };
                    report.samples.Add(result);
                    if (!result.originalLod0Preserved) report.failures.Add(sample.assetPath + ": LOD0 triangle count differs from source.");
                    if (levelCount < 2 || triangles[levelCount - 1] >= triangles[0]) report.failures.Add(sample.assetPath + ": no reduction generated.");
                    if (!result.automaticSelection || !result.staticBatchingDisabled || !result.classicInstancingDisabled)
                        report.failures.Add(sample.assetPath + ": runtime Mesh LOD eligibility check failed.");
                    result.automaticDistanceProof = RenderComparison(root, Path.GetFileNameWithoutExtension(sample.assetPath));
                    if (result.automaticDistanceProof.Any(x => x.pixelIdenticalForcedLevels.Length == 0))
                        report.failures.Add(sample.assetPath + ": automatic render did not match any known LOD.");
                    if (result.automaticDistanceProof[1].changedPixelsComparedWithLod0 == 0)
                        report.failures.Add(sample.assetPath + ": distant automatic render still matches LOD0; switching is unproven.");
                    Debug.Log("CHAPTER01_LOD_SAMPLE " + JsonUtility.ToJson(result));
                }
            }
            catch (Exception exception)
            {
                report.failures.Add(exception.ToString());
            }
            report.passed = report.failures.Count == 0 && report.samples.Count == 3;
            File.WriteAllText(Path.Combine(Output, "unity-lod-validation.json"), JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("CHAPTER01_LOD_FAILED: " + string.Join("\n", report.failures));
            Debug.Log("CHAPTER01_LOD_VALIDATION_PASS " + Output);
        }

        static DistanceProof[] RenderComparison(GameObject model, string name)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            Material material = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            var previousTarget = RenderTexture.active;
            var proofs = new List<DistanceProof>();
            try
            {
                var instance = UnityEngine.Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(instance, scene);
                foreach (var transform in instance.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 30;
                var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                var bounds = renderers[0].bounds;
                material = new Material(Shader.Find("Standard")) { color = new Color(0.65f, 0.68f, 0.72f) };
                material.SetFloat("_Glossiness", 0.1f);
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                }
                var cameraObject = new GameObject("LOD Validation Camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.cameraType = CameraType.Preview;
                camera.scene = scene;
                camera.cullingMask = 1 << 30;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.035f, 0.045f, 0.065f);
                camera.fieldOfView = 35;
                float distance = bounds.extents.magnitude / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
                camera.transform.position = bounds.center + new Vector3(3, 1.6f, -4).normalized * distance;
                camera.transform.LookAt(bounds.center);
                camera.nearClipPlane = Math.Max(0.001f, distance / 1000);
                camera.farClipPlane = distance * 10;
                foreach (var angles in new[] { new Vector3(35, -40, 0), new Vector3(320, 140, 0) })
                {
                    var lightObject = new GameObject("LOD Validation Light", typeof(Light));
                    SceneManager.MoveGameObjectToScene(lightObject, scene);
                    lightObject.transform.rotation = Quaternion.Euler(angles);
                    var light = lightObject.GetComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = 1.2f;
                    light.cullingMask = 1 << 30;
                    light.shadows = LightShadows.None;
                }
                target = new RenderTexture(512, 512, 24);
                camera.targetTexture = target;
                pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
                foreach (bool reduced in new[] { false, true })
                {
                    foreach (var renderer in renderers)
                    {
                        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        renderer.forceMeshLod = (short)(reduced ? mesh.lodCount - 1 : 0);
                    }
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                    pixels.Apply();
                    var values = pixels.GetPixels32();
                    if (values.All(p => p.Equals(values[0])))
                        throw new InvalidOperationException("Empty preview render for " + name);
                    File.WriteAllBytes(Path.Combine(Output, name + (reduced ? "-last-lod.png" : "-lod0.png")), pixels.EncodeToPNG());
                }
                int lodCount = renderers.Max(r => r.GetComponent<MeshFilter>().sharedMesh.lodCount);
                foreach (var factor in new[] { 1f, 8f })
                {
                    camera.transform.position = bounds.center + new Vector3(3, 1.6f, -4).normalized * distance * factor;
                    camera.farClipPlane = distance * factor * 10;
                    foreach (var renderer in renderers) renderer.forceMeshLod = -1;
                    var automatic = RenderPixels(camera, target, pixels);
                    string view = factor == 1 ? "near" : "far";
                    File.WriteAllBytes(Path.Combine(Output, name + "-automatic-" + view + ".png"), pixels.EncodeToPNG());
                    var matches = new List<int>();
                    var triangleCandidates = new List<long>();
                    int differenceFromLod0 = 0;
                    for (int level = 0; level < lodCount; ++level)
                    {
                        long triangleCount = 0;
                        foreach (var renderer in renderers)
                        {
                            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                            int actual = Math.Min(level, mesh.lodCount - 1);
                            renderer.forceMeshLod = (short)actual;
                            for (int sub = 0; sub < mesh.subMeshCount; ++sub) triangleCount += mesh.GetLod(sub, actual).indexCount / 3;
                        }
                        var forced = RenderPixels(camera, target, pixels);
                        int changed = 0;
                        for (int pixel = 0; pixel < forced.Length; ++pixel)
                            if (!forced[pixel].Equals(automatic[pixel])) ++changed;
                        if (level == 0) differenceFromLod0 = changed;
                        if (changed == 0) { matches.Add(level); triangleCandidates.Add(triangleCount); }
                    }
                    proofs.Add(new DistanceProof { view = view, distanceFromBoundsCenter = distance * factor,
                        pixelIdenticalForcedLevels = matches.ToArray(), possibleRenderedTriangles = triangleCandidates.ToArray(),
                        changedPixelsComparedWithLod0 = differenceFromLod0 });
                }
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            return proofs.ToArray();
        }

        static Color32[] RenderPixels(Camera camera, RenderTexture target, Texture2D pixels)
        {
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            return pixels.GetPixels32();
        }
    }
}
