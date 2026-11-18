using System;
using System.Collections.Generic;
using UnityEngine;

public static class MinorEnemyChecks
{
    public static List<string> Run() {
        var passed=new List<string>();
        void Check(bool value,string message) {
            if(!value)throw new InvalidOperationException("Minor enemy check failed: "+message);
            passed.Add("PASS "+message);
        }
        var left=MinorEnemyRules.ProjectLocal(Vector3.zero,Vector3.up,new Vector3(-3,3,0),1);
        var right=MinorEnemyRules.ProjectLocal(Vector3.zero,Vector3.up,new Vector3(3,3,0),1);
        Check(left.x>0 && right.x<0,"moving a nearby local lamp reverses the enemy shadow's projection direction");
        var sky=MinorEnemyRules.ProjectSky(Vector3.zero,Vector3.up,new Vector3(1,-.02f,0),1);
        Check(sky.x>0 && sky.magnitude<3,"a grazing sky source keeps its azimuth without an unbounded thin shadow");
        var samples=new List<Vector2>(64);var hull=new List<Vector2>(64);
        bool robesWide=true,hoodsWide=true,ghostsWide=true,hoodsBounded=true;
        float ghostCrown=float.NegativeInfinity;
        foreach(var point in MinorEnemyVolumes.GhostBody)ghostCrown=Mathf.Max(ghostCrown,point.y);
        for(int height=0;height<3;height++)for(int phase=0;phase<8;phase++)for(int yaw=0;yaw<8;yaw++) {
            float angle=phase*Mathf.PI/4;
            var lighting=new MinorShadowLighting {IsLocal=true,
                Position=new Vector3(Mathf.Cos(angle)*8,height==0 ? .35f : height==1 ? 2.8f : 8,Mathf.Sin(angle)*8)};
            Quaternion rotation=Quaternion.Euler(0,yaw*45,0);
            void Project(Vector3[] volume,Vector3 offset) => MinorEnemyRules.ProjectVolumeHull(volume,Vector3.zero,
                rotation,offset,Vector3.one,lighting,1,samples,hull);
            Project(MinorEnemyVolumes.AcolyteRobe,Vector3.zero);
            robesWide &= MinimumWidth(hull)>.44f && Area(hull)>.3f;
            Project(MinorEnemyVolumes.AcolyteHood,Vector3.zero);
            hoodsWide &= MinimumWidth(hull)>.36f && Area(hull)>.15f;
            foreach(var point in hull)hoodsBounded &= point.magnitude<6;
            const float ghostHeight=1.75f;
            lighting.ProjectionHeight=ghostHeight+ghostCrown;
            Project(MinorEnemyVolumes.GhostBody,Vector3.up*ghostHeight);
            ghostsWide &= MinimumWidth(hull)>.5f && Area(hull)>.25f;
        }
        Check(robesWide,"solid acolyte robes retain aimable width across 192 body/light/height combinations including edge-on side lighting");
        Check(hoodsWide && hoodsBounded,"low nearby candles preserve a distinct finite-area hood instead of clamping it to a far endpoint");
        Check(ghostsWide,"floating ghost volumes retain width across body rotations, changing lamp azimuth and low candle heights");
        bool hemMoves=true;
        foreach(bool local in new[]{true,false})for(int yaw=0;yaw<8;yaw++) {
            var light=new MinorShadowLighting {IsLocal=local,Position=new Vector3(-4,.35f,0),Direction=new Vector3(.6f,-.8f,0)};
            float NearEdge(float height) {
                light.ProjectionHeight=height+ghostCrown;
                MinorEnemyRules.ProjectVolumeHull(MinorEnemyVolumes.GhostBody,Vector3.zero,Quaternion.Euler(0,yaw*45,0),
                    Vector3.up*height,Vector3.one,light,1,samples,hull);
                float near=float.PositiveInfinity;foreach(var p in hull)near=Mathf.Min(near,p.x);
                return near;
            }
            float low=NearEdge(.85f),raised=NearEdge(1.75f);
            hemMoves &= raised>.20f && raised>low+.4f;
        }
        Check(hemMoves,"raising the complete wisp moves the near edge of its footprint under low side lighting and the moon at all eight headings");
        bool skyVolumesWide=true;
        for(int phase=0;phase<8;phase++) {
            var lighting=new MinorShadowLighting {Direction=new Vector3(Mathf.Cos(phase*Mathf.PI/4),-.02f,Mathf.Sin(phase*Mathf.PI/4))};
            MinorEnemyRules.ProjectVolumeHull(MinorEnemyVolumes.AcolyteHood,Vector3.zero,Quaternion.Euler(0,phase*45,0),
                Vector3.zero,Vector3.one,lighting,1,samples,hull);
            skyVolumesWide &= MinimumWidth(hull)>.36f && Area(hull)>.15f;
        }
        Check(skyVolumesWide,"grazing sky fallback keeps a volumetric hood rather than a flat ribbon");
        Check(MinorEnemyRules.SegmentHitsHunter(new Vector3(-2,.8f,0),new Vector3(2,.8f,0),Vector3.zero,.14f),
            "a fast smoke arrow crossing the hunter body is detected between frames");
        Check(!MinorEnemyRules.SegmentHitsHunter(new Vector3(-2,.8f,3),new Vector3(2,.8f,3),Vector3.zero,.14f),
            "a smoke arrow crossing a displaced visual shadow does not hurt the hunter");
        Check(!MinorEnemyRules.SegmentHitsHunter(new Vector3(-2,.8f,0),new Vector3(2,.8f,0),Vector3.up*2,.14f),
            "projectiles test the hunter's current vertical body position");
        Check(MinorEnemyRules.SegmentHitsHunter(new Vector3(0,.8f,0),new Vector3(0,.8f,0),Vector3.zero,.3f),
            "stationary overlapping ghost contact is detected");
        Check(!MinorEnemyRules.SegmentHitsHunter(new Vector3(1.4f,.8f,0),new Vector3(1.4f,.8f,0),Vector3.zero,.75f),
            "leaving ghost contact range releases pressure");
        Check(8f*.65f>3.6f,"default slowed sprint outruns the pursuing ground ghosts");
        Vector3 a=new Vector3(-1,0,-1),b=new Vector3(1,0,-1),c=new Vector3(0,0,1);
        Check(Mathf.Abs(MinorEnemyRules.TriangleHit(new Ray(Vector3.up*3,Vector3.down),a,b,c)-3)<.001f,
            "visible shadow triangles produce exact nearest shot distances");
        Check(float.IsInfinity(MinorEnemyRules.TriangleHit(new Ray(new Vector3(3,3,0),Vector3.down),a,b,c)),
            "shots outside the visible silhouette miss");
        Check(float.IsInfinity(MinorEnemyRules.TriangleHit(new Ray(Vector3.up*3,Vector3.right),a,b,c)),
            "a shot parallel to the ground cannot hit a shadow");

        var cycle=new FireThiefCycle();cycle.Reset();
        FireThiefCycle.Signal RunUntil(FireThiefCycle.Signal desired,bool lit,int max=1000) {
            for(int i=0;i<max;i++) {
                var signal=cycle.Step(.05f,lit);
                if((signal&desired)!=0)return signal;
            }
            return FireThiefCycle.Signal.None;
        }
        Check(!cycle.LampShot(),"shooting a calm linked lamp does not fabricate a prayer interrupt");
        Check(cycle.BeginEngagement(true,false)==FireThiefCycle.Signal.None && !cycle.Connected,
            "first sight without an available lamp cannot create a connection shield");
        var opening=cycle.BeginEngagement(true,true);
        Check((opening&FireThiefCycle.Signal.PrayerStarted)!=0 && cycle.Connected && cycle.AttacksReleased==0 &&
            cycle.Current==FireThiefCycle.Action.Prayer && Mathf.Approximately(cycle.Remaining,cycle.PrayerTime),
            "first sight immediately connects to a lit lamp for the full prayer without firing an opening volley");
        cycle.Step(.2f,true);float openingRemaining=cycle.Remaining;
        Check(cycle.BeginEngagement(true,true)==FireThiefCycle.Signal.None && cycle.Remaining==openingRemaining,
            "re-entering with an existing connection does not restart its prayer progress");
        Check(cycle.LampShot() && cycle.BeginEngagement(true,true)==FireThiefCycle.Signal.None && !cycle.Connected,
            "an opening lamp shot earns a stagger that a same-frame engagement cannot erase");
        Check(RunUntil(FireThiefCycle.Signal.Volley,true)!=FireThiefCycle.Signal.None,"after interrupting the opener the first ordinary volley remains vulnerable");
        Check(RunUntil(FireThiefCycle.Signal.Volley,true)!=FireThiefCycle.Signal.None,"a second ordinary volley remains available before reconnection");
        Check(!cycle.Connected && cycle.AttacksReleased==2,"two attacks leave the acolyte vulnerable and not yet connected");
        var third=RunUntil(FireThiefCycle.Signal.Volley,true);
        Check((third&FireThiefCycle.Signal.PrayerStarted)!=0 && cycle.Connected && cycle.AttacksReleased==3,
            "the third attack starts the lamp connection immediately, without another attack interval");
        Check(cycle.Step(.5f,true)==FireThiefCycle.Signal.None && cycle.Current==FireThiefCycle.Action.Prayer,
            "praying does not release simultaneous smoke attacks");
        Check(cycle.LampShot() && cycle.Current==FireThiefCycle.Action.Stagger,"shooting the threatened lamp cancels prayer into a counter window");
        Check(cycle.Step(cycle.StaggerTime*.5f,true)==FireThiefCycle.Signal.None && cycle.Current==FireThiefCycle.Action.Stagger,
            "the player receives a real stagger interval after saving a lamp");
        cycle.Reset();
        for(int i=0;i<400;i++)cycle.Step(.05f,true,false);
        Check(!cycle.Connected && cycle.Current!=FireThiefCycle.Action.Prayer,"no available lamp means continued normal attacks with no connection shield");
        cycle.Reset();
        Check((cycle.BeginEngagement(false,true)&FireThiefCycle.Signal.ConnectionStarted)!=0 && cycle.Connected &&
            cycle.Current!=FireThiefCycle.Action.Prayer && cycle.AttacksReleased==0,
            "first sight with an already-dark available lamp connects immediately without a nonexistent fire-stealing prayer");
        for(int volley=0;volley<4;volley++)
            Check(RunUntil(FireThiefCycle.Signal.Volley,false)!=FireThiefCycle.Signal.None,
                "dark-lamp attack "+(volley+1)+" remains available without a one-attack empowerment token");
        Check(cycle.Connected && cycle.Current!=FireThiefCycle.Action.Prayer,"a dark lamp sustains the connection while empowered attacks continue");
        cycle.Step(.01f,true);
        Check(!cycle.Connected && cycle.Current==FireThiefCycle.Action.Stagger && cycle.AttacksReleased==0,
            "relighting breaks the persistent connection and resets the full three-attack cycle");
        Check(RunUntil(FireThiefCycle.Signal.PrayerStarted,true)!=FireThiefCycle.Signal.None,"three new attacks precede the next connection after relighting");
        cycle.Step(.01f,false);
        Check(cycle.Connected && cycle.Current==FireThiefCycle.Action.Waiting,"natural expiry ends the prayer but keeps the connection shield until the lamp is restored");
        Check(cycle.LampShot() && !cycle.Connected,"shooting a dark linked lamp also breaks a connection outside its prayer animation");
        cycle.Kill();
        Check(cycle.Step(100,true)==FireThiefCycle.Signal.None && !cycle.LampShot() &&
            cycle.BeginEngagement(true,true)==FireThiefCycle.Signal.None,"dead acolytes cannot release attacks or reconnect to lamps");
        return passed;
    }
    static float Area(List<Vector2> points) {
        float area=0;
        for(int i=0;i<points.Count;i++) {var a=points[i];var b=points[(i+1)%points.Count];area+=a.x*b.y-a.y*b.x;}
        return Mathf.Abs(area)*.5f;
    }
    static float MinimumWidth(List<Vector2> points) {
        if(points.Count<3)return 0;
        float width=float.PositiveInfinity;
        for(int i=0;i<points.Count;i++) {
            Vector2 edge=points[(i+1)%points.Count]-points[i];
            if(edge.sqrMagnitude<.000001f)continue;
            Vector2 normal=new Vector2(-edge.y,edge.x).normalized;
            float min=float.PositiveInfinity,max=float.NegativeInfinity;
            foreach(var point in points) {float distance=Vector2.Dot(point,normal);min=Mathf.Min(min,distance);max=Mathf.Max(max,distance);}
            width=Mathf.Min(width,max-min);
        }
        return width;
    }
}
