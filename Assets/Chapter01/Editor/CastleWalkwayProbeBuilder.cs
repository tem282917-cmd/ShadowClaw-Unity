using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Chapter01.Runtime.Testing;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    public static class CastleWalkwayProbeBuilder
    {
        sealed class Probe
        {
            public Vector3 start,direction,normal;
            public float travel;
            public string prefix,category,scope;
            public Collider collider;
        }
        [Serializable] sealed class ClearanceReport { public List<ClearanceHit> hits=new List<ClearanceHit>(); }
        [Serializable] sealed class ClearanceHit { public Vector3 feet;public string collider; }
        public static void Configure(Scene scene,CastleWalkwayDescriptor d,IReadOnlyList<CastleObstacleCollisionBuilder.Obstacle> architecture)
        {
            var all=CastleWalkwayInstaller.All<Collider>(scene).Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger).ToArray();
            var ground=all.Where(CastleWalkwayInstaller.IsGround).ToArray();
            var hall=CastleWalkwayInstaller.All<IronHallEncounter>(scene).Single();
            var barriers=d.root.GetComponentsInChildren<Collider>().Where(c=>c.name.StartsWith("CW_Obstacle_PlayerBarrier_",StringComparison.Ordinal)).ToArray();
            var probes=new List<Probe>();
            var platforms=CastleWalkwayGeometry.LastPlatforms;
            var preferred=new[]{1,14,23,28,33};
            foreach(int node in preferred)
            {
                var platform=platforms.OrderBy(p=>Mathf.Abs(p.firstNode-node)).First();
                foreach(var collider in barriers.Where(c=>Horizontal(c.bounds.center-platform.pivot).sqrMagnitude<=25*25&&Mathf.Abs(c.bounds.center.y-platform.pivot.y)<=8)
                    .OrderBy(c=>Horizontal(c.bounds.center-platform.pivot).sqrMagnitude))
                {
                    if(probes.Any(p=>p.scope==collider.transform.parent.name))continue;
                    if(TryProbe(collider,"rail",collider.transform.parent.name,"CW_Obstacle_PlayerBarrier_",d,ground,hall,out var p))
                    {probes.Add(p);break;}
                }
            }
            foreach(string category in new[]{"column","building"})
            foreach(var obstacle in architecture.Where(o=>o.category==category&&o.collider&&o.collider.gameObject.layer==0)
                .OrderBy(o=>category=="building"&&(o.sourceName.StartsWith("A01__",StringComparison.Ordinal)||o.sourceName.StartsWith("A04__",StringComparison.Ordinal))?0:1)
                .ThenBy(o=>Horizontal(o.collider.bounds.center-Closest(o.collider.bounds.center,d.path)).sqrMagnitude))
            {
                if(probes.Count(p=>p.category==category)>=2)break;
                if(probes.Any(p=>p.scope==obstacle.sourceName))continue;
                var b=obstacle.collider.bounds;
                if(category=="column"&&Mathf.Min(b.size.x,b.size.z)<.55f)continue;
                string prefix=obstacle.collider.name;
                foreach(string suffix in new[]{"_Solid_","_Section_"}){int cut=prefix.LastIndexOf(suffix,StringComparison.Ordinal);if(cut>=0)prefix=prefix.Substring(0,cut);}
                if(TryProbe(obstacle.collider,category,obstacle.sourceName,prefix,d,ground,hall,out var p))probes.Add(p);
            }
            if(probes.Count(p=>p.category=="rail")<preferred.Length||!probes.Any(p=>p.category=="column")||!probes.Any(p=>p.category=="building"))
                throw new InvalidOperationException("Insufficient reachable physical probes: "+string.Join(",",probes.Select(p=>p.category+":"+p.scope)));
            d.barrierStarts=probes.Select(p=>p.start).ToArray();d.barrierDirections=probes.Select(p=>p.direction).ToArray();
            d.barrierNormals=probes.Select(p=>p.normal).ToArray();d.barrierMaxTravel=probes.Select(p=>p.travel).ToArray();
            d.barrierExpectedColliderPrefixes=probes.Select(p=>p.prefix).ToArray();d.barrierCategories=probes.Select(p=>p.category).ToArray();
            d.barrierScopeIds=probes.Select(p=>p.scope).ToArray();d.requiredBarrierScopes=probes.Where(p=>p.category=="rail").Select(p=>p.scope).Distinct().ToArray();
            ConfigureRays(d,ground,probes.Where(p=>p.category=="rail").ToArray());
            d.cornerChecks=new[]{1,23,33}.Select(node=>{
                var platform=platforms.OrderBy(p=>Mathf.Abs(p.firstNode-node)).First();
                int at=Nearest(platform.pivot,d.path);int start=Mathf.Max(1,at-2),end=Mathf.Min(d.path.Length-2,at+2);
                return new CastleWalkwayDescriptor.CornerRoute{name=platform.groupName,points=d.path.Skip(start).Take(end-start+1).ToArray(),lateralOffset=.45f};
            }).ToArray();
            var photoPoints=new[]{3,14,23,28,33}.Select(node=>platforms.OrderBy(p=>Mathf.Abs(p.firstNode-node)).First().pivot).Concat(new[]{d.entrancePosition}).ToArray();
            d.screenshotPoints=photoPoints.Select(p=>d.path[Nearest(p,d.path)]).Distinct().ToArray();
            d.screenshotLookAt=d.screenshotPoints.Select(p=>{
                int i=Nearest(p,d.path),j=i;float distance=0;
                while(j<d.path.Length-1&&distance<6){distance+=Horizontal(d.path[j+1]-d.path[j]).magnitude;++j;}
                var forward=Horizontal(d.path[j]-p).normalized;if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
                return p+forward*12+Vector3.up*.4f;
            }).ToArray();
            var jump=platforms.OrderBy(p=>Mathf.Abs(p.firstNode-28)).First().pivot;
            d.jumpPoints=new[]{d.path[Nearest(jump,d.path)]};
            WriteClearanceAudit(d,hall,ground);
        }
        static bool TryProbe(Collider collider,string category,string scope,string prefix,CastleWalkwayDescriptor d,Collider[] ground,IronHallEncounter hall,out Probe probe)
        {
            probe=null;var center=collider.bounds.center;var near=Closest(center,d.path);
            var outward=Horizontal(center-near);float distance=outward.magnitude;
            if(distance<1.1f||distance>18)return false;
            outward.Normalize();Vector3 start;
            if(category=="rail")
            {
                try{start=CastleWalkwayInstaller.FloorAt(new Vector3(center.x,near.y,center.z)-outward*1.25f,ground);}catch{return false;}
            }
            else
            {
                if(!collider.Raycast(new Ray(near+Vector3.up*.85f,outward),out var face,distance+4))return false;
                var normal=-Horizontal(face.normal).normalized;if(Vector3.Dot(normal,outward)<.8f)return false;
                outward=normal;
                var foot=face.point-outward*1.25f;foot.y=near.y;
                try{start=CastleWalkwayInstaller.FloorAt(foot,ground);}catch{return false;}
            }
            var battle=hall.BattleArea.bounds;
            if(start.x>=battle.min.x&&start.x<=battle.max.x&&start.z>=battle.min.z-.45f&&start.z<=battle.max.z)return false;
            if(!collider.Raycast(new Ray(start+Vector3.up*.85f,outward),out var boundary,2.6f))return false;
            if(category=="rail")
            {
                // At beveled corners the road-to-rail vector is not the wall's
                // normal. Use the actual face so legal tangential sliding cannot
                // be miscounted as outward penetration of an oblique plane.
                var faceNormal=-Horizontal(boundary.normal).normalized;
                if(Vector3.Dot(faceNormal,outward)<.8f)return false;
                var foot=boundary.point-faceNormal*1.25f;foot.y=start.y;
                try{start=CastleWalkwayInstaller.FloorAt(foot,ground);}catch{return false;}
                outward=faceNormal;
                if(!collider.Raycast(new Ray(start+Vector3.up*.85f,outward),out boundary,2.6f)||Vector3.Dot(-Horizontal(boundary.normal).normalized,outward)<.999f)return false;
                if(start.x>=battle.min.x&&start.x<=battle.max.x&&start.z>=battle.min.z-.45f&&start.z<=battle.max.z)return false;
            }
            float travel=boundary.distance-.35f;if(travel<.3f||travel>1.3f)return false;
            if(category=="rail")foreach(float side in new[]{-4f,-2f,2f,4f})
            {
                // Two seconds of a 5 m/s diagonal push can slide more than 3m.
                // Keep that test on one long face. Turning onto an adjoining
                // stair rail is legal movement, not penetration of this plane.
                Vector3 sideFeet;
                try{sideFeet=CastleWalkwayInstaller.FloorAt(start+Vector3.Cross(Vector3.up,outward)*side,ground);}catch{return false;}
                if(!collider.Raycast(new Ray(sideFeet+Vector3.up*.85f,outward),out var sideHit,2.6f)||
                    Mathf.Abs(sideHit.distance-boundary.distance)>.03f||Vector3.Dot(sideHit.normal,boundary.normal)<.999f)return false;
            }
            // A finite arch fragment can legitimately be rounded by a capsule.
            // Only use a broad, continuous wall face for a sustained normal push.
            if(category=="building")foreach(float side in new[]{-.6f,.6f})
            {
                var origin=start+Vector3.up*.85f+Vector3.Cross(Vector3.up,outward)*side;
                if(!collider.Raycast(new Ray(origin,outward),out var adjacent,2.6f)||Mathf.Abs(adjacent.distance-boundary.distance)>.08f||Vector3.Dot(adjacent.normal,boundary.normal)<.99f)return false;
            }
            Vector3 routePoint=Closest(start,d.path);
            if(!ClearApproach(routePoint,start,ground))return false;
            probe=new Probe{start=start,normal=outward,direction=category=="rail"?(outward+Vector3.Cross(Vector3.up,outward)*.35f).normalized:outward,
                travel=travel,prefix=prefix,category=category,scope=scope,collider=collider};
            return true;
        }
        static bool ClearApproach(Vector3 from,Vector3 to,Collider[] ground)
        {
            float length=Horizontal(to-from).magnitude;
            for(float t=0;t<=1;t+=1/Mathf.Max(2,Mathf.Ceil(length/.4f)))
            {try{CastleWalkwayInstaller.FloorAt(Vector3.Lerp(from,to,t),ground);}catch{return false;}}
            Vector3 delta=to-from;if(delta.magnitude<.01f)return true;
            var hits=Physics.CapsuleCastAll(from+Vector3.up*.38f,from+Vector3.up*1.36f,.32f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
            return !hits.Any(h=>!CastleWalkwayInstaller.IsGround(h.collider)&&h.collider.bounds.max.y>from.y+.4f&&h.distance<delta.magnitude-.08f);
        }
        static void ConfigureRays(CastleWalkwayDescriptor d,Collider[] ground,Probe[] rails)
        {
            var a=new List<Vector3>();var b=new List<Vector3>();var hits=new List<bool>();var names=new List<string>();
            foreach(var p in rails)
            {
                if(hits.Count(x=>!x)>=2)break;
                foreach(float offset in new[]{0,.25f,-.25f,.5f,-.5f,.75f})
                {
                    if(hits.Count(x=>!x)>=2)break;
                    foreach(float height in new[]{.65f,.82f,1f})
                    {
                        Vector3 feet;try{feet=CastleWalkwayInstaller.FloorAt(p.start+Vector3.Cross(Vector3.up,p.normal)*offset,ground);}catch{continue;}
                        if(!p.collider.Raycast(new Ray(feet+Vector3.up*.8f,p.normal),out var boundary,3))continue;
                        var eye=feet+Vector3.up*1.65f;var aim=feet+p.normal*boundary.distance+Vector3.up*height;
                        var end=eye+(aim-eye)*1.25f;bool clear=true;
                        foreach(float x in new[]{-.09f,0,.09f})foreach(float y in new[]{-.06f,0,.06f})
                        {var origin=eye+Vector3.Cross(Vector3.up,p.normal)*x+Vector3.up*y;var delta=end-origin;
                            if(Physics.Raycast(origin,delta.normalized,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide))clear=false;}
                        var v=end-eye;
                        if(clear&&Physics.Raycast(eye,v.normalized,v.magnitude,1<<2,QueryTriggerInteraction.Ignore))
                        {a.Add(eye);b.Add(end);hits.Add(false);names.Add("");break;}
                    }
                }
            }
            foreach(var c in d.root.GetComponentsInChildren<Collider>().Where(c=>c.name.StartsWith("CW_Obstacle_RailPost_",StringComparison.Ordinal)))
            {
                if(hits.Count(x=>x)>=2)break;
                var center=c.bounds.center;var near=Closest(center,d.path);var normal=Horizontal(center-near).normalized;
                if(normal.sqrMagnitude<.5f)continue;Vector3 feet;
                try{feet=CastleWalkwayInstaller.FloorAt(new Vector3(center.x,near.y,center.z)-normal*1.3f,ground);}catch{continue;}
                if(!ClearApproach(Closest(feet,d.path),feet,ground))continue;
                var eye=feet+Vector3.up*1.65f;var end=eye+(center-eye)*1.25f;var delta=end-eye;
                if(Physics.Raycast(eye,delta.normalized,out var hit,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)&&hit.collider==c)
                {a.Add(eye);b.Add(end);hits.Add(true);names.Add(c.name);}
            }
            if(hits.Count(x=>x)<2||hits.Count(x=>!x)<2)throw new InvalidOperationException("Cannot author two real-eye solid and open-slot ray probes.");
            d.rayStarts=a.ToArray();d.rayEnds=b.ToArray();d.rayShouldHit=hits.ToArray();d.rayExpectedColliderNames=names.ToArray();
        }
        static void WriteClearanceAudit(CastleWalkwayDescriptor d,IronHallEncounter hall,Collider[] ground)
        {
            var report=new ClearanceReport();
            for(int i=1;i<d.path.Length;i++)
            {
                float length=Horizontal(d.path[i]-d.path[i-1]).magnitude;int count=Mathf.Max(1,Mathf.CeilToInt(length/.5f));
                for(int step=1;step<=count;step++)
                {
                    var p=Vector3.Lerp(d.path[i-1],d.path[i],step/(float)count);
                    foreach(var c in Physics.OverlapCapsule(p+Vector3.up*.4f,p+Vector3.up*1.35f,.31f,~0,QueryTriggerInteraction.Ignore))
                        if(c.name.StartsWith("CW_Obstacle_",StringComparison.Ordinal)&&c.bounds.max.y>p.y+.4f)
                            report.hits.Add(new ClearanceHit{feet=p,collider=c.name});
                }
            }
            File.WriteAllText(Path.Combine(CastleWalkwayInstaller.Output,"route-clearance.json"),JsonUtility.ToJson(report,true));
            if(report.hits.Count>0)Debug.LogWarning("CASTLE_WALKWAY_CLEARANCE_REVIEW count="+report.hits.Count+" first="+report.hits[0].collider+" at "+report.hits[0].feet);
            var inside=d.entrancePosition;inside.z=hall.BattleArea.bounds.min.z+1;
            for(float t=0;t<=1;t+=.1f)
            {
                var p=CastleWalkwayInstaller.FloorAt(Vector3.Lerp(d.entrancePosition,inside,t),ground);
                var blocked=Physics.OverlapCapsule(p+Vector3.up*.4f,p+Vector3.up*1.35f,.31f,~0,QueryTriggerInteraction.Ignore)
                    .Where(c=>c.name.StartsWith("CW_Obstacle_",StringComparison.Ordinal)&&c.bounds.max.y>p.y+.4f).ToArray();
                if(blocked.Length>0)throw new InvalidOperationException("Normal battle entrance obstructed at "+p+" by "+string.Join(",",blocked.Select(c=>c.name)));
            }
        }
        static Vector3 Horizontal(Vector3 v)=>new Vector3(v.x,0,v.z);
        static int Nearest(Vector3 p,Vector3[] path)
        {int found=0;float best=float.PositiveInfinity;for(int i=0;i<path.Length;i++){float d=Horizontal(path[i]-p).sqrMagnitude;if(d<best){found=i;best=d;}}return found;}
        static Vector3 Closest(Vector3 p,Vector3[] path)
        {
            float best=float.PositiveInfinity;var found=path[0];
            for(int i=1;i<path.Length;i++){var delta=Horizontal(path[i]-path[i-1]);float t=Mathf.Clamp01(Vector3.Dot(Horizontal(p-path[i-1]),delta)/Mathf.Max(.0001f,delta.sqrMagnitude));
                var point=Vector3.Lerp(path[i-1],path[i],t);float distance=Horizontal(p-point).sqrMagnitude;if(distance<best){best=distance;found=point;}}
            return found;
        }
    }
}
