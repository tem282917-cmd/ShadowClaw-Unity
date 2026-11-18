using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    public static class Chapter01MountainStart
    {
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/Chapter01-Approach-Verification"));

        [MenuItem("Shadow Lab/Chapter01/Set mountain clearing start")]
        public static void Configure()
        {
            var scene = SceneManager.GetSceneByPath(Chapter01CombatInstaller.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(Chapter01CombatInstaller.ScenePath, OpenSceneMode.Additive);
            var hall = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<IronHallEncounter>(true)).Single();
            var route = Chapter01RuntimeValidation.ReadRoute().points;
            Directory.CreateDirectory(Output);
            var backup = Path.Combine(Output, "Chapter01_Main.before-mountain-start.unity");
            if (!File.Exists(backup)) File.Copy(Chapter01CombatInstaller.ScenePath, backup);

            SetSpawn(hall, route);
            Chapter01CombatInstaller.ValidateScene(scene);
            EditorUtility.SetDirty(hall);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save mountain clearing spawn.");
            File.WriteAllText(Path.Combine(Output, "scene-update.txt"), "CHAPTER01_MOUNTAIN_START_PASS\n" +
                "Scene: " + scene.path + "\nPlayerSpawn: " + hall.PlayerSpawn.position + "\nStartWithExploration: true\n");
            Debug.Log("CHAPTER01_MOUNTAIN_START_PASS " + hall.PlayerSpawn.position);
        }

        public static void SetSpawn(IronHallEncounter hall, Vector3[] route)
        {
            hall.StartWithExploration = true;
            // The arrival ridge has an irregular edge behind the path. This open
            // patch stays on its flat top and connects to the first paved waypoint.
            var clearing = route[0] + new Vector3(2, 0, -6);
            var forward = route[0] - clearing; forward.y = 0;
            Physics.SyncTransforms();
            var ray = new Ray(clearing + Vector3.up * 5, Vector3.down);
            var hits = hall.GroundColliders.Where(x => x && x.enabled && !x.isTrigger)
                .Select(x => x.Raycast(ray, out var hit, 10) ? hit : default)
                .Where(x => x.collider && x.normal.y > .7f).OrderBy(x => x.distance).ToArray();
            if (hits.Length == 0) throw new InvalidOperationException("Mountain clearing has no walkable ground.");
            clearing.y = hits[0].point.y;
            hall.PlayerSpawn.position = clearing;
            hall.PlayerSpawn.rotation = Quaternion.LookRotation(forward.normalized);
            hall.GameplayCamera.transform.SetPositionAndRotation(hall.PlayerSpawn.position + Vector3.up * 1.65f,
                Quaternion.Euler(4, hall.PlayerSpawn.eulerAngles.y, 0));
            hall.MenuView.position = clearing + new Vector3(-2, 2.6f, -3);
            hall.MenuView.LookAt(route[0] + new Vector3(3, 7, 42));
            EditorUtility.SetDirty(hall.PlayerSpawn);
            EditorUtility.SetDirty(hall.MenuView);
            EditorUtility.SetDirty(hall.GameplayCamera);
        }

        public static void ConfigureAndBuild() { Configure(); ShiyinBuild.BuildWindows(); }
    }
}
