using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Chapter01.Editor
{
    public static class Chapter01CollisionDiagnostics
    {
        const float Dt=.02f, Speed=3, Radius=.28f, Height=1.8f, Step=.4f;
        [Serializable] public sealed class Hit
        {
            public string collider,query;
            public Vector3 point,normal;
            public float distance;
            public int triangle;
        }
        [Serializable] public sealed class Sample
        {
            public int step,segment;
            public Vector3 feet;
            public float simulatedSeconds,groundGap;
            public string floor,flags;
            public bool grounded;
        }
        [Serializable] public sealed class Report
        {
            public string schema="shadow.route-static-physics-gate/v1",timestampUtc,note="Editor-only deterministic CharacterController.Move on the actual static colliders. No camera renders, no performance/FPS conclusion; real standalone route test is still required.";
            public float fixedDeltaTime=Dt,walkSpeed=Speed,radius=Radius,height=Height,stepOffset=Step,slopeLimit=48,skinWidth=.035f;
            public bool passed;
            public string failure;
            public int segment,steps;
            public float simulatedSeconds,travelledMeters;
            public double wallSeconds;
            public Vector3 finalFeet;
            public List<Sample> samples=new List<Sample>();
            public List<Hit> blockingQueries=new List<Hit>();
        }
        public static void InspectCurrent()
        {
            var scene=EditorSceneManager.OpenScene(Chapter01CastleNightBuilder.ScenePath,OpenSceneMode.Single);
            var route=ReadRoute();
            var report=RunGate(scene,route);
            Write("physics-gate-before-fix.json",report);
            Debug.Log("PHYSICS_GATE_BEFORE "+JsonUtility.ToJson(new { report.passed }));
        }
        public static void RebuildAndGate()
        {
            var scene=EditorSceneManager.OpenScene(Chapter01CastleNightBuilder.ScenePath,OpenSceneMode.Single);
            var route=ReadRoute();
            var collision=Chapter01CollisionBuilder.Apply(scene,route);
            File.WriteAllText(Path.Combine(Chapter01CastleNightBuilder.Output,"collision-junction-fix.json"),JsonUtility.ToJson(collision,true));
            var report=RunGate(scene,route);
            Write("physics-gate-after-fix.json",report);
            if(!report.passed) throw new InvalidOperationException("Static physics route failed: "+report.failure+" segment "+report.segment+" feet "+report.finalFeet);
            if(collision.unacceptableFloors>0) throw new InvalidOperationException("Collision floor samples failed: "+collision.unacceptableFloors);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("CHAPTER01_STATIC_PHYSICS_ROUTE_PASS steps="+report.steps+" metres="+report.travelledMeters);
        }
        static Vector3[] ReadRoute() => JsonUtility.FromJson<Chapter01CastleNightBuilder.RouteFile>(File.ReadAllText(Path.Combine(Application.dataPath,"Chapter01/Data/castle-route-v025.json"))).points;
        static void Write(string name,Report report)
        {
            Directory.CreateDirectory(Chapter01CastleNightBuilder.Output);
            File.WriteAllText(Path.Combine(Chapter01CastleNightBuilder.Output,name),JsonUtility.ToJson(report,true));
        }
        public static Report RunGate(Scene scene,Vector3[] route)
        {
            var report=new Report { timestampUtc=DateTime.UtcNow.ToString("o") };
            var stopwatch=Stopwatch.StartNew();
            var root=scene.GetRootGameObjects().Single(x=>x.name=="Chapter01_Collision").transform;
            var oldControllers=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CharacterController>(true)).ToArray();
            var enabled=oldControllers.Select(x=>x.enabled).ToArray();
            foreach(var cc in oldControllers)cc.enabled=false;
            var oldSimulation=Physics.simulationMode;
            GameObject go=null;
            try
            {
                Physics.simulationMode=SimulationMode.Script;
                go=new GameObject("Temporary static route physics gate");SceneManager.MoveGameObjectToScene(go,scene);
                var controller=go.AddComponent<CharacterController>();
                controller.height=Height;controller.radius=Radius;controller.center=Vector3.up*(Height*.5f);
                controller.stepOffset=Step;controller.slopeLimit=48;controller.skinWidth=.035f;controller.minMoveDistance=0;
                controller.enabled=false;go.transform.position=route[0]+Vector3.up*.15f;
                Physics.SyncTransforms();
                if(FindFloor(root,route[0]+Vector3.up*4,8,out var initial))go.transform.position=initial.point+Vector3.up*.08f;
                controller.enabled=true;Physics.SyncTransforms();
                float vertical=0,stuck=0;var lastProgress=go.transform.position;
                int segment=0;
                for(int step=0;step<60000;step++)
                {
                    var previous=go.transform.position;
                    var to=route[segment+1]-previous;to.y=0;
                    if(to.magnitude<=.10f)
                    {
                        if(Mathf.Abs(previous.y-route[segment+1].y)>1.25f) { report.failure="Waypoint reached at wrong elevation";break; }
                        if(segment==route.Length-2) { report.passed=true;break; }
                        segment++;to=route[segment+1]-previous;to.y=0;
                    }
                    if(controller.isGrounded&&vertical<0)vertical=-3;
                    vertical=Mathf.Max(-30,vertical-20*Dt);
                    var flags=controller.Move(to.normalized*Mathf.Min(Speed*Dt,to.magnitude)+Vector3.up*(vertical*Dt));
                    Physics.SyncTransforms();Physics.Simulate(Dt);
                    report.steps=step+1;report.segment=segment;report.simulatedSeconds=(step+1)*Dt;
                    report.finalFeet=go.transform.position;report.travelledMeters+=Vector3.Distance(previous,report.finalFeet);
                    float gap=-999;string floor="";
                    if(FindFloor(root,report.finalFeet+Vector3.up*.35f,2.4f,out var ground)) { gap=report.finalFeet.y-ground.point.y;floor=ground.collider.name; }
                    if(step%25==0)report.samples.Add(new Sample { step=step,segment=segment,feet=report.finalFeet,simulatedSeconds=report.simulatedSeconds,
                        grounded=controller.isGrounded,groundGap=gap,floor=floor,flags=flags.ToString() });
                    if(report.finalFeet.y<Mathf.Min(route[segment].y,route[segment+1].y)-2.5f) { report.failure="Fell below route support";break; }
                    if(step%100==99)
                    {
                        stuck=Vector3.Distance(lastProgress,report.finalFeet)<.2f?stuck+2:0;
                        lastProgress=report.finalFeet;
                        if(stuck>=8) { report.failure="Controller blocked for 8 simulated seconds";break; }
                    }
                }
                if(!report.passed&&string.IsNullOrEmpty(report.failure))report.failure="Exceeded simulation step budget";
                if(!report.passed)
                    report.blockingQueries=Probe(root,go.transform.position,route[Mathf.Min(report.segment+1,route.Length-1)]-go.transform.position);
            }
            finally
            {
                if(go!=null)Object.DestroyImmediate(go);
                Physics.simulationMode=oldSimulation;
                for(int i=0;i<oldControllers.Length;i++)if(oldControllers[i])oldControllers[i].enabled=enabled[i];
                Physics.SyncTransforms();stopwatch.Stop();report.wallSeconds=stopwatch.Elapsed.TotalSeconds;
            }
            return report;
        }
        static bool FindFloor(Transform root,Vector3 origin,float distance,out RaycastHit hit)
        {
            var hits=Physics.RaycastAll(origin,Vector3.down,distance,~0,QueryTriggerInteraction.Ignore)
                .Where(x=>x.collider.transform.IsChildOf(root)).OrderBy(x=>x.distance).ToArray();
            hit=hits.Length>0?hits[0]:default;return hits.Length>0;
        }
        public static List<Hit> Probe(Transform root,Vector3 feet,Vector3 direction)
        {
            direction.y=0;direction.Normalize();var found=new List<Hit>();
            foreach(float lift in new[]{0f,Step,.7f})
            {
                var bottom=feet+Vector3.up*(Radius+lift);var top=feet+Vector3.up*(Height-Radius+lift);
                foreach(var h in Physics.CapsuleCastAll(bottom,top,Radius,direction,1.25f,~0,QueryTriggerInteraction.Ignore)
                    .Where(x=>x.collider.transform.IsChildOf(root)).OrderBy(x=>x.distance))
                    found.Add(new Hit { collider=h.collider.name,query="capsule forward lifted "+lift,point=h.point,normal=h.normal,distance=h.distance,triangle=h.triangleIndex });
            }
            foreach(float advance in new[]{0f,.3f,.6f,1f})
                foreach(var h in Physics.RaycastAll(feet+direction*advance+Vector3.up*2,Vector3.down,4,~0,QueryTriggerInteraction.Ignore)
                    .Where(x=>x.collider.transform.IsChildOf(root)).OrderBy(x=>x.distance))
                    found.Add(new Hit { collider=h.collider.name,query="all vertical surfaces forward "+advance,point=h.point,normal=h.normal,distance=h.distance,triangle=h.triangleIndex });
            return found;
        }
    }
}
