using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public sealed partial class ShadowLab {
    [Serializable] sealed class FinalFeedbackReport {
        public string result,error,gpu;
        public List<string> checks=new List<string>();
        public int width,height,groups,ghosts,hiddenExterior,cameraFrames,frames,overBudget,over16,areaStrikes;
        public float exteriorClip,roomClip,medianMs,p95Ms,p99Ms,worstMs;
        public Vector3 hallCenter;
        public float hallRadius;
        public int retainedMeshes;
        public int mountainMeshes,visibleMountainPixels;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartFinalFeedbackCheck() {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-final-feedback")<0)return;
        var lab=FindFirstObjectByType<ShadowLab>();
        Application.runInBackground=true;lab.StartCoroutine(lab.CheckFinalFeedback());
    }
    IEnumerator CheckFinalFeedback() {
        var args=Environment.GetCommandLineArgs();
        var path=Path.Combine(args[Array.IndexOf(args,"-chapter01-evidence")+1],"final-feedback.json");
        var report=new FinalFeedbackReport {gpu=SystemInfo.graphicsDeviceName};
        int rendered=0;
        Camera.CameraCallback counter=c=>{if(c==Eye)rendered++;};
        void Check(bool value,string label) { if(!value)throw new InvalidOperationException(label);report.checks.Add(label); }
        void Cross() {
            for(int i=0;i<180 && !EncounterStarted;i++){Walk(hall.FogGate.Inward,1f/60,true);Tick(1f/60);}
            Check(EncounterStarted && hall.FogGate.Sealed && FinalRoomIsolated,"walking through the white gate starts and seals the encounter");
        }
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        for(int i=0;i<6;i++)yield return null;
        enabled=false;
        try {
            RestartEncounter();
            const BindingFlags priv=BindingFlags.Instance|BindingFlags.NonPublic;
            UI.GetType().GetField("sessionStarted",priv).SetValue(UI,true);
            UI.GetType().GetMethod("SetView",priv).Invoke(UI,new object[]{"battle",null});
            Developer.ClearPauseReasons();
            var groups=authoredActors.OfType<GroundShadowSwarm>().ToArray();
            report.groups=groups.Length;report.ghosts=groups.Sum(g=>g.LivingCount);
            Check(report.groups>0 && groups.All(g=>g.LivingCount==4),"all authored swarms default to four living ghosts");
            Check(hall.FogGate.GetComponentsInChildren<ParticleSystem>(true).Length==0 &&
                hall.FogGate.GetComponentsInChildren<MeshRenderer>().Length==1,"white entrance renders one mesh with no simulated fog particles");
            report.exteriorClip=Eye.farClipPlane;
            var checkActor=groups[0];
            state.Player=PlaceBody(playerBody,checkActor.transform.position+Vector3.forward*2);
            checkActor.Tick(.016f);
            var ink=checkActor.GetComponentsInChildren<MeshFilter>().First(m=>m.name.StartsWith("Shootable ground shadow") && m.sharedMesh.vertexCount>0);
            var indices=ink.sharedMesh.triangles;var vertices=ink.sharedMesh.vertices;
            var spot=ink.transform.TransformPoint((vertices[indices[0]]+vertices[indices[1]]+vertices[indices[2]])/3);
            var arrow=new Ray(spot+Vector3.up,Vector3.down);
            Check(checkActor.TryHitShadow(arrow,2,out float hit),"a shot hits the enlarged ghost shadow");
            checkActor.ReceiveShadowHit(Bolt.Holy,arrow.GetPoint(hit));
            Check(checkActor.LivingCount==3,"one ghost dies from the shadow shot");
            state.Player=PlaceBody(playerBody,OutsideGate());yaw=hall.FogGate.transform.eulerAngles.y;
            HasGuardianRelic=true;GuardianRelicCharges=2;GuardianRelicRelights=1;holyKnives.Reset(3);
            var gate=hall.FogGate;
            for(int i=0;i<2;i++) {
                Cross();report.hiddenExterior=HiddenExteriorCount;report.roomClip=Eye.farClipPlane;
                Check(HiddenExteriorCount>0 && report.roomClip==report.exteriorClip && hiddenForFinalRoom.All(r=>r.forceRenderingOff),"exterior meshes are suppressed without shortening the camera range");
                boss.HunterHealthLocked=false;DamageHunter(12);
                Check(HurtIntensity>0 && hunterHitClip!=null && hurtSoundCooldown>0,"real damage triggers the visual pulse and the imported impact sound");
                holyKnives.Spend();UseGuardianRelic();
                DamageHunter(1000);Tick(.4f);
                Check(!Finished && HunterHealth==HuntBoss.HunterMaxHealth && !EncounterStarted && !gate.Sealed &&
                    !gate.IsInside(state.Player) && Vector3.Distance(gate.FloorCenter,state.Player)<5,"fatal damage revives the hunter outside the open fog gate");
                Check(!FinalRoomIsolated && HiddenExteriorCount==0 && Eye.farClipPlane==report.exteriorClip &&
                    checkActor.LivingCount==3,"exterior rendering and the previously defeated ghost state survive the retry");
                Check(HasGuardianRelic && GuardianRelicCharges==2 && GuardianRelicRelights==1 && HolyKnifeCount==3 &&
                    combat.Damage==0 && UI.CurrentView=="battle","the retry restores entry inventory and a fresh boss without opening a defeat menu");
            }
            Cross();boss.HunterHealthLocked=true;
            CheckFinalHallIntegrity(report,Path.GetDirectoryName(path),Check);
            boss.StrikeReleased+=(move,from,to)=> {
                if(move==HuntBoss.Move.Burst || move==HuntBoss.Move.Rift || move==HuntBoss.Move.SleeveSweep || move==HuntBoss.Move.BellWaves)
                    report.areaStrikes++;
            };
            bool savedReduced=UI.Settings.ReducedMotion;UI.Settings.ReducedMotion=true;ShakeOnImpact();
            Check(impactShake==0,"reduced motion suppresses camera shake");
            UI.Settings.ReducedMotion=false;ShakeOnImpact();Tick(.03f);
            Check(Mathf.Abs(CameraImpactRoll)>0 && Mathf.Abs(CameraImpactRoll)<1,"impact shake is transient and stays below one degree");
            UI.Settings.ReducedMotion=savedReduced;
            var direction=state.Enemy-state.Player;yaw=Quaternion.LookRotation(direction).eulerAngles.y;pitch=0;
        } catch(Exception error) { report.error=error.ToString(); }
        if(report.error==null) {
            Camera.onPostRender+=counter;
            var ms=new List<float>(8192);double elapsed=0;
            long previous=Stopwatch.GetTimestamp();
            int warm=90,baseline=0;
            while(elapsed<20) {
                long now=Stopwatch.GetTimestamp();
                if(warm==0){float delta=(float)((now-previous)*1000d/Stopwatch.Frequency);ms.Add(delta);elapsed+=delta/1000d;}
                previous=now;
                Tick(Time.deltaTime);
                if(warm>0 && --warm==0)baseline=rendered+1;
                yield return null;
            }
            Camera.onPostRender-=counter;
            report.cameraFrames=rendered-baseline;report.frames=ms.Count;
            ms.Sort();report.width=Screen.width;report.height=Screen.height;
            report.medianMs=ms[ms.Count/2];report.p95Ms=ms[(int)(.95*ms.Count)];
            report.p99Ms=ms[(int)(.99*ms.Count)];report.worstMs=ms[ms.Count-1];
            report.overBudget=ms.Count(t=>t>1000f/150);report.over16=ms.Count(t=>t>1000f/60);
            if(report.cameraFrames!=report.frames)report.error="Missing rendered camera frames";
        }
        report.result=report.error==null?"PASS":"FAIL";
        File.WriteAllText(path,JsonUtility.ToJson(report,true));Application.Quit(report.error==null?0:1);
    }

    void CheckFinalHallIntegrity(FinalFeedbackReport report,string folder,Action<bool,string> check) {
        Vector3 entry=state.Player;
        report.hallCenter=roomEnvelope.Center;report.hallRadius=roomEnvelope.Radius;
        var nave=hall.GroundColliders.Single(c=>c && c.name=="Collision__Citadel_Nave_Floor");
        check(Mathf.Abs(report.hallCenter.y-(nave.bounds.max.y-1))<.01f,"hemisphere equator is one metre under the nave floor");
        foreach(var mesh in exteriorRenderers) {
            if(!mesh || !mesh.enabled || !mesh.gameObject.activeInHierarchy)continue;
            Bounds b=mesh.bounds;
            if(b.max.y<report.hallCenter.y)continue;
            Vector3 nearest=b.ClosestPoint(report.hallCenter);
            if(Vector3.Distance(nearest,report.hallCenter)>report.hallRadius)continue;
            if(mesh.forceRenderingOff)throw new InvalidOperationException("Missing hemisphere mesh: "+mesh.name);
            report.retainedMeshes++;
        }
        check(report.retainedMeshes>0,"every active scene mesh intersecting the upper hemisphere retains its renderer");
        var mountains=FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled &&
            (r.name.Contains("__Far_Ridge_") || r.name.Contains("__East_Backdrop") || r.name.Contains("__North_Backdrop"))).ToArray();
        report.mountainMeshes=mountains.Length;
        check(mountains.Length>0 && mountains.All(r=>!r.forceRenderingOff) && mountains.Any(r=>!roomEnvelope.Keeps(r.bounds)),
            "the authored distant ridges and background mountains remain enabled beyond the room hemisphere");
        foreach(string name in new[]{"A14__Citadel_Steep_Nave_Roof__0","B02__Central_Crown__0","B07__Polygonal_Throne_Apse__0",
            "D11__Throne_Assembly__0","D13__Crown_Chandelier__0","A14__Citadel_Transept_Roof_Mesh_-1__0","A14__Citadel_Transept_Roof_Mesh_1__0"}) {
            var r=exteriorRenderers.Single(m=>m && m.name==name);
            check(r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff && roomEnvelope.Keeps(r.bounds),"visible boss-room landmark: "+name);
        }
        void Push(Vector3 start,Vector3 direction,float boundary,string label) {
            state.Player=PlaceBody(playerBody,start);
            for(int i=0;i<100;i++)Walk(direction,.02f,true);
            check(Vector3.Dot(state.Player,direction)<=boundary+.05f,"player capsule stays inside "+label);
        }
        Push(new Vector3(13,68,109),Vector3.right,18,"east nave wall");
        Push(new Vector3(-13,68,109),Vector3.left,18,"west nave wall");
        foreach(int sign in new[]{-1,1}) {
            Vector3 outward=Vector3.right*sign;
            Push(new Vector3(15*sign,68,124),outward,32,"outer transept wall "+sign);
            check(Vector3.Dot(state.Player,outward)>19,"transept entrance stays traversable "+sign);
            Push(new Vector3(6*sign,68,85),Vector3.back,-hall.FogGate.FloorCenter.z,"porch shoulder "+sign);
        }
        Push(new Vector3(0,71,160),Vector3.forward,167,"throne apse");
        // Render using the real first-person camera with the combat isolation active.
        var old=Eye.targetTexture;var active=RenderTexture.active;
        var rt=RenderTexture.GetTemporary(960,540,24);
        var image=new Texture2D(960,540,TextureFormat.RGB24,false);
        void View(string label,Vector3 at,Vector3 target) {
            Eye.transform.SetPositionAndRotation(at,Quaternion.LookRotation(target-at));
            Eye.targetTexture=rt;Eye.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
            if(label!=null)File.WriteAllBytes(Path.Combine(folder,"hall-"+label+".png"),image.EncodeToPNG());
        }
        try {
            View("entry",new Vector3(0,69.7f,86),new Vector3(0,75,150));
            View("vault",new Vector3(2,69.7f,110),new Vector3(0,102,125));
            View("throne",new Vector3(2,69.7f,133),new Vector3(0,77,157));
            View("west",new Vector3(0,69.7f,124),new Vector3(-32,79,124));
            View("east",new Vector3(0,69.7f,124),new Vector3(32,79,124));
            // Compare the same gameplay-camera frame with and without the mountain
            // silhouettes. Keep the hall and all foreground occluders in both passes.
            var gate=hall.FogGate;
            foreach(int side in new[]{-1,1}) {
                Vector3 at=gate.FloorCenter+gate.Inward*(gate.EntryClearance+.25f)+gate.transform.right*(side*3)+Vector3.up*1.65f;
                Vector3 target=at+gate.transform.right*(side*18)-gate.Inward*10+Vector3.up*2;
                View("mountains-"+side,at,target);
                var withRidges=image.GetPixels32();
                try {
                    foreach(var mountain in mountains)mountain.forceRenderingOff=true;
                    View(null,at,target);
                    var without=image.GetPixels32();
                    int changed=0;
                    for(int p=0;p<without.Length;p++) {
                        var a=withRidges[p];var b=without[p];
                        if(Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)>12)changed++;
                    }
                    report.visibleMountainPixels=Mathf.Max(changed,report.visibleMountainPixels);
                } finally {foreach(var mountain in mountains)mountain.forceRenderingOff=false;}
            }
            check(report.visibleMountainPixels>100,"background ridges contribute visible silhouette pixels from inside the sealed doorway");
        } finally {
            Eye.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Destroy(image);
            state.Player=PlaceBody(playerBody,entry);Sync();
        }
    }
}
