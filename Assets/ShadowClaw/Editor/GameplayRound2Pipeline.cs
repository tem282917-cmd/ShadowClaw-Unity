using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit job invoked through the existing editor bridge. BuildPipeline can
// save open dirty scenes even when building another scene, so never run this
// in-editor build while the user has unsaved scene edits.
public static class GameplayRound2Pipeline
{
    const string Evidence="../Builds/Gameplay-Round2-20261118";
    [Serializable] sealed class Record {
        public string result,sourceScene,snapshot,output,startedUtc,completedUtc,error,buildProject;
        public string originalSceneHashBefore,originalSceneHashAfter;
        public bool dirtyBefore,dirtyAfter;
        public int totalErrors,totalWarnings;
    }
    public static void ExportCurrentSceneCopy() {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("The running game was left untouched; export needs edit mode.");
        var scene=SceneManager.GetSceneByPath(ShiyinBuild.MainScene);
        if(!scene.IsValid() || !scene.isLoaded)throw new InvalidOperationException("The main scene must already be open.");
        Directory.CreateDirectory(Evidence);
        string folder="Assets/Chapter01/VerificationSnapshots/Export_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        Directory.CreateDirectory(folder);
        string snapshot=folder+"/Chapter01_Main.unity";
        var record=new Record {sourceScene=scene.path,dirtyBefore=scene.isDirty,originalSceneHashBefore=Hash(scene.path),
            snapshot=Path.GetFullPath(Evidence+"/Chapter01_Main.editor-snapshot.unity"),startedUtc=DateTime.UtcNow.ToString("o")};
        try {
            if(!EditorSceneManager.SaveScene(scene,snapshot,true))throw new InvalidOperationException("Snapshot export failed.");
            File.Copy(snapshot,record.snapshot,true);
            if(scene.path!=record.sourceScene || scene.isDirty!=record.dirtyBefore || Hash(scene.path)!=record.originalSceneHashBefore)
                throw new InvalidOperationException("Export did not preserve the user's original scene.");
            record.result="PASS";
        } catch(Exception error) {record.result="FAIL";record.error=error.ToString();throw;}
        finally {
            record.originalSceneHashAfter=Hash(record.sourceScene);record.dirtyAfter=scene.isDirty;
            record.completedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(Evidence+"/export-report.json",JsonUtility.ToJson(record,true));
            if(File.Exists(snapshot))AssetDatabase.DeleteAsset(snapshot);
        }
    }
    // Only the dedicated temporary project may package an exported scene.
    public static void BuildExportedSnapshot() {
        string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        if(!Application.isBatchMode || !Path.GetFileName(project).StartsWith("ShadowRound2PlayerBuild-",StringComparison.Ordinal))
            throw new InvalidOperationException("This entry point only runs in the dedicated isolated build project.");
        string output=Argument("-round2-output"),evidence=Argument("-round2-evidence");
        if(string.IsNullOrEmpty(output) || string.IsNullOrEmpty(evidence))throw new InvalidOperationException("Explicit output and evidence paths are required.");
        Directory.CreateDirectory(evidence);
        var record=new Record {sourceScene=ShiyinBuild.MainScene,buildProject=project,output=Path.GetFullPath(output),
            originalSceneHashBefore=Hash(ShiyinBuild.MainScene),startedUtc=DateTime.UtcNow.ToString("o")};
        try {
            var report=BuildPipeline.BuildPlayer(new[]{ShiyinBuild.MainScene},record.output,BuildTarget.StandaloneWindows64,BuildOptions.None);
            record.totalErrors=(int)report.summary.totalErrors;record.totalWarnings=(int)report.summary.totalWarnings;
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Isolated build failed: "+report.summary.result);
            record.result="PASS";
        } catch(Exception error) {record.result="FAIL";record.error=error.ToString();throw;}
        finally {
            record.originalSceneHashAfter=Hash(ShiyinBuild.MainScene);record.completedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(evidence,"build-report.json"),JsonUtility.ToJson(record,true));
        }
        Debug.Log("GAMEPLAY_ROUND2_ISOLATED_BUILD_PASS "+record.output);
    }
    static string Argument(string name) {
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);
        return index>=0 && index+1<args.Length ? args[index+1] : null;
    }
    public static void BuildCurrentSceneCopy() {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Finish play mode before building a copy; no running session was changed.");
        var scene=SceneManager.GetSceneByPath(ShiyinBuild.MainScene);
        if(!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Chapter01_Main must already be open. No scene was opened or replaced.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("An open scene has unsaved edits. Export a copy and build it in an isolated project; this editor build will not save the user's scene.");
        Directory.CreateDirectory(Evidence);
        string folder="Assets/Chapter01/VerificationSnapshots/Round2_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        Directory.CreateDirectory(folder);
        string snapshot=folder+"/Chapter01_Main.unity";
        var record=new Record {sourceScene=scene.path,dirtyBefore=scene.isDirty,
            originalSceneHashBefore=Hash(scene.path),startedUtc=DateTime.UtcNow.ToString("o"),
            snapshot=Path.GetFullPath(Evidence+"/Chapter01_Main.editor-snapshot.unity"),
            output=Path.GetFullPath("../Builds/Windows-Chapter01-Combat/ShadowClaw.exe")};
        try {
            if(!EditorSceneManager.SaveScene(scene,snapshot,true))
                throw new InvalidOperationException("Could not save the separate verification snapshot.");
            File.Copy(snapshot,record.snapshot,true);
            if(scene.path!=record.sourceScene || scene.isDirty!=record.dirtyBefore || Hash(scene.path)!=record.originalSceneHashBefore)
                throw new InvalidOperationException("Copying must preserve the user's original scene.");
            var report=BuildPipeline.BuildPlayer(new[]{snapshot},record.output,BuildTarget.StandaloneWindows64,BuildOptions.None);
            record.totalErrors=(int)report.summary.totalErrors;record.totalWarnings=(int)report.summary.totalWarnings;
            if(report.summary.result!=BuildResult.Succeeded)
                throw new InvalidOperationException("Round 2 player build failed: "+report.summary.result);
            if(scene.path!=record.sourceScene || scene.isDirty!=record.dirtyBefore || Hash(record.sourceScene)!=record.originalSceneHashBefore)
                throw new InvalidOperationException("Build completed, but the original scene preservation check failed.");
            record.result="PASS";
        } catch(Exception e) {record.result="FAIL";record.error=e.ToString();throw;}
        finally {
            record.originalSceneHashAfter=Hash(record.sourceScene);record.dirtyAfter=scene.isDirty;
            record.completedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(Evidence+"/build-report.json",JsonUtility.ToJson(record,true));
            // This exact file was created by this invocation; the evidence copy is retained.
            if(File.Exists(snapshot))AssetDatabase.DeleteAsset(snapshot);
        }
        Debug.Log("GAMEPLAY_ROUND2_BUILD_PASS "+record.output);
    }
    static string Hash(string path) {
        using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
    }
}
