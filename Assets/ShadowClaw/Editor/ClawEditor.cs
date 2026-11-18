using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ClawEditor
{
    const string HallScene="Assets/IronCrownHall/Scenes/IronCrownHall.unity";

    [MenuItem("Shadow Lab/Run checks")]
    public static void Check() { foreach(var row in ClawChecks.Run()) Debug.Log(row); }
    public static void BuildWindows() {
        Check();
        var result=BuildPipeline.BuildPlayer(new[] { HallScene },
            "../Builds/Windows-BellSleeve-IronHall/ShadowClaw.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Windows build failed: "+result.summary.result);
    }
    [MenuItem("Shadow Lab/Play Iron Crown Hall")]
    public static void Play() {
        if(EditorApplication.isPlaying) return;
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(HallScene);
        EditorApplication.isPlaying=true;
    }

    [MenuItem("Shadow Lab/Play Main Scene - Chapter01")]
    public static void PlayMain() {
        if(EditorApplication.isPlaying) return;
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ShiyinBuild.MainScene);
        EditorApplication.isPlaying=true;
    }
}
