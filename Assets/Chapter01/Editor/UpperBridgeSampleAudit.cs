using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Chapter01.Editor
{
    public static class UpperBridgeSampleAudit
    {
        public const string Output = "../Builds/UpperBridgeSample-Verification";
        [Serializable] public class Entry
        {
            public string name, path, asset;
            public Vector3 center, size, position, scale;
            public int triangles;
            public string[] materials, colliders;
        }
        [Serializable] public class Report { public string scene; public List<Entry> objects = new List<Entry>(); }
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            var scene=EditorSceneManager.OpenScene(Chapter01CombatInstaller.ScenePath);
            var report=new Report {scene=scene.path};
            foreach(var r in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MeshRenderer>(true)))
            {
                var b=r.bounds;
                bool scope=b.max.x>=5 && b.min.x<=96 && b.max.z>=55 && b.min.z<=86 && b.max.y>=44 && b.min.y<=63;
                if(!scope)continue;
                var f=r.GetComponent<MeshFilter>();
                report.objects.Add(new Entry { name=r.name,path=Hierarchy(r.transform),position=r.transform.position,
                    center=b.center,size=b.size,scale=r.transform.lossyScale,
                    asset=f&&f.sharedMesh?AssetDatabase.GetAssetPath(f.sharedMesh):"",
                    triangles=f&&f.sharedMesh?Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(i=>(int)f.sharedMesh.GetIndexCount(i))/3:0,
                    materials=r.sharedMaterials.Select(m=>m?AssetDatabase.GetAssetPath(m):"null").ToArray(),
                    colliders=r.GetComponentsInChildren<Collider>(true).Select(c=>c.name).ToArray()});
            }
            File.WriteAllText(Path.Combine(Output,"before-scope.json"),JsonUtility.ToJson(report,true));
            Debug.Log("UPPER_BRIDGE_AUDIT_PASS count="+report.objects.Count);
        }
        static string Hierarchy(Transform t)=>t.parent?Hierarchy(t.parent)+"/"+t.name:t.name;
    }
}
