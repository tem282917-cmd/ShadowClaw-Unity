using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

// Strip the settlement on the build copy, before static batching combines its meshes.
internal sealed class Chapter01VillageBuildFilter : IProcessSceneWithReport
{
    public int callbackOrder => -10000;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report == null || scene.name != "Chapter01_Main") return;
        int removed = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "VillageBackdrop")
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (!child.name.Contains("__Village_") && !child.name.StartsWith("Village_", StringComparison.Ordinal)) continue;
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                    removed++;
                }
            }
            else if (root.name.StartsWith("WarmPool__D01__Village_Lantern_", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(root);
        }
        Debug.Log($"Chapter01 build removed {removed} distant village objects.");
    }
}
