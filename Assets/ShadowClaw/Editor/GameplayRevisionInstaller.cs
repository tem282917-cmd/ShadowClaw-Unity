using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GameplayRevisionInstaller {
    public static void InstallAndBuildWindows() {
        Apply();
        ShiyinBuild.BuildWindows();
    }
    public static void Apply() {
        string output=Path.GetFullPath("../Builds/Gameplay-Revision-20261118");
        Directory.CreateDirectory(output);
        string backup=Path.Combine(output,"Chapter01_Main.before-hall-presentation.unity");
        if(!File.Exists(backup))File.Copy(ShiyinBuild.MainScene,backup);
        var scene=EditorSceneManager.OpenScene(ShiyinBuild.MainScene);
        HallPresentationInstaller.ApplyToCurrentScene();
        var hall=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<IronHallEncounter>(true)).Single();
        var lab=hall.GetComponent<ShadowLab>();
        hall.FogGate=hall.GetComponentInChildren<BossFogGate>(true);
        hall.FogGate.Encounter=lab;
        lab.DemonShadowScale=2;
        EditorUtility.SetDirty(hall);EditorUtility.SetDirty(lab);
        foreach(var line in HallPresentationInstaller.CheckPrefabs())Debug.Log(line);
        if(!EditorSceneManager.SaveScene(scene))throw new Exception("Could not save the authored scene.");
        AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(output,"installation.txt"),
            "PASS six 4m combat lamps, actual doorway fog gate at (0,68,83.475), classic boss, 2x linear shadow.\n"+
            "Scene: "+scene.path+"\nBackup: "+backup+"\nNo minor enemies or guardian placed.\n");
        Debug.Log("GAMEPLAY_REVISION_INSTALLED");
    }
}
