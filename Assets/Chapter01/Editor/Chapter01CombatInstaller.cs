using System;
using System.IO;
using System.Linq;
using Chapter01.Rendering;
using Chapter01.Runtime.Testing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    // One-time authoring migration. Playing/restarting never rebuilds the user's layout.
    public static class Chapter01CombatInstaller
    {
        public const string ScenePath = "Assets/Chapter01/Scenes/Chapter01_Main.unity";
        const string SourceScene = "Assets/Chapter01/Scenes/Chapter01_CastleNight.unity";
        static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/Chapter01-Combat-Verification"));

        [MenuItem("Shadow Lab/Chapter01/Connect combat and make main scene")]
        public static void Install()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Evidence);
            string backup = Path.Combine(Evidence, "Chapter01_CastleNight.before-combat.unity");
            if (!File.Exists(backup)) File.Copy(SourceScene, backup);
            if (!File.Exists(ScenePath)) AssetDatabase.CopyAsset(SourceScene, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var existing = All<ShadowLab>(scene).SingleOrDefault();
            if (existing)
            {
                ValidateScene(scene);
                SetMainScene();
                Debug.Log("CHAPTER01_COMBAT_ALREADY_INSTALLED: authored layout retained");
                return;
            }
            var rig = All<Chapter01PlayerRoute>(scene).Single();
            var camera = rig.ViewCamera;
            if (!camera || !camera.GetComponent<Chapter01NightPostProcess>())
                throw new InvalidOperationException("Expected the existing CastleNight gameplay camera and night atmosphere.");
            var budget = rig.GetComponent<Chapter01SceneRenderBudget>();
            var presentation = rig.gameObject;
            var routeController = rig.GetComponent<CharacterController>();
            UnityEngine.Object.DestroyImmediate(rig);
            UnityEngine.Object.DestroyImmediate(routeController);
            presentation.name = "Chapter01_Presentation";
            camera.name = "Hunter eyes";
            camera.tag = "MainCamera";
            camera.fieldOfView = 72;
            camera.nearClipPlane = .04f;
            camera.clearStencilAfterLightingPass = true;
            foreach (var other in All<Camera>(scene)) other.enabled = other == camera;
            foreach (var listener in All<AudioListener>(scene)) listener.enabled = listener.gameObject == camera.gameObject;
            if (!camera.GetComponent<AudioListener>()) camera.gameObject.AddComponent<AudioListener>();

            var root = new GameObject("Chapter01_Combat");
            SceneManager.MoveGameObjectToScene(root, scene);
            var hall = root.AddComponent<IronHallEncounter>();
            hall.UseAuthoredLayout = true;
            hall.GameplayCamera = camera;
            hall.Moonlight = All<Light>(scene).Single(x => x.name == "Review Moon");
            hall.RaisedSurfaces = Array.Empty<BoxCollider>();
            hall.GroundColliders = All<Collider>(scene).Where(x => x.name.StartsWith("Collision__", StringComparison.Ordinal)).ToArray();
            var bounds = Marker(root.transform, "BattleArea - resize to adjust combat bounds", new Vector3(0, 70, 117));
            hall.BattleArea = bounds.gameObject.AddComponent<BoxCollider>();
            hall.BattleArea.size = new Vector3(32, 4, 54);
            hall.BattleArea.isTrigger = true;
            bounds.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            hall.PlayerSpawn = Marker(root.transform, "PlayerSpawn", new Vector3(-.45f, 68, 120.55f));
            hall.DemonSpawn = Marker(root.transform, "DemonSpawn", new Vector3(.36f, 68, 118.34f));
            hall.MenuView = Marker(root.transform, "MenuView", new Vector3(-7, 71, 137));
            hall.MenuView.LookAt(new Vector3(0, 70, 110));
            Chapter01MountainStart.SetSpawn(hall, Chapter01RuntimeValidation.ReadRoute().points);

            // Source FBX roots are unpacked once so scene objects and their collision can be edited together.
            foreach (var source in scene.GetRootGameObjects().Where(PrefabUtility.IsAnyPrefabInstanceRoot).ToArray())
                PrefabUtility.UnpackPrefabInstance(source, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var surfaces = All<MeshRenderer>(scene);
            var export = JsonUtility.FromJson<Chapter01ReviewScene.Export>(File.ReadAllText(
                Path.Combine(Application.dataPath, "Chapter01/chapter01-export.json")));
            var map = Chapter01CastleNightBuilder.BlenderToUnity();
            var exportedPoints = export.lights.Where(l => l.type == "POINT").ToDictionary(l => l.name);
            foreach (var point in All<Light>(scene).Where(l => l.name.StartsWith("WarmPool__", StringComparison.Ordinal) ||
                                                              l.name.StartsWith("WindowSpill__", StringComparison.Ordinal)))
            {
                // Older CastleNight source scenes stored these lights at the scene root.
                if (!exportedPoints.TryGetValue(point.name, out var source)) continue;
                var fixture = Chapter01ReviewScene.PlaceExportedLight(point, point.name, source.WorldPosition(map), surfaces);
                if (!fixture || !point.name.StartsWith("WarmPool__D03__Chapel_Candle_", StringComparison.Ordinal)) continue;
                string flameName = "Candle_Flame__" + point.name.Substring("WarmPool__".Length);
                var flame = surfaces.FirstOrDefault(r => r.name == flameName);
                if (flame && !flame.transform.IsChildOf(fixture))
                {
                    flame.transform.SetParent(fixture, true);
                    flame.transform.position += point.transform.position - flame.bounds.center;
                }
            }
            var lanterns = surfaces.Where(x => x.name.StartsWith("D02__Six_Combat_Lantern_", StringComparison.Ordinal))
                .OrderBy(x => x.bounds.center.x < 0 ? 0 : 1).ThenBy(x => x.bounds.center.z).ToArray();
            if (lanterns.Length != HuntBoss.LampCapacity) throw new InvalidOperationException("Expected the six authored D02 combat lantern models.");
            hall.Lanterns = lanterns;
            hall.GalleryLights = new Light[HuntBoss.LampCapacity];
            hall.WickAnchors = new Transform[HuntBoss.LampCapacity];
            for (int i = 0; i < lanterns.Length; i++)
            {
                var light = lanterns[i].GetComponentsInChildren<Light>(true)
                    .Single(l => l.name.StartsWith("WarmPool__D02__Six_Combat_Lantern_", StringComparison.Ordinal));
                hall.GalleryLights[i] = light;
                hall.WickAnchors[i] = Marker(lanterns[i].transform, "Combat Wick", light.transform.position);
            }
            foreach (var lightBudget in All<Chapter01PracticalLightBudget>(scene))
            {
                lightBudget.viewer = camera.transform;
                lightBudget.lights = lightBudget.lights.Except(hall.GalleryLights).ToArray();
                EditorUtility.SetDirty(lightBudget);
            }
            var combat = root.AddComponent<ShadowLab>();
            combat.EncounterDisplayName = "古堡";
            combat.EncounterDisplayNameEnglish = "SHADOWS OF THE CASTLE";
            if (budget) { budget.cameras = new[] { camera }; budget.ConfigureCameras(); }

            Chapter01PrefabLibrary.Generate(scene);
            EditorUtility.SetDirty(hall);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save CastleNight combat scene.");
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ValidateScene(scene);
            SetMainScene();
            File.WriteAllText(Path.Combine(Evidence, "installation.txt"), "CHAPTER01_COMBAT_INSTALL_PASS\nScene: " + ScenePath + "\n" +
                "Six original D02 lamps; authored spawn/menu/bounds; night camera retained; route controller removed; layout saved and reopened.\n");
            Debug.Log("CHAPTER01_COMBAT_INSTALL_PASS");
        }

        public static void InstallAndBuild() { Install(); ShiyinBuild.BuildWindows(); }

        public static void ValidateScene(Scene scene)
        {
            var lab = All<ShadowLab>(scene).Single();
            var hall = lab.GetComponent<IronHallEncounter>();
            if (!hall || !hall.UseAuthoredLayout || !hall.GameplayCamera || !hall.BattleArea || !hall.PlayerSpawn || !hall.DemonSpawn || !hall.MenuView)
                throw new InvalidOperationException("Missing authored encounter references.");
            if (All<Chapter01PlayerRoute>(scene).Length != 0 || All<Camera>(scene).Count(x => x.enabled && x.gameObject.activeInHierarchy) != 1)
                throw new InvalidOperationException("Duplicate player input or active camera.");
            if (hall.WickAnchors.Length != 6 || hall.GalleryLights.Length != 6 || hall.Lanterns.Length != 6 ||
                hall.WickAnchors.Any(x => !x) || hall.GalleryLights.Any(x => !x) || hall.Lanterns.Any(x => !x))
                throw new InvalidOperationException("Invalid authored six-light references.");
            if (hall.GroundColliders.Length == 0 || hall.GroundColliders.Any(x => !x))
                throw new InvalidOperationException("Missing scene ground collision references.");
            var nave = hall.GroundColliders.Single(x => x.name == "Collision__Citadel_Nave_Floor");
            if (!nave.transform.parent || nave.transform.parent.name != "Citadel_Nave_Floor")
                throw new InvalidOperationException("Authored floor collision must move with its visible model.");
            var floorModel = nave.transform.parent;
            var savedPosition = floorModel.position;
            var originalCenter = nave.bounds.center;
            var translation = new Vector3(1.25f, 0, -.75f);
            try
            {
                floorModel.position += translation;
                Physics.SyncTransforms();
                if (Vector3.Distance(nave.bounds.center, originalCenter + translation) > .002f)
                    throw new InvalidOperationException("Moving a scene model did not carry its collision.");
            }
            finally { floorModel.position = savedPosition; Physics.SyncTransforms(); }
            if (All<Chapter01PracticalLightBudget>(scene).Any(x => x.lights.Intersect(hall.GalleryLights).Any()))
                throw new InvalidOperationException("Decorative-light budget must not override combat lamps.");
            Physics.SyncTransforms();
            foreach (var spawn in new[] { hall.PlayerSpawn, hall.DemonSpawn })
            {
                var ray = new Ray(spawn.position + Vector3.up, Vector3.down);
                if (!hall.GroundColliders.Any(x => x.Raycast(ray, out var hit, 2) && hit.normal.y > .7f))
                    throw new InvalidOperationException("Spawn is not on an authored walkable surface: " + spawn.name);
            }
            foreach (var row in ClawChecks.Run()) Debug.Log(row);
        }

        static void SetMainScene()
        {
            var others = EditorBuildSettings.scenes.Where(x => x.path != ScenePath)
                .Select(x => new EditorBuildSettingsScene(x.path, false));
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(others).ToArray();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<T>(true)).ToArray();
        static Transform Marker(Transform parent, string name, Vector3 position)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.position = position;
            return marker;
        }
    }
}
