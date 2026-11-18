using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit, local one-shot jobs for the already-open editor. No scene or asset
// is changed unless a request file is written for this revision.
[InitializeOnLoad]
public static class GameplayRevisionBridge
{
    [Serializable] public sealed class Request {
        public string id, command, typeName, method, expectedSha256;
        public string[] names;
        public bool allowExitPlay;
    }
    [Serializable] public sealed class Item {
        public string name, path, kind;
        public Vector3 position, scale, minimum, maximum;
        public bool enabled;
    }
    [Serializable] public sealed class SceneInfo {
        public string name, path;
        public bool dirty;
    }
    [Serializable] public sealed class Reply {
        public string id, status, error, timestamp;
        public bool playing;
        public List<SceneInfo> scenes=new List<SceneInfo>();
        public List<Item> items=new List<Item>();
    }
    static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath,"../../Builds/Gameplay-Revision-20261118"));
    static string Input => Path.Combine(Folder,"editor-request.json");
    static double next;
    static bool busy;
    static GameplayRevisionBridge() { EditorApplication.update+=Poll; }
    static string Hierarchy(Transform t) => t.parent ? Hierarchy(t.parent)+"/"+t.name : t.name;
    static void Write(Reply reply) {
        Directory.CreateDirectory(Folder);
        reply.timestamp=DateTime.UtcNow.ToString("o");
        File.WriteAllText(Path.Combine(Folder,"editor-response.json"),JsonUtility.ToJson(reply,true));
    }
    static void Poll() {
        if(Application.isBatchMode || busy || EditorApplication.timeSinceStartup<next)return;
        next=EditorApplication.timeSinceStartup+.25;
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Input))return;
        var reply=new Reply();
        try {
            var request=JsonUtility.FromJson<Request>(File.ReadAllText(Input));
            reply.id=request.id;reply.playing=EditorApplication.isPlaying;
            if(request.command!="inspect" && EditorApplication.isPlayingOrWillChangePlaymode) {
                if(!request.allowExitPlay)throw new InvalidOperationException("This job needs edit mode; the running game was left untouched.");
                EditorApplication.isPlaying=false;
                reply.status="waiting_for_edit_mode";Write(reply);return;
            }
            busy=true;
            if(request.command=="inspect")Inspect(request,reply);
            else if(request.command=="reloadCleanMainScene") {
                ReloadCleanMainScene(request.expectedSha256);
                Inspect(request,reply);
            }
            else if(request.command=="invoke") {
                var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(request.typeName)).FirstOrDefault(t=>t!=null);
                if(type==null)throw new InvalidOperationException("Editor task type unavailable: "+request.typeName);
                var method=type.GetMethod(request.method,BindingFlags.Static|BindingFlags.Public);
                if(method==null || method.GetParameters().Length!=0)
                    throw new InvalidOperationException("Expected a public static zero-argument editor task.");
                method.Invoke(null,null);
                AssetDatabase.SaveAssets();
            } else throw new InvalidOperationException("Unknown local editor job: "+request.command);
            reply.status="complete";
        } catch(Exception e) {reply.status="failed";reply.error=(e.InnerException ?? e).ToString();}
        finally {busy=false;}
        Write(reply);
        if(File.Exists(Input))File.Delete(Input);
    }
    static void ReloadCleanMainScene(string expectedSha256) {
        const string path="Assets/Chapter01/Scenes/Chapter01_Main.unity";
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount!=1 ||
            PrefabStageUtility.GetCurrentPrefabStage()!=null || !scene.isLoaded || scene.path!=path || scene.isDirty)
            throw new InvalidOperationException("Reload requires the clean main scene alone in edit mode.");
        using(var file=File.OpenRead(path))
        using(var sha=SHA256.Create()) {
            string hash=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");
            if(!string.Equals(hash,expectedSha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The saved scene changed since verification.");
        }
        var selected=Selection.objects;
        var active=Selection.activeObject;
        var selection=selected.Select(obj=>GlobalObjectId.GetGlobalObjectIdSlow(obj)).ToArray();
        for(int i=0;i<selected.Length;i++)
            if(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(selection[i])!=selected[i])
                throw new InvalidOperationException("The selection contains an unsaved object.");
        int activeIndex=Array.IndexOf(selected,active);
        var restored=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        if(!restored.isLoaded || restored.path!=path)
            throw new InvalidOperationException("Could not reopen the saved main scene.");
        var objects=selection.Select(GlobalObjectId.GlobalObjectIdentifierToObjectSlow).ToArray();
        // Unity uses the first object as the active object of a multi-selection.
        if(activeIndex>0) (objects[0],objects[activeIndex])=(objects[activeIndex],objects[0]);
        Selection.objects=objects;
        if(objects.Any(obj=>!obj) || (activeIndex>=0 && Selection.activeObject!=objects[0]))
            throw new InvalidOperationException("The scene reopened but part of the selection could not be restored.");
    }
    static void Inspect(Request request,Reply reply) {
        for(int i=0;i<SceneManager.sceneCount;i++) {
            var scene=SceneManager.GetSceneAt(i);
            reply.scenes.Add(new SceneInfo{name=scene.name,path=scene.path,dirty=scene.isDirty});
        }
        foreach(var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            if(!t.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(t.gameObject.scene))continue;
            if(request.names!=null && request.names.Length>0 && !request.names.Any(n=>t.name.IndexOf(n,StringComparison.OrdinalIgnoreCase)>=0))continue;
            var bounds=new Bounds(t.position,Vector3.zero);bool hasBounds=false;
            foreach(var r in t.GetComponentsInChildren<Renderer>(true)) {
                if(!hasBounds){bounds=r.bounds;hasBounds=true;}else bounds.Encapsulate(r.bounds);
            }
            var collider=t.GetComponent<Collider>();
            if(!hasBounds && collider){bounds=collider.bounds;hasBounds=true;}
            var light=t.GetComponent<Light>();
            reply.items.Add(new Item{name=t.name,path=Hierarchy(t),kind=light ? light.type+" Light" : collider ? collider.GetType().Name : "Transform",
                position=t.position,scale=t.lossyScale,minimum=bounds.min,maximum=bounds.max,enabled=t.gameObject.activeInHierarchy});
        }
    }
}
