using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    public static class Chapter01ReviewScene
    {
        const string Root = "Assets/Chapter01/";
        const string ScenePath = Root + "Scenes/Chapter01_Review.unity";
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../Design/Chapter01-Production-v03/engine-lod-validation-v001"));

        [Serializable] public sealed class Export
        {
            public ModelFile[] files;
            public MaterialInfo[] materials;
            public LightInfo[] lights;
            public string skyTexture;
            public string sourceBlendSha256;
            public Anchor[] coordinateAnchors;
            public CameraInfo camera;
            public PlayerCameraInfo playerCamera;
        }
        [Serializable] public sealed class ModelFile
        {
            public string category;
            public string assetPath;
            public int meshObjects;
            public long uniqueSourceTriangles;
        }
        [Serializable] public sealed class MaterialInfo
        {
            public string name, baseColorTexture, normalTexture, emissionTexture, roughnessTexture;
            public float[] baseColor, emissionColor;
            public float alpha, roughness, metallic;
            public float normalScale = 1;
        }
        [Serializable] public sealed class LightInfo
        {
            public string name, type;
            public float[] positionBlender, rotationEulerBlender, color;
            public float energy;
            public float range;
            public bool shadow;
            public Vector3 WorldPosition(Matrix4x4 blenderToUnity) => blenderToUnity.MultiplyPoint3x4(VectorOf(positionBlender));
        }
        [Serializable] public sealed class Anchor { public string name; public float[] worldPositionBlender; }
        [Serializable] public sealed class CameraInfo
        {
            public float[] positionBlender, rotationQuaternionBlenderWXYZ;
            public float lens, sensorWidth, sensorHeight, orthoScale, renderAspect;
            public bool orthographic;
        }
        [Serializable] public sealed class PlayerCameraInfo
        { public float[] positionBlender, targetBlender; public float lens, sensorWidth, renderAspect; }
        [Serializable] public sealed class ModelResult
        {
            public string assetPath;
            public int renderers, uniqueMeshes, meshesWithReduction, emptyMeshRenderers, meshesWithoutExtraLods;
            public long sourceUniqueTriangles;
            public long[] uniqueTrianglesByLod, instanceTrianglesByLod;
            public bool lod0CountMatchesSource, automaticSelectionEligible;
            public bool lod0MatchesAuditedImportBaseline;
            public long auditedCoincidentTrianglesRemoved;
        }
        [Serializable] sealed class PackedMaterials { public string sourceBlendSha256; public PackedMaterial[] materials; }
        [Serializable] sealed class PackedMaterial { public string name, packedPath; }
        [Serializable] sealed class CleanupAudit
        {
            public string sourceBlendSha256, assetPath;
            public bool allMeshesAccountedFor, allRowsMatchExpectedCleanup;
            public CleanupMesh[] meshes;
        }
        [Serializable] sealed class CleanupMesh
        {
            public long unityLocalFileId, rawTriangles, coincidentVertexTriangles, importedTriangles, expectedTriangles;
            public string unityMeshName;
        }
        [Serializable] public sealed class Report
        {
            public string unityVersion, sourceBlendSha256, scenePath, timestampUtc;
            public string note = "Separate review scene; build settings and game scene unchanged. LOD index totals are not camera-visible triangle or FPS measurements. Standard maps preserve per-pixel metallic/roughness with audited source factors and normal strength; Blender procedural shaders and volume fog remain approximations. Raw LOD0 mismatch is retained alongside the separately audited zero-area import-cleanup baseline.";
            public List<ModelResult> models = new List<ModelResult>();
            public List<string> unmatchedMaterials = new List<string>();
            public List<string> emptyMaterialSlotExemptions = new List<string>();
            public int materialCount, pointLights;
            public string coordinateMapping;
            public float coordinateError;
            public float meshLodThreshold;
            public string lodSelection = "Unity native Mesh LOD: projected screen size and geometric error, not fixed meter thresholds. Full scene counts below are mesh index totals, not triangles visible to the review camera.";
            public string error;
            public bool savedSceneReopened;
            public int persistedMappedMaterialSlots;
            public int expectedMappedMaterialSlots;
            public bool savedRendererSettingsIntact, persistedSky;
            public bool sourceCameraOrthographic;
            public float distanceFogDensity;
            public int standardPackedMaterialCount;
            public bool passed;
        }

        // Blender exports the point separately from the fixture mesh, whose first part is named __0.
        internal static Transform PlaceExportedLight(Light light, string sourceName, Vector3 sourcePosition, IEnumerable<Renderer> surfaces)
        {
            if (sourceName.StartsWith("WarmPool__", StringComparison.Ordinal) ||
                sourceName.StartsWith("WindowSpill__", StringComparison.Ordinal))
            {
                string fixture = sourceName.Substring(sourceName.IndexOf("__", StringComparison.Ordinal) + 2);
                bool IsFixture(Transform candidate) => candidate.name == fixture || candidate.name == fixture + "__0";
                for (var parent = light.transform.parent; parent; parent = parent.parent)
                    if (IsFixture(parent)) return parent; // Preserve the authored wick offset on a look refresh.

                var owner = surfaces.Where(x => x is MeshRenderer && IsFixture(x.transform))
                    .OrderBy(x => x.bounds.SqrDistance(sourcePosition)).FirstOrDefault();
                if (owner)
                {
                    // Reconstruct the wick in the imported mesh's coordinate system. A loose
                    // light can still be at its export coordinates after the fixture has moved.
                    var filter = owner.GetComponent<MeshFilter>();
                    var model = filter && filter.sharedMesh ? AssetDatabase.LoadAssetAtPath<GameObject>(
                        AssetDatabase.GetAssetPath(filter.sharedMesh)) : null;
                    var source = model ? model.GetComponentsInChildren<MeshFilter>(true)
                        .FirstOrDefault(x => x.sharedMesh == filter.sharedMesh && IsFixture(x.transform)) : null;
                    bool atExportPosition = (light.transform.position - sourcePosition).sqrMagnitude < .000001f;
                    light.transform.SetParent(owner.transform, true);
                    if (source && atExportPosition) light.transform.localPosition = source.transform.InverseTransformPoint(sourcePosition);
                    return owner.transform;
                }
            }
            return null;
        }

        [MenuItem("Shadow Lab/Chapter01/Build separate full-scene LOD review")]
        public static void Build()
        {
            var export = JsonUtility.FromJson<Export>(File.ReadAllText(Path.Combine(Application.dataPath, "Chapter01/chapter01-export.json")));
            if (export.files == null || export.files.Length != 5) throw new InvalidOperationException("Wait for all five scene FBX exports.");
            Directory.CreateDirectory(Output);
            EnsureFolder(Root + "Materials");
            EnsureFolder(Root + "Scenes");
            var report = new Report { unityVersion = Application.unityVersion, sourceBlendSha256 = export.sourceBlendSha256,
                scenePath = ScenePath, timestampUtc = DateTime.UtcNow.ToString("o"), meshLodThreshold = QualitySettings.meshLodThreshold };
            var packed = JsonUtility.FromJson<PackedMaterials>(File.ReadAllText(Path.Combine(Application.dataPath, "Chapter01/chapter01-standard-materials.json")));
            if (packed.sourceBlendSha256 != export.sourceBlendSha256) throw new InvalidOperationException("Packed materials are from a different source scene.");
            var materials = RestoreMaterials(export.materials, packed.materials.ToDictionary(x => x.name, x => x.packedPath));
            report.materialCount = materials.Count;
            report.standardPackedMaterialCount = packed.materials.Length;
            var previous = SceneManager.GetActiveScene();
            bool replaceBatchStartup = Application.isBatchMode && string.IsNullOrEmpty(previous.path);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                replaceBatchStartup ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var roots = new List<GameObject>();
                Bounds? castleBounds = null;
                foreach (var file in export.files)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(file.assetPath);
                    if (model == null) throw new InvalidOperationException("Missing imported model: " + file.assetPath);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                    roots.Add(instance);
                    var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (var renderer in renderers)
                    {
                        bool hasGeometry = HasGeometry(renderer);
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(material => {
                            if (material == null)
                            {
                                if (!hasGeometry) report.emptyMaterialSlotExemptions.Add(renderer.name + ": null material, zero triangles");
                                else report.unmatchedMaterials.Add(renderer.name + ": null material on nonempty mesh");
                                return null;
                            }
                            string name = material.name.Replace(" (Instance)", "");
                            if (materials.TryGetValue(name, out var replacement)) return replacement;
                            if (!hasGeometry)
                            {
                                report.emptyMaterialSlotExemptions.Add(renderer.name + ": " + name + ", zero triangles");
                                return material;
                            }
                            if (!report.unmatchedMaterials.Contains(name)) report.unmatchedMaterials.Add(name);
                            return material;
                        }).ToArray();
                        var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                        GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.gameObject);
                    }
                    var meshInstances = instance.GetComponentsInChildren<MeshFilter>(true).Select(x => x.sharedMesh).Where(x => x != null).ToArray();
                    var uniqueMeshes = meshInstances.Distinct().ToArray();
                    int levels = Math.Max(1, uniqueMeshes.Max(x => x.lodCount));
                    var result = new ModelResult {
                        assetPath = file.assetPath, renderers = renderers.Length, uniqueMeshes = uniqueMeshes.Length,
                        meshesWithReduction = uniqueMeshes.Count(x => x.lodCount > 1),
                        emptyMeshRenderers = renderers.Count(x => !HasGeometry(x)),
                        meshesWithoutExtraLods = uniqueMeshes.Count(x => x.lodCount <= 1),
                        sourceUniqueTriangles = file.uniqueSourceTriangles,
                        uniqueTrianglesByLod = Count(uniqueMeshes, levels), instanceTrianglesByLod = Count(meshInstances, levels),
                        automaticSelectionEligible = renderers.All(x => x.forceMeshLod == -1 &&
                            (GameObjectUtility.GetStaticEditorFlags(x.gameObject) & StaticEditorFlags.BatchingStatic) == 0 &&
                            x.sharedMaterials.All(m => m == null || !m.enableInstancing))
                    };
                    result.lod0CountMatchesSource = result.uniqueTrianglesByLod[0] == file.uniqueSourceTriangles;
                    result.lod0MatchesAuditedImportBaseline = result.lod0CountMatchesSource ||
                        MatchesAuditedCleanup(file, uniqueMeshes, export.sourceBlendSha256, out result.auditedCoincidentTrianglesRemoved);
                    report.models.Add(result);
                    if (file.category == "Architecture")
                    {
                        var bounds = renderers[0].bounds;
                        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                        castleBounds = bounds;
                    }
                }
                var allRenderers = roots.SelectMany(x => x.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
                report.expectedMappedMaterialSlots = allRenderers.Sum(x => x.sharedMaterials.Count(m => m != null && AssetDatabase.GetAssetPath(m).StartsWith(Root + "Materials/", StringComparison.Ordinal)));
                var allBounds = allRenderers[0].bounds;
                foreach (var renderer in allRenderers) allBounds.Encapsulate(renderer.bounds);
                var focus = castleBounds ?? allBounds;
                ConfigureNight(export.skyTexture);
                report.distanceFogDensity = RenderSettings.fogDensity;
                var moon = new GameObject("Review Moon", typeof(Light)).GetComponent<Light>();
                moon.type = LightType.Directional;
                moon.color = new Color(0.52f, 0.66f, 1);
                moon.intensity = 0.55f;
                moon.shadows = LightShadows.Soft;
                moon.transform.rotation = Quaternion.Euler(42, -30, 0);
                var camera = new GameObject("Chapter01 Review Camera", typeof(Camera)).GetComponent<Camera>();
                camera.tag = "MainCamera";
                camera.fieldOfView = 50;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = Math.Max(1500, allBounds.size.magnitude * 3);
                camera.transform.position = focus.center + new Vector3(0.6f, 0.45f, -1.2f) * focus.extents.magnitude;
                camera.transform.LookAt(focus.center);
                camera.allowHDR = true;
                camera.allowMSAA = true;
                camera.backgroundColor = new Color(0.025f, 0.04f, 0.075f);
                camera.clearFlags = CameraClearFlags.Skybox;
                var map = FindCoordinateMap(export.coordinateAnchors, roots, out var mapping, out var error);
                report.coordinateMapping = mapping;
                report.coordinateError = error;
                if (export.camera != null && export.camera.rotationQuaternionBlenderWXYZ != null)
                {
                    camera.transform.position = map.MultiplyPoint3x4(VectorOf(export.camera.positionBlender));
                    var q = export.camera.rotationQuaternionBlenderWXYZ;
                    var rotation = new Quaternion(q[1], q[2], q[3], q[0]);
                    camera.transform.rotation = Quaternion.LookRotation(map.MultiplyVector(rotation * Vector3.back), map.MultiplyVector(rotation * Vector3.up));
                    float horizontal = 2 * Mathf.Atan(export.camera.sensorWidth / (2 * export.camera.lens)) * Mathf.Rad2Deg;
                    float aspect = export.camera.renderAspect > 0 ? export.camera.renderAspect : 1280f / 720f;
                    camera.aspect = aspect;
                    camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(horizontal, aspect);
                    camera.orthographic = export.camera.orthographic;
                    if (camera.orthographic)
                        camera.orthographicSize = export.camera.orthoScale / aspect / 2 * map.MultiplyVector(Vector3.right).magnitude;
                    report.sourceCameraOrthographic = camera.orthographic;
                }
                // A disabled perspective camera supports player-view LOD inspection without replacing the authored composition.
                var inspection = new GameObject("Perspective LOD inspection (enable to inspect distance)", typeof(Camera)).GetComponent<Camera>();
                inspection.CopyFrom(camera);
                inspection.orthographic = false;
                inspection.fieldOfView = 50;
                inspection.enabled = false;
                inspection.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                if (export.playerCamera != null)
                {
                    inspection.transform.position = map.MultiplyPoint3x4(VectorOf(export.playerCamera.positionBlender));
                    inspection.transform.LookAt(map.MultiplyPoint3x4(VectorOf(export.playerCamera.targetBlender)));
                    inspection.aspect = export.playerCamera.renderAspect > 0 ? export.playerCamera.renderAspect : 1.44f;
                    float horizontal = 2 * Mathf.Atan(export.playerCamera.sensorWidth / (2 * export.playerCamera.lens)) * Mathf.Rad2Deg;
                    inspection.fieldOfView = Camera.HorizontalToVerticalFieldOfView(horizontal, inspection.aspect);
                }
                // Emissive surfaces retain all visible practical lights; a bounded set supplies nearby illumination.
                var candidates = (export.lights ?? Array.Empty<LightInfo>()).Where(x => x.type == "POINT").ToArray();
                var nearbyCastle = candidates.OrderBy(x => (map.MultiplyPoint3x4(VectorOf(x.positionBlender)) - focus.center).sqrMagnitude).Take(16);
                var nearbyPlayer = candidates.OrderBy(x => (map.MultiplyPoint3x4(VectorOf(x.positionBlender)) - inspection.transform.position).sqrMagnitude).Take(8);
                foreach (var source in nearbyCastle.Concat(nearbyPlayer).Distinct())
                {
                    var light = new GameObject(source.name + " (Review)", typeof(Light)).GetComponent<Light>();
                    light.transform.position = source.WorldPosition(map);
                    PlaceExportedLight(light, source.name, light.transform.position, allRenderers);
                    light.type = LightType.Point;
                    light.color = ColorOf(source.color, new Color(1, 0.6f, 0.25f));
                    light.intensity = Mathf.Clamp(source.energy / 100f, 0.15f, 3f);
                    light.range = source.range > 0 ? source.range : 10;
                    light.shadows = LightShadows.None;
                    ++report.pointLights;
                }
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save review scene.");
                Capture(camera, Path.Combine(Output, "chapter01-unity-review.png"));
                Capture(inspection, Path.Combine(Output, "chapter01-unity-player.png"));
                report.passed = report.models.All(x => x.meshesWithReduction > 0 && x.automaticSelectionEligible && x.lod0MatchesAuditedImportBaseline)
                    && report.unmatchedMaterials.Count == 0;
                if (!report.passed) throw new InvalidOperationException("Full-scene Mesh LOD eligibility failed; inspect report.");
            }
            catch (Exception exception) { report.error = exception.ToString(); throw; }
            finally
            {
                if (!replaceBatchStartup)
                {
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
                File.WriteAllText(Path.Combine(Output, "chapter01-full-scene-lod.json"), JsonUtility.ToJson(report, true));
            }
            if (replaceBatchStartup) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var reloaded = EditorSceneManager.OpenScene(ScenePath,
                replaceBatchStartup ? OpenSceneMode.Single : OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(reloaded);
                var renderers = reloaded.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
                report.persistedMappedMaterialSlots = renderers.Sum(x => x.sharedMaterials.Count(m => m != null && AssetDatabase.GetAssetPath(m).StartsWith(Root + "Materials/", StringComparison.Ordinal)));
                report.savedRendererSettingsIntact = renderers.All(renderer => {
                    if (!HasGeometry(renderer)) return true;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null || !Chapter01LodImportPolicy.TryGetProfile(AssetDatabase.GetAssetPath(filter.sharedMesh), out var profile)) return false;
                    return renderer.forceMeshLod == -1 && renderer.meshLodSelectionBias == Chapter01LodImportPolicy.GetSelectionBias(profile, renderer.name) &&
                        (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.BatchingStatic) == 0 &&
                        renderer.sharedMaterials.All(m => m == null || !m.enableInstancing);
                });
                report.persistedSky = RenderSettings.skybox != null && RenderSettings.skybox.GetTexture("_MainTex") != null;
                report.savedSceneReopened = renderers.Length == report.models.Sum(x => x.renderers) &&
                    report.persistedMappedMaterialSlots == report.expectedMappedMaterialSlots && report.expectedMappedMaterialSlots > 0 &&
                    report.savedRendererSettingsIntact && report.persistedSky;
                report.passed &= report.savedSceneReopened;
            }
            finally
            {
                if (!replaceBatchStartup)
                {
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(reloaded, true);
                }
            }
            File.WriteAllText(Path.Combine(Output, "chapter01-full-scene-lod.json"), JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Saved review scene did not preserve geometry or material assignments.");
            Debug.Log("CHAPTER01_FULL_SCENE_LOD_PASS " + ScenePath);
        }

        static long[] Count(IEnumerable<Mesh> meshes, int levels)
        {
            var counts = new long[levels];
            foreach (var mesh in meshes)
                for (int level = 0; level < levels; ++level)
                    for (int sub = 0; sub < mesh.subMeshCount; ++sub)
                        counts[level] += mesh.lodCount > 0
                            ? mesh.GetLod(sub, Math.Min(level, mesh.lodCount - 1)).indexCount / 3
                            : (long)mesh.GetIndexCount(sub) / 3;
            return counts;
        }

        public static void ReimportInfrastructureAndBuild()
        {
            const string path = Root + "Models/Infrastructure/BridgeStairsTerrain.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing infrastructure importer.");
            if (importer.maximumMeshLod != 4)
            {
                importer.maximumMeshLod = 4;
                importer.SaveAndReimport();
            }
            Build();
        }

        static bool MatchesAuditedCleanup(ModelFile file, Mesh[] meshes, string sourceHash, out long removed)
        {
            removed = 0;
            string path = Path.Combine(Output, "audited-import-cleanup.json");
            if (!File.Exists(path)) return false;
            var audit = JsonUtility.FromJson<CleanupAudit>(File.ReadAllText(path));
            if (audit.sourceBlendSha256 != sourceHash || audit.assetPath != file.assetPath ||
                !audit.allMeshesAccountedFor || !audit.allRowsMatchExpectedCleanup || audit.meshes.Length != meshes.Length ||
                audit.meshes.Sum(x => x.rawTriangles) != file.uniqueSourceTriangles) return false;
            var meshById = new Dictionary<long, Mesh>();
            foreach (var mesh in meshes)
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long id)) return false;
                meshById.Add(id, mesh);
            }
            foreach (var row in audit.meshes)
            {
                if (!meshById.TryGetValue(row.unityLocalFileId, out var mesh) || mesh.name != row.unityMeshName ||
                    row.rawTriangles - row.coincidentVertexTriangles != row.expectedTriangles ||
                    row.importedTriangles != row.expectedTriangles || Count(new[] { mesh }, 1)[0] != row.importedTriangles) return false;
                removed += row.coincidentVertexTriangles;
            }
            return true;
        }

        static Dictionary<string, Material> RestoreMaterials(MaterialInfo[] descriptions, Dictionary<string, string> packedMaps)
        {
            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var info in descriptions)
            {
                if (!string.IsNullOrEmpty(info.normalTexture))
                {
                    var importer = AssetImporter.GetAtPath(info.normalTexture) as TextureImporter;
                    if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                    { importer.textureType = TextureImporterType.NormalMap; importer.SaveAndReimport(); }
                }
                string safeName = string.Concat(info.name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
                string path = Root + "Materials/" + safeName + "_" + Hash128.Compute(info.name).ToString().Substring(0, 8) + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
                material.name = info.name;
                material.enableInstancing = false;
                material.color = ColorOf(info.baseColor, Color.white);
                var color = material.color; color.a = info.alpha; material.color = color;
                material.mainTexture = LoadTexture(info.baseColorTexture);
                material.SetFloat("_Glossiness", 1 - info.roughness);
                material.SetFloat("_Metallic", info.metallic);
                if (packedMaps.TryGetValue(info.name, out var packedPath))
                {
                    var dataImporter = AssetImporter.GetAtPath(packedPath) as TextureImporter;
                    if (dataImporter == null) throw new InvalidOperationException("Packed material texture missing: " + packedPath);
                    if (dataImporter.sRGBTexture || dataImporter.alphaIsTransparency)
                    { dataImporter.sRGBTexture = false; dataImporter.alphaIsTransparency = false; dataImporter.SaveAndReimport(); }
                    var texture = LoadTexture(packedPath);
                    material.SetTexture("_MetallicGlossMap", texture);
                    material.EnableKeyword("_METALLICGLOSSMAP");
                    material.SetFloat("_GlossMapScale", 1);
                    material.SetFloat("_SmoothnessTextureChannel", 0);
                    material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                    // glTF metallic/roughness R is not necessarily authored AO.
                    material.SetTexture("_OcclusionMap", null);
                    material.SetFloat("_OcclusionStrength", 0);
                }
                else
                {
                    material.SetTexture("_MetallicGlossMap", null);
                    material.DisableKeyword("_METALLICGLOSSMAP");
                    material.SetTexture("_OcclusionMap", null);
                    material.SetFloat("_OcclusionStrength", 0);
                }
                material.SetTexture("_BumpMap", LoadTexture(info.normalTexture));
                material.SetFloat("_BumpScale", info.normalScale);
                if (!string.IsNullOrEmpty(info.normalTexture)) material.EnableKeyword("_NORMALMAP");
                else material.DisableKeyword("_NORMALMAP");
                material.SetTexture("_EmissionMap", LoadTexture(info.emissionTexture));
                var emission = ColorOf(info.emissionColor, Color.black);
                material.SetColor("_EmissionColor", emission);
                if (emission.maxColorComponent > 0 || !string.IsNullOrEmpty(info.emissionTexture)) material.EnableKeyword("_EMISSION");
                else material.DisableKeyword("_EMISSION");
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                bool transparent = info.alpha < 0.999f;
                material.SetFloat("_Mode", transparent ? 2 : 0);
                material.SetInt("_SrcBlend", (int)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
                material.SetInt("_DstBlend", (int)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
                material.SetInt("_ZWrite", transparent ? 0 : 1);
                if (transparent) material.EnableKeyword("_ALPHABLEND_ON"); else material.DisableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
                material.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;
                EditorUtility.SetDirty(material);
                result[info.name] = material;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static Texture LoadTexture(string path) => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        static bool HasGeometry(MeshRenderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;
            var mesh = filter.sharedMesh;
            for (int sub = 0; sub < mesh.subMeshCount; ++sub) if (mesh.GetIndexCount(sub) > 0) return true;
            return false;
        }
        static Vector3 VectorOf(float[] values) => new Vector3(values[0], values[1], values[2]);

        static Matrix4x4 FindCoordinateMap(Anchor[] anchors, List<GameObject> roots, out string description, out float rms)
        {
            var transforms = roots.SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
            var pairs = new List<(Vector3 source, Vector3 target)>();
            foreach (var anchor in anchors ?? Array.Empty<Anchor>())
            {
                var match = transforms.FirstOrDefault(x => x.name == anchor.name);
                if (match != null) pairs.Add((VectorOf(anchor.worldPositionBlender), match.position));
            }
            if (pairs.Count < 3) throw new InvalidOperationException("At least three exported coordinate anchors must match FBX objects.");
            var permutations = new[] { new[] {0,1,2}, new[] {0,2,1}, new[] {1,0,2}, new[] {1,2,0}, new[] {2,0,1}, new[] {2,1,0} };
            float best = float.PositiveInfinity;
            var result = Matrix4x4.identity;
            description = "";
            foreach (var permutation in permutations)
                for (int signs = 0; signs < 8; ++signs)
                {
                    var candidate = Matrix4x4.zero; candidate[3, 3] = 1;
                    for (int axis = 0; axis < 3; ++axis) candidate[axis, permutation[axis]] = (signs & (1 << axis)) == 0 ? 1 : -1;
                    float numerator = 0, denominator = 0;
                    foreach (var pair in pairs)
                    { var mapped = candidate.MultiplyVector(pair.source); numerator += Vector3.Dot(mapped, pair.target); denominator += mapped.sqrMagnitude; }
                    float scale = numerator / Math.Max(0.0001f, denominator);
                    if (scale <= 0) continue;
                    float error = pairs.Sum(pair => (candidate.MultiplyVector(pair.source) * scale - pair.target).sqrMagnitude);
                    if (error >= best) continue;
                    best = error;
                    result = candidate;
                    for (int row = 0; row < 3; ++row) for (int col = 0; col < 3; ++col) result[row, col] *= scale;
                    description = "axes=" + string.Join(",", permutation) + "; signs=" + signs + "; scale=" + scale;
                }
            rms = Mathf.Sqrt(best / pairs.Count);
            if (rms > 0.1f) throw new InvalidOperationException("FBX coordinate reference mismatch, RMS=" + rms);
            return result;
        }
        static Color ColorOf(float[] values, Color fallback) => values != null && values.Length >= 3
            ? new Color(values[0], values[1], values[2], values.Length > 3 ? values[3] : 1) : fallback;
        static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; ++i)
            { if (!AssetDatabase.IsValidFolder(current + "/" + parts[i])) AssetDatabase.CreateFolder(current, parts[i]); current += "/" + parts[i]; }
        }
        static void ConfigureNight(string skyPath)
        {
            var path = Root + "Materials/Chapter01_Review_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(sky, path); }
            sky.SetTexture("_MainTex", LoadTexture(skyPath));
            sky.SetFloat("_Exposure", 0.35f);
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssetIfDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.055f, 0.075f, 0.13f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.065f, 0.095f, 0.14f);
            RenderSettings.fogDensity = 0.0008f;
        }
        static void Capture(Camera camera, string path)
        {
            int width = 1280;
            int height = Mathf.RoundToInt(width / camera.aspect);
            var target = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var scene = camera.gameObject.scene;
            ulong previousSceneMask = EditorSceneManager.GetSceneCullingMask(scene);
            ulong previousCameraMask = camera.overrideSceneCullingMask;
            try
            {
                ulong captureMask = EditorSceneManager.CalculateAvailableSceneCullingMask();
                EditorSceneManager.SetSceneCullingMask(scene, captureMask);
                camera.overrideSceneCullingMask = captureMask;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                camera.overrideSceneCullingMask = previousCameraMask;
                EditorSceneManager.SetSceneCullingMask(scene, previousSceneMask);
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
