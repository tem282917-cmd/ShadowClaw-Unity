using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    // Explicit, isolated-clone authoring command. No initialize-on-load behavior.
    // Adds one prefab instance only after geometry, lights and sightlines pass.
    public static class GuardianPlacementAuthoring
    {
        const string ScenePath="Assets/Chapter01/Scenes/Chapter01_Main.unity";
        const string PrefabPath="Assets/ShadowClaw/Prefabs/Encounters/FixedLanternGuardian.prefab";
        const string RootName="GuardianEncounter_Chapel";
        [Serializable] sealed class Sample {
            public Vector3 position;
            public float value;
            public string surface;
        }
        [Serializable] sealed class Report {
            public string status="running",failure,sourceSceneSha256,savedSceneSha256,projectPath;
            public string scene=ScenePath,placement=RootName;
            public Vector3 guardianPosition,arenaCenter,arenaSize,modelBounds;
            public float heading=180,minimumHeadroom=1000;
            public int preexistingTransforms,preservedTransforms,guardiansBefore,guardiansAfter;
            public int sceneLightsBefore,sceneLightsAfter,ordinaryLampCount;
            public string[] expectedOrdinaryLamps,bodyBlockers;
            public List<Sample> floorSamples=new List<Sample>(),headroomSamples=new List<Sample>(),sightlines=new List<Sample>();
            public string binding="Nearest three existing ordinary lamps within 24m, stable for this attempt; never final-boss lamps.";
        }
        sealed class TransformSnapshot {
            public Transform target,parent;
            public string name;
            public bool active;
            public Vector3 position,scale;
            public Quaternion rotation;
        }
        sealed class MeshData {public Vector3[] vertices;public int[] indices;}
        static readonly Dictionary<Mesh,MeshData> meshes=new Dictionary<Mesh,MeshData>();

        public static void RunProbeAndPlace() {
            var report=new Report {projectPath=Path.GetFullPath(Path.Combine(Application.dataPath,".."))};
            string output=Argument("-guardian-placement-evidence");
            if(string.IsNullOrEmpty(output))output=Path.Combine(report.projectPath,"GuardianPlacementEvidence");
            Directory.CreateDirectory(output);
            try {
                if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Placement requires edit mode.");
                if(string.Equals(report.projectPath.TrimEnd(Path.DirectorySeparatorChar),@"D:\Shadow\ShadowClaw",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Run this command in the reviewed isolated clone; source-scene publication belongs to the parent task.");
                report.sourceSceneSha256=Hash(ScenePath);
                var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
                var roots=scene.GetRootGameObjects();
                var lab=roots.SelectMany(r=>r.GetComponentsInChildren<ShadowLab>(true)).Single();
                var hall=lab.GetComponent<IronHallEncounter>();
                Require(hall!=null && hall.UseAuthoredLayout,"Expected authored main-scene encounter.");
                var existing=roots.SelectMany(r=>r.GetComponentsInChildren<FixedLanternGuardian>(true)).ToArray();
                report.guardiansBefore=existing.Length;
                Require(existing.Length==0,"The scene already has a guardian; inspect it instead of adding a duplicate.");
                Require(!roots.Any(r=>r.name==RootName),"Placement root already exists; this command never replaces user objects.");
                var before=roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>new TransformSnapshot {
                    target=t,parent=t.parent,name=t.name,active=t.gameObject.activeSelf,
                    position=t.localPosition,rotation=t.localRotation,scale=t.localScale
                }).ToArray();
                report.preexistingTransforms=before.Length;
                var lights=roots.SelectMany(r=>r.GetComponentsInChildren<Light>(true)).ToArray();
                report.sceneLightsBefore=lights.Length;
                Physics.SyncTransforms();
                var proposed=new Vector3(95,48,52);
                RaycastHit floor=Floor(scene,proposed);
                Require(Mathf.Abs(floor.point.y-48)<.3f,"Chapel floor has unexpected height: "+floor.point);
                proposed.y=floor.point.y;
                report.guardianPosition=proposed;
                report.arenaCenter=new Vector3(93,proposed.y,45);
                report.arenaSize=new Vector3(24,0,32);
                Require(!hall.PlayArea.Contains(new Vector2(proposed.x,proposed.z)),"Guardian must remain before the final boss room.");

                // The 24 x 32m arena stays 3-5m inside the existing 30 x 40m chapel.
                // Its northern space lets the player circle and shoot the enlarged shadow.
                for(int x=0;x<5;x++)for(int z=0;z<5;z++) {
                    Vector3 sample=report.arenaCenter+new Vector3(-12+x*6,0,-16+z*8);
                    var hit=Floor(scene,sample);
                    report.floorSamples.Add(new Sample {position=sample,value=hit.point.y-proposed.y,surface=hit.collider.name});
                    Require(Mathf.Abs(hit.point.y-proposed.y)<.3f && hit.normal.y>.85f,"Arena floor support failed at "+sample+" on "+hit.collider.name);
                }
                var blockers=Physics.OverlapBox(proposed+Vector3.up*4.75f,new Vector3(2.45f,4.6f,1.7f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore)
                    .Where(c=>c.gameObject.scene==scene && c.enabled && c.gameObject.activeInHierarchy).ToArray();
                report.bodyBlockers=blockers.Select(c=>c.name).ToArray();
                Require(blockers.Length==0,"The 4.9 x 9.2 x 3.4m raised-arm clearance volume intersects "+string.Join(", ",report.bodyBlockers));

                // Check rendered roof/rib triangles as well as physical colliders.
                // Decorative roofs need not have a Collider to block a 9m giant.
                var filters=roots.SelectMany(r=>r.GetComponentsInChildren<MeshFilter>(false))
                    .Where(f=>f.sharedMesh!=null && f.GetComponent<Renderer>()!=null && f.GetComponent<Renderer>().enabled).ToArray();
                for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++) {
                    Vector3 point=proposed+new Vector3(x*2.4f,.12f,z*1.65f);
                    float ceiling=RayMeshes(filters,new Ray(point,Vector3.up),35,out string surface);
                    float clear=float.IsInfinity(ceiling)?35:ceiling+.12f;
                    report.minimumHeadroom=Mathf.Min(report.minimumHeadroom,clear);
                    report.headroomSamples.Add(new Sample {position=point,value=clear,surface=surface});
                    Require(clear>=9.3f,"Rendered roof/architecture leaves only "+clear+"m at "+point+" ("+surface+").");
                }
                foreach(var view in new[]{new Vector3(81.5f,proposed.y+1.65f,38),new Vector3(93,proposed.y+1.65f,38)}) {
                    Vector3 target=proposed+Vector3.up*5.2f,delta=target-view;
                    float obstacle=RayMeshes(filters,new Ray(view,delta.normalized),delta.magnitude,out string surface);
                    report.sightlines.Add(new Sample {position=view,value=float.IsInfinity(obstacle)?delta.magnitude:obstacle,surface=surface});
                    Require(float.IsInfinity(obstacle),"Chapel approach cannot see guardian torso because of "+surface);
                }

                var renderers=roots.SelectMany(r=>r.GetComponentsInChildren<Renderer>(false)).ToArray();
                var ordinary=lights.Where(l=>l.gameObject.activeInHierarchy && (l.type==LightType.Point || l.type==LightType.Spot) &&
                    l.name.StartsWith("WarmPool__",StringComparison.Ordinal) && !l.name.StartsWith("WarmPool__D02__Six_Combat_Lantern_",StringComparison.Ordinal) &&
                    l.intensity>0 && l.range>0 && Mathf.Abs(l.transform.position.y-proposed.y)<=10.8f &&
                    Vector2.Distance(new Vector2(l.transform.position.x,l.transform.position.z),new Vector2(proposed.x,proposed.z))<=24 &&
                    renderers.Any(r=>r.enabled && (r.name==l.name.Substring(10) || r.name.StartsWith(l.name.Substring(10)+"__",StringComparison.Ordinal)) && r.bounds.SqrDistance(l.transform.position)<16))
                    .OrderBy(l=>(l.transform.position-proposed).sqrMagnitude).ToArray();
                report.ordinaryLampCount=ordinary.Length;
                report.expectedOrdinaryLamps=ordinary.Take(3).Select(l=>l.name+" @ "+l.transform.position.ToString("F3")).ToArray();
                Require(ordinary.Length>=3,"Fewer than three existing ordinary fixture lights can support this placement.");
                Require(ordinary.Take(3).All(l=>l.name.Contains("Chapel_Candle")),"A nearer non-chapel fixture would change the intended teaching lamp group.");

                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Require(prefab!=null && prefab.GetComponent<FixedLanternGuardian>()!=null,"Guardian prefab is missing.");
                var group=new GameObject(RootName);SceneManager.MoveGameObjectToScene(group,scene);
                var placed=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                placed.name="Guardian - Chapel Lampkeeper";placed.transform.SetParent(group.transform,false);
                placed.transform.SetPositionAndRotation(proposed,Quaternion.Euler(0,180,0));placed.transform.localScale=Vector3.one;
                var center=new GameObject("GuardianArenaCenter").transform;center.SetParent(group.transform,false);
                center.SetPositionAndRotation(report.arenaCenter,Quaternion.identity);
                var guardian=placed.GetComponent<FixedLanternGuardian>();
                guardian.Encounter=lab;guardian.BodyHeight=7.8f;guardian.ArenaCenter=center;guardian.ArenaSize=new Vector2(24,32);
                guardian.StartActive=true;guardian.StartOnArenaEntry=true;guardian.Activated=true;
                guardian.AutoLinkNearbyOrdinaryLamps=true;guardian.NearbyLampLimit=3;guardian.LampSearchRadius=24;
                guardian.LinkedLanterns=Array.Empty<EncounterLantern>();guardian.LampIndices=Array.Empty<int>();
                PrefabUtility.RecordPrefabInstancePropertyModifications(guardian);
                PrefabUtility.RecordPrefabInstancePropertyModifications(placed.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(placed);
                EditorUtility.SetDirty(guardian);
                var preview=placed.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled && !(r is ParticleSystemRenderer)).ToArray();
                if(preview.Length>0) {Bounds bounds=preview[0].bounds;foreach(var r in preview.Skip(1))bounds.Encapsulate(r.bounds);report.modelBounds=bounds.size;}
                foreach(var prior in before) {
                    Require(prior.target!=null && prior.target.parent==prior.parent && prior.target.name==prior.name &&
                        prior.target.gameObject.activeSelf==prior.active && prior.target.localPosition.Equals(prior.position) &&
                        prior.target.localRotation.Equals(prior.rotation) && prior.target.localScale.Equals(prior.scale),
                        "A pre-existing transform changed during placement: "+prior.name);
                    report.preservedTransforms++;
                }
                report.guardiansAfter=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<FixedLanternGuardian>(true)).Count();
                report.sceneLightsAfter=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Light>(true)).Count();
                Require(report.guardiansAfter==1,"Expected exactly one placed guardian.");
                Require(report.sceneLightsAfter==report.sceneLightsBefore,"Placement unexpectedly added scene lights.");
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene,ScenePath),"Could not save the isolated placed scene.");
                report.savedSceneSha256=Hash(ScenePath);report.status="PASS";
                Debug.Log("GUARDIAN_PLACEMENT_PASS "+JsonUtility.ToJson(report));
            } catch(Exception error) {report.status="FAIL";report.failure=error.ToString();throw;}
            finally {File.WriteAllText(Path.Combine(output,"guardian-placement-authoring.json"),JsonUtility.ToJson(report,true));meshes.Clear();}
        }

        static RaycastHit Floor(Scene scene,Vector3 point) {
            var hits=Physics.RaycastAll(point+Vector3.up*5,Vector3.down,10,~0,QueryTriggerInteraction.Ignore)
                .Where(h=>h.collider.gameObject.scene==scene && h.normal.y>.7f).OrderBy(h=>h.distance).ToArray();
            Require(hits.Length>0,"No existing floor below "+point);return hits[0];
        }
        static float RayMeshes(MeshFilter[] filters,Ray ray,float maximum,out string surface) {
            float nearest=float.PositiveInfinity;surface="clear";
            foreach(var filter in filters) {
                var bounds=filter.GetComponent<Renderer>().bounds;
                if(!bounds.IntersectRay(ray,out float broad) || broad>maximum)continue;
                var mesh=filter.sharedMesh;
                if(!meshes.TryGetValue(mesh,out var data)) {
                    data=new MeshData {vertices=mesh.vertices,indices=mesh.triangles};meshes.Add(mesh,data);
                }
                var matrix=filter.transform.worldToLocalMatrix;
                Vector3 origin=matrix.MultiplyPoint3x4(ray.origin),direction=matrix.MultiplyVector(ray.direction);
                for(int i=0;i<data.indices.Length;i+=3) {
                    var a=data.vertices[data.indices[i]];var e1=data.vertices[data.indices[i+1]]-a;var e2=data.vertices[data.indices[i+2]]-a;
                    Vector3 p=Vector3.Cross(direction,e2);double det=Vector3.Dot(e1,p);
                    if(Math.Abs(det)<1e-14)continue;
                    double inv=1/det;Vector3 t=origin-a;
                    double u=Vector3.Dot(t,p)*inv;if(u<0 || u>1)continue;
                    Vector3 q=Vector3.Cross(t,e1);double v=Vector3.Dot(direction,q)*inv;if(v<0 || u+v>1)continue;
                    float distance=(float)(Vector3.Dot(e2,q)*inv);
                    if(distance<=.002f || distance>=nearest || distance>maximum)continue;
                    nearest=distance;surface=filter.name;
                }
            }
            return nearest;
        }
        static string Hash(string path) {
            if(!Path.IsPathRooted(path))path=Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");
        }
        static string Argument(string name) {var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);return index>=0 && index+1<args.Length?args[index+1]:null;}
        static void Require(bool condition,string text) {if(!condition)throw new InvalidOperationException(text);}
    }
}
