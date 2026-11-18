using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chapter01.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    /// <summary>Called by the scene builder after it has assigned source materials, lights and camera poses.</summary>
    public static class Chapter01NightLook
    {
        const string Root = "Assets/Chapter01/";
        const string PostShaderPath = Root + "Runtime/Rendering/Chapter01NightPost.shader";

        [Serializable]
        public sealed class MaterialCheck
        {
            public string name, assetPath, emissionTexture, shader;
            public bool keywordBefore, keywordAfter, wasMarkedBlack, shouldEmit;
            public int actualSceneSlots;
            public float[] emissionColor;
        }

        [Serializable]
        public sealed class Report
        {
            public string pipeline = "Built-in; no pipeline or project color-space change";
            public string colorSpace;
            public int cameraCount, emissiveMaterialCount, fixedEmissionKeywords, pointLightCount;
            public float moonIntensity, fillIntensity, skyExposure, reflectionIntensity;
            public bool hdr, depthFog, runtimePostProcessing, shaderSupported;
            public bool savedSceneReopened, savedEmissionFlagsVerified, savedFogParametersVerified;
            public float fogStart, distanceFogDensity, maximumFogOpacity, fogRayLimit;
            public Color fogColor;
            public Chapter01NightPostProcess.MistVolume[] fogVolumes;
            public string depthAudit = "DepthTextureMode.Depth requested on both cameras; diagnostics render actual sampled camera depth and integrated fog opacity.";
            public string[] cameraDepthModes;
            public string performance = "2 full-resolution passes, 1 half-resolution and 3 quarter-resolution passes; 4 analytic mist volumes; no fog geometry or shadowed point lights";
            public List<MaterialCheck> materials = new List<MaterialCheck>();
        }

        public static Report Apply(Camera[] cameras, Light moon, Matrix4x4 blenderToUnity, string skyTexturePath)
        {
            if (cameras == null || cameras.Length == 0 || cameras.Any(x => x == null))
                throw new ArgumentException("At least one valid night-look camera is required.");
            var scene = cameras[0].gameObject.scene;
            var report = new Report { colorSpace = QualitySettings.activeColorSpace.ToString(), cameraCount = cameras.Length };
            var export = JsonUtility.FromJson<Chapter01ReviewScene.Export>(File.ReadAllText(
                Path.Combine(Application.dataPath, "Chapter01/chapter01-export.json")));
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(PostShaderPath);
            var emissionShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "Runtime/Rendering/Chapter01NativeEmission.shader");
            if (shader == null) throw new FileNotFoundException("Night post-process shader must be imported before building.", PostShaderPath);
            if (emissionShader == null) throw new FileNotFoundException("Native emission shader must be imported before building.");
            report.shaderSupported = shader.isSupported;
            if (!shader.isSupported) throw new InvalidOperationException("Night post-process shader is unsupported on this editor graphics device.");
            var renderers = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).ToArray();
            var slotMaterials = renderers.SelectMany(x => x.sharedMaterials).Where(x => x != null).ToArray();
            var materialDescriptions = (export.materials ?? Array.Empty<Chapter01ReviewScene.MaterialInfo>())
                .ToDictionary(x => x.name, x => x, StringComparer.Ordinal);
            foreach (var material in slotMaterials.Distinct())
            {
                if (!material.HasProperty("_EmissionColor")) continue;
                if (!materialDescriptions.TryGetValue(material.name, out var source)) continue;
                var emission = ColorOf(source.emissionColor, Color.black);
                bool hasEnergy = emission.maxColorComponent > 0.00001f;
                var check = new MaterialCheck
                {
                    name = material.name, assetPath = AssetDatabase.GetAssetPath(material),
                    emissionTexture = source.emissionTexture,
                    emissionColor = new[] { emission.r, emission.g, emission.b, emission.a },
                    shouldEmit = hasEnergy, keywordBefore = material.IsKeywordEnabled("_EMISSION"),
                    wasMarkedBlack = (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) != 0,
                    actualSceneSlots = slotMaterials.Count(x => x == material)
                };
                material.SetColor("_EmissionColor", emission);
                material.SetTexture("_EmissionMap", string.IsNullOrEmpty(source.emissionTexture) ? null
                    : AssetDatabase.LoadAssetAtPath<Texture2D>(source.emissionTexture));
                if (hasEnergy)
                {
                    if (material.name.StartsWith("Night_", StringComparison.Ordinal))
                    {
                        material.shader = emissionShader;
                        if (material.GetTexture("_MetallicGlossMap") != null) material.EnableKeyword("_METALLICGLOSSMAP");
                        else material.DisableKeyword("_METALLICGLOSSMAP");
                        if (material.GetTexture("_BumpMap") != null) material.EnableKeyword("_NORMALMAP");
                        else material.DisableKeyword("_NORMALMAP");
                    }
                    // Standard's material validation derives _EMISSION from EmissiveIsBlack.
                    // EnableKeyword alone is undone when an asset with flag 4 is revalidated.
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    material.EnableKeyword("_EMISSION");
                    ++report.emissiveMaterialCount;
                    if (!check.keywordBefore || check.wasMarkedBlack) ++report.fixedEmissionKeywords;
                }
                else
                {
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    material.DisableKeyword("_EMISSION");
                }
                check.keywordAfter = material.IsKeywordEnabled("_EMISSION");
                check.shader = material.shader.name;
                report.materials.Add(check);
                EditorUtility.SetDirty(material);
            }
            // The original 4K star image contains single-pixel stars. Default 2K import erases many of them.
            // Give this scene a private texture import so the accepted review scene keeps its settings.
            var skyCopyPath = Root + "Runtime/Rendering/NightV025_Stars" + Path.GetExtension(skyTexturePath);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(skyCopyPath) == null && !AssetDatabase.CopyAsset(skyTexturePath, skyCopyPath))
                throw new IOException("Could not create the private night sky texture.");
            var skyImporter = AssetImporter.GetAtPath(skyCopyPath) as TextureImporter;
            if (skyImporter == null) throw new FileNotFoundException("Native sky texture missing.", skyTexturePath);
            if (skyImporter.maxTextureSize < 4096 || !skyImporter.sRGBTexture || skyImporter.textureCompression != TextureImporterCompression.Uncompressed)
            {
                skyImporter.maxTextureSize = 4096;
                skyImporter.sRGBTexture = true;
                skyImporter.textureCompression = TextureImporterCompression.Uncompressed;
                skyImporter.SaveAndReimport();
            }
            var previousSky = RenderSettings.skybox;
            if (previousSky == null || !previousSky.HasProperty("_MainTex")) throw new InvalidOperationException("Create the panoramic sky before applying the night look.");
            if (!AssetDatabase.IsValidFolder(Root + "Materials/NightV025"))
                AssetDatabase.CreateFolder(Root + "Materials", "NightV025");
            var skyPath = Root + "Materials/NightV025/Chapter01_Night_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(previousSky) { name = "Chapter01 Night V025 Sky" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            RenderSettings.skybox = sky;
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(skyCopyPath));
            sky.SetFloat("_Exposure", 0.85f);
            if (sky.HasProperty("_Tint")) sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1));
            EditorUtility.SetDirty(sky);
            report.skyExposure = 0.85f;

            // Scene-scoped environment state is saved in the review scene and also applies in a player.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.085f, 0.105f, 0.14f);
            RenderSettings.ambientEquatorColor = new Color(0.045f, 0.057f, 0.075f);
            RenderSettings.ambientGroundColor = new Color(0.019f, 0.025f, 0.033f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = 0.18f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 128;
            RenderSettings.fog = false; // Depth-aware post fog replaces uniform built-in fog, avoiding double fogging.
            report.reflectionIntensity = RenderSettings.reflectionIntensity;

            if (moon == null) throw new ArgumentNullException(nameof(moon));
            var nativeMoon = export.lights.FirstOrDefault(x => x.name == "Moonlight_Key");
            var nativeFill = export.lights.FirstOrDefault(x => x.name == "Night_Sky_Fill");
            SetSun(moon, nativeMoon, blenderToUnity, 0.55f, true);
            RenderSettings.sun = moon;
            var fill = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Light>(true))
                .FirstOrDefault(x => x.name == "Chapter01 Moon sky fill");
            if (fill == null)
            {
                fill = new GameObject("Chapter01 Moon sky fill", typeof(Light)).GetComponent<Light>();
                SceneManager.MoveGameObjectToScene(fill.gameObject, scene);
            }
            SetSun(fill, nativeFill, blenderToUnity, 0.25f, false);
            report.moonIntensity = moon.intensity;
            report.fillIntensity = fill.intensity;
            float scale = blenderToUnity.MultiplyVector(Vector3.right).magnitude;
            var existingPoints = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Light>(true))
                .Where(x => x.type == LightType.Point).ToArray();
            var hall = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<IronHallEncounter>(true)).FirstOrDefault();
            var combatLights = hall && hall.GalleryLights != null ? hall.GalleryLights.Where(x => x).ToArray() : Array.Empty<Light>();
            var points = new List<Light>();
            foreach (var source in export.lights.Where(x => x.type == "POINT"))
            {
                // The combat installer replaces the six export sources with lights on the gameplay wicks.
                if (hall && hall.UseAuthoredLayout &&
                    source.name.StartsWith("WarmPool__D02__Six_Combat_Lantern_", StringComparison.Ordinal)) continue;
                var point = existingPoints.FirstOrDefault(x => x.name == source.name || x.name == source.name + " (Review)");
                var sourcePosition = source.WorldPosition(blenderToUnity);
                if (point == null)
                {
                    point = new GameObject(source.name, typeof(Light)).GetComponent<Light>();
                    SceneManager.MoveGameObjectToScene(point.gameObject, scene);
                    point.transform.position = sourcePosition;
                }
                point.name = source.name;
                Chapter01ReviewScene.PlaceExportedLight(point, source.name, sourcePosition, renderers);
                point.type = LightType.Point;
                point.color = ColorOf(source.color, new Color(1, 0.43f, 0.13f));
                point.intensity = Mathf.Clamp(source.energy / 200f, 0.25f, 3.2f);
                point.range = (source.range > 0 ? source.range : 12) * scale;
                point.enabled = true;
                // Match native practical sources: lit glass is the visual source; a bounded unshadowed
                // point light provides the nearby warm pool, without opaque lamp housings hiding it.
                point.shadows = LightShadows.None;
                point.renderMode = LightRenderMode.Auto;
                points.Add(point);
            }
            report.pointLightCount = points.Count + combatLights.Length;
            var budget = cameras[0].GetComponent<Chapter01PracticalLightBudget>();
            if (budget == null) budget = cameras[0].gameObject.AddComponent<Chapter01PracticalLightBudget>();
            budget.viewer = cameras[0].transform;
            budget.lights = points.ToArray();
            budget.maximumActiveLights = 24;
            EditorUtility.SetDirty(budget);
            var volumes = new[]
            {
                Volume(blenderToUnity, new Vector3(-100, 20, -35), new Vector3(625, 600, 55), 0.0026f / scale),
                Volume(blenderToUnity, new Vector3(-90, -20, 20), new Vector3(350, 350, 24), 0.0012f / scale),
                Volume(blenderToUnity, new Vector3(-250, 65, -12), new Vector3(150, 135, 12.5f), 0.0014f / scale),
                // Native final_balance.py: 2600 x 2500 x 450 volume centered at (0,-400,10).
                Volume(blenderToUnity, new Vector3(0, -400, 10), new Vector3(1300, 1250, 225), 0.0008f / scale,
                    new Vector3(-90, 20, 0), 250 * scale, 620 * scale)
            };
            foreach (var camera in cameras)
            {
                camera.allowHDR = true;
                camera.depthTextureMode |= DepthTextureMode.Depth;
                var post = camera.GetComponent<Chapter01NightPostProcess>();
                if (post == null) post = camera.gameObject.AddComponent<Chapter01NightPostProcess>();
                post.shader = shader;
                post.exposure = 0.12f;
                post.bloomThreshold = 1.05f;
                post.bloomSoftKnee = 0.55f;
                post.bloomIntensity = 0.18f;
                post.mistVolumes = volumes;
                post.fogStart = 85 * scale;
                post.fogRayLimit = 2600 * scale;
                post.distanceFogDensity = 0.00024f / scale;
                post.fogColor = new Color(0.18f, 0.26f, 0.34f, 1);
                post.diagnosticMode = 0;
                EditorUtility.SetDirty(post);
            }
            var firstPost = cameras[0].GetComponent<Chapter01NightPostProcess>();
            report.fogVolumes = volumes;
            report.fogColor = firstPost.fogColor;
            report.fogStart = firstPost.fogStart;
            report.distanceFogDensity = firstPost.distanceFogDensity;
            report.maximumFogOpacity = firstPost.maximumFogOpacity;
            report.fogRayLimit = firstPost.fogRayLimit;
            report.cameraDepthModes = cameras.Select(x => x.name + ": " + x.depthTextureMode).ToArray();
            report.hdr = report.depthFog = report.runtimePostProcessing = true;
            AssetDatabase.SaveAssets();
            DynamicGI.UpdateEnvironment();
            return report;
        }

        static Chapter01NightPostProcess.MistVolume Volume(Matrix4x4 map, Vector3 center, Vector3 radius, float density,
            Vector3 clearCenter = default(Vector3), float clearRadius = 0, float fullDensityRadius = 0)
        {
            var x = map.MultiplyVector(new Vector3(radius.x, 0, 0));
            var y = map.MultiplyVector(new Vector3(0, radius.y, 0));
            var z = map.MultiplyVector(new Vector3(0, 0, radius.z));
            return new Chapter01NightPostProcess.MistVolume { center = map.MultiplyPoint3x4(center), density = density,
                clearCenter = map.MultiplyPoint3x4(clearCenter), clearRadius = clearRadius, fullDensityRadius = fullDensityRadius,
                radius = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) };
        }

        [MenuItem("Shadow Lab/Chapter01/Refresh castle night look only")]
        public static void RefreshLook()
        {
            string path = Chapter01CastleNightBuilder.ScenePath;
            string output = Chapter01CastleNightBuilder.Output;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var cameras = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).ToArray();
            var player = cameras.Single(x => x.CompareTag("MainCamera"));
            var overview = cameras.Single(x => x.name == "Chapter01 Review Camera");
            var moon = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Light>(true)).Single(x => x.name == "Review Moon");
            var export = JsonUtility.FromJson<Chapter01ReviewScene.Export>(File.ReadAllText(
                Path.Combine(Application.dataPath, "Chapter01/chapter01-export.json")));
            var report = Apply(new[] { player, overview }, moon, Chapter01CastleNightBuilder.BlenderToUnity(), export.skyTexture);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not save updated castle night look.");
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            cameras = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).ToArray();
            player = cameras.Single(x => x.CompareTag("MainCamera"));
            overview = cameras.Single(x => x.name == "Chapter01 Review Camera");
            VerifyScene(scene, report, new[] { player, overview });
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "night-look.json"), JsonUtility.ToJson(report, true));
            Chapter01CastleNightBuilder.Capture(overview, Path.Combine(output, "01-unity-overall.png"), 1440, 1000);
            Chapter01CastleNightBuilder.Capture(player, Path.Combine(output, "02-unity-player-start.png"), 1920, 1080);
            CaptureFogAudit(overview, output);
            Debug.Log("CHAPTER01_NIGHT_LOOK_REFRESH_PASS: saved/reopened emission flags, shader references and fog parameters verified; depth/fog diagnostic images written.");
        }

        // Call after a full Build in the same Unity process; this does not rebuild collision or dressing.
        public static void VerifySavedSceneLook()
        {
            string output = Chapter01CastleNightBuilder.Output;
            var report = JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(output, "night-look.json")));
            var scene = EditorSceneManager.OpenScene(Chapter01CastleNightBuilder.ScenePath, OpenSceneMode.Single);
            var cameras = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).ToArray();
            var player = cameras.Single(x => x.CompareTag("MainCamera"));
            var overview = cameras.Single(x => x.name == "Chapter01 Review Camera");
            VerifyScene(scene, report, new[] { player, overview });
            File.WriteAllText(Path.Combine(output, "night-look.json"), JsonUtility.ToJson(report, true));
            CaptureFogAudit(overview, output);
            Debug.Log("CHAPTER01_NIGHT_LOOK_SAVED_AUDIT_PASS");
        }

        static void VerifyScene(Scene scene, Report report, Camera[] cameras)
        {
            var materials = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Renderer>(true))
                .SelectMany(x => x.sharedMaterials).Where(x => x != null).Distinct().ToArray();
            var emitting = report.materials.Where(x => x.shouldEmit).ToArray();
            report.savedEmissionFlagsVerified = emitting.All(check => materials.Any(m => m.name == check.name &&
                m.IsKeywordEnabled("_EMISSION") && (m.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0 &&
                m.shader.isSupported && m.shader.name == check.shader &&
                (string.IsNullOrEmpty(check.emissionTexture) || AssetDatabase.GetAssetPath(m.GetTexture("_EmissionMap")) == check.emissionTexture)));
            report.savedFogParametersVerified = cameras.All(c =>
            {
                var post = c.GetComponent<Chapter01NightPostProcess>();
                return post != null && post.enabled && post.shader != null && post.shader.isSupported && post.diagnosticMode == 0 &&
                    (c.depthTextureMode & DepthTextureMode.Depth) != 0 && post.mistVolumes.Length == 4 &&
                    Mathf.Approximately(post.mistVolumes[3].clearRadius, 250) &&
                    Mathf.Approximately(post.mistVolumes[3].fullDensityRadius, 620) && Mathf.Approximately(post.fogRayLimit, 2600);
            });
            report.savedSceneReopened = scene.IsValid() && scene.isLoaded && report.savedEmissionFlagsVerified && report.savedFogParametersVerified;
            if (!report.savedSceneReopened) throw new InvalidOperationException("Saved night look failed emission/fog/depth verification.");
        }

        static void CaptureFogAudit(Camera overview, string output)
        {
            var auditPost = overview.GetComponent<Chapter01NightPostProcess>();
            try
            {
                auditPost.diagnosticMode = 1;
                Chapter01CastleNightBuilder.Capture(overview, Path.Combine(output, "04-depth-audit.png"), 960, 667);
                auditPost.diagnosticMode = 2;
                Chapter01CastleNightBuilder.Capture(overview, Path.Combine(output, "05-fog-opacity-audit.png"), 960, 667);
            }
            finally { auditPost.diagnosticMode = 0; }
        }

        static void SetSun(Light light, Chapter01ReviewScene.LightInfo source, Matrix4x4 map, float intensity, bool shadows)
        {
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = source != null ? ColorOf(source.color, Color.white) : new Color(0.52f, 0.67f, 1);
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.85f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.25f;
            if (source != null && source.rotationEulerBlender != null)
            {
                var e = source.rotationEulerBlender;
                // Blender XYZ Euler composition is Rz * Ry * Rx; Unity Quaternion.Euler uses ZXY.
                var rotation = Quaternion.AngleAxis(e[2] * Mathf.Rad2Deg, Vector3.forward)
                    * Quaternion.AngleAxis(e[1] * Mathf.Rad2Deg, Vector3.up)
                    * Quaternion.AngleAxis(e[0] * Mathf.Rad2Deg, Vector3.right);
                light.transform.rotation = Quaternion.LookRotation(map.MultiplyVector(rotation * Vector3.back),
                    map.MultiplyVector(rotation * Vector3.up));
            }
        }

        static Color ColorOf(float[] values, Color fallback) => values != null && values.Length >= 3
            ? new Color(values[0], values[1], values[2], values.Length > 3 ? values[3] : 1) : fallback;
    }
}
