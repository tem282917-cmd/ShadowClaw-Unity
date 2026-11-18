using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run the same opt-in acceptance routines before packaging the player.
public static class GameplayRevisionPlayCheck {
    const string Key="GameplayRevisionPlayCheck.Active";
    [Serializable] sealed class Result {public string result;}
    public static void InstallAndRun() {GameplayRevisionInstaller.Apply();Run();}
    public static void Run() {
        var args=Environment.GetCommandLineArgs();
        int index=Array.IndexOf(args,"-chapter01-evidence");
        if(index<0 || index+1>=args.Length)throw new Exception("An evidence directory is required.");
        string folder=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(folder);
        string name=Array.IndexOf(args,"-check-chapter01-presentation")>=0 ? "presentation-check.json" : "combat-check.json";
        string report=Path.Combine(folder,name);
        if(File.Exists(report))throw new Exception("Use a fresh evidence folder.");
        EditorSceneManager.OpenScene(ShiyinBuild.MainScene);
        SessionState.SetString(Key+"Report",report);
        SessionState.SetString(Key+"Since",DateTime.UtcNow.Ticks.ToString());
        SessionState.SetBool(Key,true);Monitor();EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod] static void Monitor() {
        EditorApplication.update-=Poll;
        if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;
    }
    static void Poll() {
        string path=SessionState.GetString(Key+"Report","");
        if(File.Exists(path)) {
            var report=JsonUtility.FromJson<Result>(File.ReadAllText(path));
            SessionState.SetBool(Key,false);EditorApplication.Exit(report.result=="PASS" ? 0 : 1);
        } else if(long.TryParse(SessionState.GetString(Key+"Since","0"),out long start) &&
                  DateTime.UtcNow.Ticks-start>TimeSpan.FromMinutes(4).Ticks) {
            SessionState.SetBool(Key,false);Debug.LogError("GAMEPLAY_REVISION_PLAY_TIMEOUT");EditorApplication.Exit(1);
        }
    }
}
