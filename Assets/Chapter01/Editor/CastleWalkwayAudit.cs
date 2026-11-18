using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Chapter01.Editor
{
    public static class CastleWalkwayAudit
    {
        public const string Output="../Builds/CastleWalkway-Verification";
        [Serializable] public class Entry
        {
            public string name,path,asset;
            public Vector3 center,size,localCenter,localSize,position,scale;
            public Quaternion rotation;
            public int triangles,layer;
            public bool active;
            public string[] materials,colliders;
        }
        [Serializable] public class CollisionEntry
        {
            public string name,path,type;
            public Vector3 center,size;
            public bool active,enabled,trigger;
            public int layer;
        }
        [Serializable] public class Report
        {
            public string scene,authoredHallJson;
            public Vector3 spawn,menu,boss,battleCenter,battleSize;
            public List<Entry> objects=new List<Entry>();
            public List<CollisionEntry> colliders=new List<CollisionEntry>();
        }
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            string source=Chapter01CombatInstaller.ScenePath;
            if(!File.Exists(Path.Combine(Output,"Chapter01_Main.before-walkway.unity")))File.Copy(source,Path.Combine(Output,"Chapter01_Main.before-walkway.unity"));
            var scene=EditorSceneManager.OpenScene(source);
            var report=new Report{scene=scene.path};
            foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)))
            {
                var f=r.GetComponent<MeshFilter>();var mesh=f?f.sharedMesh:null;var b=r.bounds;var local=mesh?mesh.bounds:new Bounds();
                report.objects.Add(new Entry{name=r.name,path=Hierarchy(r.transform),asset=mesh?AssetDatabase.GetAssetPath(mesh):"",
                    center=b.center,size=b.size,localCenter=local.center,localSize=local.size,position=r.transform.position,rotation=r.transform.rotation,scale=r.transform.lossyScale,
                    triangles=mesh?Enumerable.Range(0,mesh.subMeshCount).Sum(i=>(int)mesh.GetIndexCount(i))/3:0,
                    active=r.gameObject.activeInHierarchy,layer=r.gameObject.layer,
                    materials=r.sharedMaterials.Select(m=>m?AssetDatabase.GetAssetPath(m):"null").ToArray(),colliders=r.GetComponentsInChildren<Collider>(true).Select(c=>c.name).ToArray()});
            }
            Physics.SyncTransforms();
            foreach(var c in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>(true)))
                report.colliders.Add(new CollisionEntry{name=c.name,path=Hierarchy(c.transform),type=c.GetType().Name,center=c.bounds.center,size=c.bounds.size,active=c.gameObject.activeInHierarchy,enabled=c.enabled,trigger=c.isTrigger,layer=c.gameObject.layer});
            var hall=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<IronHallEncounter>(true)).Single();
            report.authoredHallJson=EditorJsonUtility.ToJson(hall);report.spawn=hall.PlayerSpawn.position;report.menu=hall.MenuView.position;report.boss=hall.DemonSpawn.position;report.battleCenter=hall.BattleArea.bounds.center;report.battleSize=hall.BattleArea.bounds.size;
            File.WriteAllText(Path.Combine(Output,"before-audit.json"),JsonUtility.ToJson(report,true));
            Debug.Log("CASTLE_WALKWAY_AUDIT_PASS renderers="+report.objects.Count+" colliders="+report.colliders.Count);
        }
        static string Hierarchy(Transform t)=>t.parent?Hierarchy(t.parent)+"/"+t.name:t.name;
    }
}
