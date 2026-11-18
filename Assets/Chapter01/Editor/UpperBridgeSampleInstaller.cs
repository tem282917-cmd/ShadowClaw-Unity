using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Chapter01.Rendering;
using Chapter01.Runtime.Testing;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Chapter01.Editor
{
    public static class UpperBridgeSampleInstaller
    {
        public const string ScenePath="Assets/Chapter01/Scenes/Chapter01_UpperBridgeSample.unity";
        public const string AssetFolder="Assets/Chapter01/UpperBridgeSample";
        public const string Output="../Builds/UpperBridgeSample-Verification";
        public const string PlayerPath="../Builds/UpperBridgeSample/ShadowClaw-UpperBridge.exe";
        [Serializable] public class Report
        {
            public string timestampUtc,sourceScene,sourceSha256,scene,scope;
            public int replacedRenderers,replacedGroundColliders,oldLod0Triangles,newTriangles,newRenderers,newGroundColliders,newObstacleColliders;
            public bool sourceSceneUnchanged;
            public string[] replacedObjects;
            public Vector3 playerSpawn;
        }
        static readonly Regex Scope=new Regex(@"(?:^|__)Route_(?:26|27|28|29|30)(?:_|$)|(?:^|__)Edge_(?:26|27|28|29|30)(?:_|$)|^A05__C04_Approved_Deck_(?:27|28|29)_");
        static T[] All<T>(Scene s) where T:Component=>s.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<T>(true)).ToArray();
        static string Hash(string path) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-",""); }

        [MenuItem("Shadow Lab/Upper bridge sample/Build sample scene")]
        public static void Install()
        {
            Directory.CreateDirectory(Output);
            string main=Chapter01CombatInstaller.ScenePath,hash=Hash(main);
            var scene=EditorSceneManager.OpenScene(main);
            string backup=Path.Combine(Output,"Chapter01_Main.source.unity");
            if(!File.Exists(backup))File.Copy(main,backup);
            Capture(scene,"before");
            // Save into an independent review scene before touching any authored objects.
            if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new IOException("Cannot create sample scene");
            var old=All<MeshRenderer>(scene).Where(x=>Scope.IsMatch(x.name)).ToArray();
            if(old.Length<60)throw new InvalidOperationException("Expected upper bridge route, art skins and edge modules");
            var removedColliders=old.SelectMany(x=>x.GetComponentsInChildren<Collider>(true)).Distinct().ToArray();
            int oldTriangles=old.Sum(r=>TriangleCount(r.GetComponent<MeshFilter>()?.sharedMesh));
            var names=old.Select(x=>x.name).Distinct().ToArray();
            foreach(var renderer in old) {
                renderer.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.gameObject);
            }
            var materials=UpperBridgeSampleMaterials.Build();
            var route=Chapter01RuntimeValidation.ReadRoute().points;
            var root=UpperBridgeSampleGeometry.Build(route,materials[0],materials[1],materials[2],materials[3],materials[4]);
            SceneManager.MoveGameObjectToScene(root,scene);
            AddPierCollisions(scene,root.transform);
            var descriptor=root.AddComponent<UpperBridgeSampleDescriptor>();
            descriptor.root=root.transform;
            descriptor.allowedConnectionColliderNames=new[]{"Collision__Route_25","Collision__Route_31_Step_0","Collision__Route_31_Step_1","Collision__Route_31_Step_2"};
            Physics.SyncTransforms();
            var connectionGround=All<Collider>(scene).Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&descriptor.allowedConnectionColliderNames.Contains(c.name)).ToArray();
            if(connectionGround.Length!=4)throw new InvalidOperationException("Missing exact retained floor colliders at sample connections");
            Vector3 entry=route[26]-Vector3.ProjectOnPlane(route[26]-route[25],Vector3.up).normalized*1.25f;
            Vector3 exit=route[31]+Vector3.ProjectOnPlane(route[32]-route[31],Vector3.up).normalized*1.25f;
            descriptor.path=new[]{FloorAt(entry,connectionGround)}.Concat(UpperBridgeSampleGeometry.LastWalkPath).Concat(new[]{FloorAt(exit,connectionGround)}).ToArray();
            descriptor.scopeNames=names;
            descriptor.replacedColliderNames=removedColliders.Select(x=>x.name).ToArray();
            if(descriptor.path==null||descriptor.path.Length<6)throw new InvalidOperationException("Missing geometry walk path");
            Physics.SyncTransforms();
            ConfigureProbes(descriptor);
            var hall=All<IronHallEncounter>(scene).Single();
            hall.StartWithExploration=true;
            hall.GroundColliders=All<Collider>(scene).Where(c=>c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.name.StartsWith("Collision__",StringComparison.Ordinal)).ToArray();
            hall.PlayerSpawn.position=descriptor.path[0]+Vector3.up*.025f;
            var direction=descriptor.path[1]-descriptor.path[0];direction.y=0;
            hall.PlayerSpawn.rotation=Quaternion.LookRotation(direction);
            hall.GameplayCamera.transform.SetPositionAndRotation(hall.PlayerSpawn.position+Vector3.up*1.65f,Quaternion.LookRotation(direction));
            hall.MenuView.SetPositionAndRotation(new Vector3(80,61,88),Quaternion.LookRotation(new Vector3(55,53,74)-new Vector3(80,61,88)));
            hall.GameplayCamera.farClipPlane=2600;
            EditorUtility.SetDirty(hall);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Cannot save sample");
            scene=EditorSceneManager.OpenScene(ScenePath);
            root=scene.GetRootGameObjects().Single(x=>x.name=="UpperBridgeSample");
            descriptor=root.GetComponent<UpperBridgeSampleDescriptor>();
            Physics.SyncTransforms();
            Validate(scene,descriptor);
            Capture(scene,"after");
            var report=new Report {timestampUtc=DateTime.UtcNow.ToString("o"),sourceScene=main,sourceSha256=hash,scene=ScenePath,
                scope="Only route segments 26–30: two stair flights, three upper bridge decks and their turns/rails/collisions. Existing arches and piers retained; no main scene or distant village changes.",
                replacedRenderers=old.Length,replacedGroundColliders=removedColliders.Length,oldLod0Triangles=oldTriangles,
                newTriangles=root.GetComponentsInChildren<MeshFilter>(true).Sum(f=>TriangleCount(f.sharedMesh)),
                newRenderers=root.GetComponentsInChildren<MeshRenderer>(true).Length,
                newGroundColliders=root.GetComponentsInChildren<Collider>(true).Count(c=>c.name.StartsWith("Collision__",StringComparison.Ordinal)),
                newObstacleColliders=root.GetComponentsInChildren<Collider>(true).Count(c=>!c.name.StartsWith("Collision__",StringComparison.Ordinal)),
                replacedObjects=names,playerSpawn=descriptor.path[0],sourceSceneUnchanged=Hash(main)==hash};
            File.WriteAllText(Path.Combine(Output,"sample-installation.json"),JsonUtility.ToJson(report,true));
            if(!report.sourceSceneUnchanged)throw new InvalidOperationException("Source scene changed during sample creation");
            Debug.Log("UPPER_BRIDGE_INSTALL_PASS "+JsonUtility.ToJson(report));
        }
        static int TriangleCount(Mesh mesh)=>mesh?Enumerable.Range(0,mesh.subMeshCount).Sum(i=>(int)mesh.GetIndexCount(i))/3:0;
        static void AddPierCollisions(Scene scene,Transform parent)
        {
            foreach(var renderer in All<MeshRenderer>(scene).Where(r=>Regex.IsMatch(r.name,@"^C05__Rift_Bridge_Pier_(?:27|28|29|30)_tier_"))) {
                var b=renderer.bounds;
                var obj=new GameObject("UB_Obstacle_Pier_"+renderer.name);obj.transform.SetParent(parent,false);obj.transform.position=b.center;
                var box=obj.AddComponent<BoxCollider>();box.size=new Vector3(b.size.x*.88f,b.size.y,b.size.z*.88f);
            }
        }
        static Vector3 FloorAt(Vector3 point,Collider[] ground)
        {
            float highest=float.NegativeInfinity;
            foreach(var c in ground)if(c.Raycast(new Ray(new Vector3(point.x,80,point.z),Vector3.down),out var h,60)&&h.normal.y>.65f)highest=Mathf.Max(highest,h.point.y);
            if(float.IsNegativeInfinity(highest))throw new InvalidOperationException("No sample floor at "+point);
            point.y=highest+.02f;return point;
        }
        static Vector3 ClosestPath(Vector3 point,Vector3[] path)
        {
            float best=float.PositiveInfinity;Vector3 found=path[0];
            for(int i=1;i<path.Length;i++) {
                Vector3 a=path[i-1],d=path[i]-a;d.y=0;Vector3 delta=point-a;delta.y=0;
                float t=Mathf.Clamp01(Vector3.Dot(delta,d)/Mathf.Max(.001f,d.sqrMagnitude));
                Vector3 p=Vector3.Lerp(path[i-1],path[i],t);float distance=new Vector2(point.x-p.x,point.z-p.z).sqrMagnitude;
                if(distance<best){best=distance;found=p;}
            }
            return found;
        }
        static void ConfigureProbes(UpperBridgeSampleDescriptor d)
        {
            var all=d.root.GetComponentsInChildren<Collider>();
            var ground=all.Where(c=>c.name.StartsWith("Collision__",StringComparison.Ordinal)).ToArray();
            var barriers=all.Where(c=>c.name.StartsWith("UB_Obstacle_PlayerBarrier",StringComparison.Ordinal)).ToArray();
            var starts=new List<Vector3>();var dirs=new List<Vector3>();var normals=new List<Vector3>();var travel=new List<float>();
            var raysA=new List<Vector3>();var raysB=new List<Vector3>();var rayHits=new List<bool>();var rayNames=new List<string>();
            foreach(var b in barriers.OrderBy(c=>c.name)) {
                if(starts.Count>=5)break;
                Vector3 p=b.bounds.center,center=ClosestPath(p,d.path),outward=p-center;outward.y=0;
                if(outward.magnitude<2 || starts.Any(s=>Vector3.Distance(s,p)<8))continue;
                outward.Normalize();
                Vector3 start;
                try{start=FloorAt(p-outward*1.25f,ground);}catch{continue;}
                if(!b.Raycast(new Ray(start+Vector3.up*.8f,outward),out var boundary,3))continue;
                float allowed=boundary.distance-.35f;
                if(allowed<.15f || allowed>1.3f)continue;
                starts.Add(start);normals.Add(outward);dirs.Add((outward+Vector3.Cross(Vector3.up,outward)*.35f).normalized);travel.Add(allowed);
                // Search an actual visible opening. Movement barriers are excluded from projectile layers.
                foreach(float offset in new[]{.0f,.25f,-.25f,.5f,-.5f}) {
                    if(rayHits.Count(x=>!x)>=3)break;
                    Vector3 t=Vector3.Cross(Vector3.up,outward)*offset;
                    foreach(float height in new[]{.65f,.82f,1.0f}) {
                        Vector3 feet;try{feet=FloorAt(start+t,ground);}catch{continue;}
                        Vector3 a=feet+Vector3.up*1.65f;
                        Vector3 aim=feet+outward*boundary.distance+Vector3.up*height;
                        Vector3 end=a+(aim-a)*1.25f,delta=end-a;
                        bool clear=true;
                        foreach(float shift in new[]{-.08f,0,.08f}) {
                            foreach(float verticalShift in new[]{-.06f,0,.06f}) {
                                Vector3 origin=a+Vector3.Cross(Vector3.up,outward)*shift+Vector3.up*verticalShift;
                                Vector3 v=end-origin;
                                if(Physics.Raycast(origin,v.normalized,v.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide))clear=false;
                            }
                        }
                        if(clear&&Physics.Raycast(a,delta.normalized,delta.magnitude,1<<2,QueryTriggerInteraction.Ignore)) {
                            raysA.Add(a);raysB.Add(end);rayHits.Add(false);rayNames.Add("");break;
                        }
                    }
                }
            }
            foreach(var c in all.Where(c=>c.name.StartsWith("UB_Obstacle_RailPost",StringComparison.Ordinal)).OrderBy(c=>c.name)) {
                if(rayHits.Count(x=>x)>=3)break;
                Vector3 p=c.bounds.center,center=ClosestPath(p,d.path),outward=p-center;outward.y=0;if(outward.sqrMagnitude<1)continue;
                outward.Normalize();Vector3 feet;try{feet=FloorAt(p-outward*1.3f,ground);}catch{continue;}
                Vector3 a=feet+Vector3.up*1.65f,end=a+(p-a)*1.25f,delta=end-a;
                if(Physics.Raycast(a,delta.normalized,out var hit,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)&&hit.collider==c) {
                    raysA.Add(a);raysB.Add(end);rayHits.Add(true);rayNames.Add(c.name);
                }
            }
            d.barrierStarts=starts.ToArray();d.barrierDirections=dirs.ToArray();d.barrierNormals=normals.ToArray();d.barrierMaxTravel=travel.ToArray();
            d.rayStarts=raysA.ToArray();d.rayEnds=raysB.ToArray();d.rayShouldHit=rayHits.ToArray();d.rayExpectedColliderNames=rayNames.ToArray();
            if(starts.Count<3 || rayHits.Count(x=>x)<2 || rayHits.Count(x=>!x)<2)throw new InvalidOperationException($"Insufficient probes: barriers={starts.Count}, solid={rayHits.Count(x=>x)}, slots={rayHits.Count(x=>!x)}");
        }
        public static void Validate(Scene scene,UpperBridgeSampleDescriptor descriptor)
        {
            if(All<ShadowLab>(scene).Length!=1)throw new InvalidOperationException("Expected actual player system");
            if(All<Collider>(scene).Any(c=>descriptor.replacedColliderNames.Contains(c.name)&&c.enabled&&c.gameObject.activeInHierarchy))throw new InvalidOperationException("Old collider still active");
            var ground=All<Collider>(scene).Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&(c.transform.IsChildOf(descriptor.root)&&c.name.StartsWith("Collision__",StringComparison.Ordinal)||descriptor.allowedConnectionColliderNames.Contains(c.name))).ToArray();
            foreach(var p in descriptor.path)FloorAt(p,ground);
            if(Physics.GetIgnoreLayerCollision(2,2))throw new InvalidOperationException("Actual player layer cannot hit sample barriers");
            if(descriptor.root.GetComponentsInChildren<MeshRenderer>().Any(r=>r.sharedMaterials.Any(m=>!m || !m.shader)))throw new InvalidOperationException("Missing sample material");
        }
        static void Capture(Scene scene,string prefix)
        {
            var hall=All<IronHallEncounter>(scene).Single();var original=hall.GameplayCamera;
            var go=new GameObject("UpperBridge_EditorCapture");SceneManager.MoveGameObjectToScene(go,scene);var camera=go.AddComponent<Camera>();
            camera.CopyFrom(original);camera.enabled=false;camera.fieldOfView=62;camera.nearClipPlane=.06f;camera.farClipPlane=2600;camera.renderingPath=RenderingPath.DeferredShading;camera.allowHDR=true;camera.allowMSAA=false;
            var source=original.GetComponent<Chapter01NightPostProcess>();if(source)EditorUtility.CopySerialized(source,go.AddComponent<Chapter01NightPostProcess>());
            var budget=All<Chapter01SceneRenderBudget>(scene).FirstOrDefault();
            var positions=new[]{new Vector3(113,83,127),new Vector3(84,50.1f,68.4f),new Vector3(63,55.65f,79.4f),new Vector3(27,57.65f,65)};
            var targets=new[]{new Vector3(49,52,73),new Vector3(77,53,73),new Vector3(46,52.7f,70),new Vector3(12,60,69)};
            using(var quality=budget?.BeginPreviewQualityScope())
            try{
                for(int i=0;i<positions.Length;i++) {
                    camera.transform.SetPositionAndRotation(positions[i],Quaternion.LookRotation(targets[i]-positions[i]));
                    var rt=RenderTexture.GetTemporary(1600,900,24,RenderTextureFormat.ARGBHalf);var previous=RenderTexture.active;var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);
                    try{camera.targetTexture=rt;camera.aspect=1600f/900;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1600,900),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Output,$"{prefix}-{i:00}.png"),texture.EncodeToPNG());}
                    finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(texture);}
                }
            }finally{Object.DestroyImmediate(go);}
        }
        public static void BuildPlayer()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPath));
            var scene=EditorSceneManager.OpenScene(ScenePath);Validate(scene,All<UpperBridgeSampleDescriptor>(scene).Single());
            var report=BuildPipeline.BuildPlayer(new[]{ScenePath},PlayerPath,BuildTarget.StandaloneWindows64,BuildOptions.Development);
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Sample build failed: "+report.summary.result);
            File.WriteAllText(Path.Combine(Output,"player-build.txt"),$"UPPER_BRIDGE_BUILD_PASS\n{PlayerPath}\n{report.summary.totalSize}\n{report.summary.totalTime}");
            Debug.Log("UPPER_BRIDGE_BUILD_PASS");
        }
        public static void InstallAndBuild(){Install();BuildPlayer();}
        [MenuItem("Shadow Lab/Upper bridge sample/Open sample scene")]
        public static void OpenSample(){if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);}
        [MenuItem("Shadow Lab/Upper bridge sample/Play sample")]
        public static void PlaySample(){if(EditorApplication.isPlaying)return;OpenSample();if(SceneManager.GetActiveScene().path!=ScenePath)return;SessionState.SetString("UpperBridge.PreviousPlayScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);EditorApplication.isPlaying=true;}
        [InitializeOnLoadMethod]
        static void RestorePlaySceneAfterSample(){EditorApplication.playModeStateChanged+=state=>{if(state!=PlayModeStateChange.EnteredEditMode)return;string previous=SessionState.GetString("UpperBridge.PreviousPlayScene","__unset");if(previous=="__unset")return;EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(previous)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);SessionState.EraseString("UpperBridge.PreviousPlayScene");};}
    }
}
