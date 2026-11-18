using System;
using System.IO;
using System.Linq;
using Chapter01.Runtime.Testing;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    public static class Chapter01RuntimeValidation
    {
        public const string ScenePath = "Assets/Chapter01/Scenes/Chapter01_CastleNight.unity";
        public const string RoutePath = "Assets/Chapter01/Runtime/Testing/castle-route.json";
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Design/Chapter01-Production-v03/unity-castle-v025"));
        [Serializable] public sealed class RouteDocument { public string sourcePath, sourceSha256, coordinateConversion; public Vector3[] points; }
        [Serializable] sealed class BuildEvidence
        {
            public string timestampUtc, scenePath, executable, result, duration, routeSourceSha256;
            public ulong totalBytes;
            public int warnings, errors, sceneBuildSettingsCount, colliders, routePoints;
            public bool buildSettingsPreserved;
            public string description = "Windows x64 Development Player; only CastleNight scene supplied to BuildPipeline.BuildPlayer. No AutoconnectProfiler, DeepProfiling, or script debugging. Development builds enable FrameTimingManager without changing PlayerSettings.";
        }

        public static RouteDocument ReadRoute()
        {
            var doc = JsonUtility.FromJson<RouteDocument>(File.ReadAllText(RoutePath));
            if (doc.points == null || doc.points.Length != 41) throw new InvalidDataException("Expected approved 41-point castle route");
            return doc;
        }

        public static Chapter01PlayerRoute InstallPlayerRig(Scene scene, Camera playerCamera, string collisionRootName = "Chapter01_Collision")
        {
            if (!scene.IsValid() || !scene.isLoaded || !playerCamera || playerCamera.gameObject.scene != scene)
                throw new ArgumentException("InstallPlayerRig requires a loaded scene and a camera in that scene");
            var doc = ReadRoute();
            var old = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "Chapter01_Player");
            if (old && playerCamera.transform.IsChildOf(old.transform)) playerCamera.transform.SetParent(null, true);
            if (old) UnityEngine.Object.DestroyImmediate(old);
            var go = new GameObject("Chapter01_Player");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = doc.points[0] + Vector3.up * .08f;
            Vector3 direction = doc.points[1] - doc.points[0]; direction.y = 0;
            go.transform.rotation = Quaternion.LookRotation(direction.normalized);
            var controller = go.AddComponent<CharacterController>();
            controller.height = 1.8f; controller.radius = .28f; controller.center = new Vector3(0, .9f, 0);
            controller.stepOffset = .4f; controller.slopeLimit = 48; controller.skinWidth = .035f; controller.minMoveDistance = 0;
            var rig = go.AddComponent<Chapter01PlayerRoute>();
            rig.Route = doc.points; rig.RouteSourceSha256 = doc.sourceSha256; rig.CollisionRootName = collisionRootName;
            rig.ViewCamera = playerCamera;
            foreach (var camera in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)))
                camera.enabled = camera == playerCamera;
            playerCamera.gameObject.SetActive(true);
            playerCamera.transform.SetParent(go.transform, false);
            playerCamera.transform.localPosition = Vector3.up * 1.7f;
            playerCamera.transform.localRotation = Quaternion.Euler(2, 0, 0);
            playerCamera.orthographic = false; playerCamera.allowMSAA = true;
            playerCamera.aspect = 1920f / 1080;
            playerCamera.tag = "MainCamera";
            foreach (var listener in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<AudioListener>(true)))
                listener.enabled = listener.gameObject == playerCamera.gameObject;
            if (!playerCamera.GetComponent<AudioListener>()) playerCamera.gameObject.AddComponent<AudioListener>();
            EditorSceneManager.MarkSceneDirty(scene);
            return rig;
        }

        [MenuItem("Chapter01/Build CastleNight Development Player")]
        public static void BuildPlayer()
        {
            Directory.CreateDirectory(Output);
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Build the independent CastleNight scene first", ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var rig = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Chapter01PlayerRoute>(true)).Single();
            if (!rig.ViewCamera || rig.Route.Length != 41) throw new InvalidDataException("Player rig not configured");
            var collisionRoot = scene.GetRootGameObjects().Single(x => x.name == rig.CollisionRootName);
            var colliders = collisionRoot.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0 || colliders.Any(x => x.isTrigger)) throw new InvalidDataException("Missing solid route collision");
            if (scene.isDirty) throw new InvalidOperationException("Saved CastleNight scene must be clean before build");
            var settingsBefore = EditorBuildSettings.scenes.Select(x => x.path + "|" + x.enabled).ToArray();
            string exe = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/Chapter01-CastleNight-v025/Chapter01-CastleNight.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = exe, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            bool preserved = settingsBefore.SequenceEqual(EditorBuildSettings.scenes.Select(x => x.path + "|" + x.enabled));
            var evidence = new BuildEvidence {
                timestampUtc = DateTime.UtcNow.ToString("o"), scenePath = ScenePath, executable = exe,
                result = report.summary.result.ToString(), duration = report.summary.totalTime.ToString(),
                totalBytes = report.summary.totalSize, warnings = (int)report.summary.totalWarnings, errors = (int)report.summary.totalErrors,
                routeSourceSha256 = rig.RouteSourceSha256, routePoints = rig.Route.Length, colliders = colliders.Length,
                sceneBuildSettingsCount = settingsBefore.Length, buildSettingsPreserved = preserved
            };
            File.WriteAllText(Path.Combine(Output, "player-build.json"), JsonUtility.ToJson(evidence, true));
            if (!preserved || report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("CastleNight player build failed; see player-build.json");
            Debug.Log("Chapter01 Player ready: " + exe);
        }
    }
}
