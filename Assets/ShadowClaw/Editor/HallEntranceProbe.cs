using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HallEntranceProbe
{
    public static void PrepareAuthoringAssets() {
        EncounterPrefabBuilder.EnsureAll();
        EditorSceneManager.OpenScene("Assets/Chapter01/Scenes/Chapter01_Main.unity");
        Run();
        Debug.Log("HALL_AUTHORING_PREPARED");
    }
    [Serializable] public sealed class Row { public float height, left, right;public bool centreBlocked; }
    [Serializable] public sealed class Report { public string objectName;public Vector3 min,max;public List<Row> rows=new List<Row>(); }
    public static void Run() {
        var root=GameObject.Find("Castle/B03__Great_Entrance__0");
        if(!root)throw new InvalidOperationException("The authored main entrance is not loaded.");
        var report=new Report{objectName=root.name};var renderers=root.GetComponentsInChildren<Renderer>();
        var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);report.min=b.min;report.max=b.max;
        var scene=EditorSceneManager.NewPreviewScene();var colliders=new List<Collider>();
        bool backfaces=Physics.queriesHitBackfaces;
        try {
            Physics.queriesHitBackfaces=true;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>()) {
                var probe=new GameObject("Door measurement mesh");SceneManager.MoveGameObjectToScene(probe,scene);
                probe.transform.SetPositionAndRotation(filter.transform.position,filter.transform.rotation);
                probe.transform.localScale=filter.transform.lossyScale;
                var collider=probe.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;colliders.Add(collider);
            }
            Physics.SyncTransforms();
            bool Solid(float x,float y) {
                var ray=new Ray(new Vector3(x,y,b.min.z-1),Vector3.forward);
                foreach(var c in colliders)if(c.Raycast(ray,out var hit,b.size.z+2))return true;
                return false;
            }
            for(float height=.5f;height<=21;height+=.5f) {
                var row=new Row{height=height,centreBlocked=Solid(0,b.min.y+height),left=b.min.x,right=b.max.x};
                if(!row.centreBlocked) {
                    for(float x=.1f;x<b.max.x;x+=.1f)if(Solid(x,b.min.y+height)){row.right=x;break;}
                    for(float x=-.1f;x>b.min.x;x-=.1f)if(Solid(x,b.min.y+height)){row.left=x;break;}
                }
                report.rows.Add(row);
            }
        } finally {Physics.queriesHitBackfaces=backfaces;EditorSceneManager.ClosePreviewScene(scene);}
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Builds/Gameplay-Revision-20261118"));
        Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"entrance-probe.json"),JsonUtility.ToJson(report,true));
    }
}
