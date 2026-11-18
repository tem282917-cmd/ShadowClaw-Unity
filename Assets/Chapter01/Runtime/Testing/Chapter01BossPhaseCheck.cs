using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Chapter01.Runtime.Testing {
    public static class Chapter01BossPhaseCheck {
        [Serializable] sealed class Report {
            public string result,failure,gpu;
            public List<string> passed=new List<string>();
            public double blackGateMs,greyGateMs,emptyMs;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start() {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-check-boss-phase-exit")<0)return;
            var lab=Object.FindFirstObjectByType<ShadowLab>();
            lab.StartCoroutine(Run(lab,args[Array.IndexOf(args,"-chapter01-evidence")+1]));
        }
        static IEnumerator Run(ShadowLab lab,string folder) {
            for(int i=0;i<5;i++)yield return null;
            var report=new Report {gpu=SystemInfo.graphicsDeviceName};
            try {
                lab.RestartEncounter();
                lab.enabled=false;
                report.passed.AddRange(ClassicBossChecks.Run());
                report.passed.AddRange(GrandBossChecks.Run());
                report.passed.AddRange(FixedGuardianChecks.Run());
                TestExit(lab,report,folder);
                TestFinalRoom(lab,report);
                report.result="PASS";
            } catch(Exception ex) {
                report.result="FAIL";report.failure=ex.ToString();
                UnityEngine.Debug.LogException(ex);
            }
            File.WriteAllText(Path.Combine(folder,"boss-phase-exit.json"),JsonUtility.ToJson(report,true));
            Application.Quit(report.result=="PASS"?0:1);
        }

        static void TestFinalRoom(ShadowLab lab,Report report) {
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            lab.UI.GetType().GetField("sessionStarted",flags).SetValue(lab.UI,true);
            lab.UI.GetType().GetMethod("SetView",flags).Invoke(lab.UI,new object[]{"battle",null});
            lab.Developer.ClearPauseReasons();
            lab.Eye.GetComponent<ShiyinAudio>().SetPaused(false);
            var tick=typeof(ShadowLab).GetMethod("Tick",flags);
            void Step()=>tick.Invoke(lab,new object[]{.04f});
            var hall=lab.GetComponent<IronHallEncounter>();
            var body=lab.transform.Find("Hunter capsule").GetComponent<CharacterController>();
            Vector3 entry=hall.FogGate.FloorCenter+hall.FogGate.Inward*2.5f;
            entry.y=hall.SurfaceHeight(entry.x,entry.z)+.08f;
            body.enabled=false;body.transform.position=entry;body.enabled=true;body.Move(Vector3.down*.1f);
            var state=lab.ShadowState;state.Player=body.transform.position;Step();
            if(!lab.EncounterStarted)throw new InvalidOperationException("final room entry");
            var ink=state.Witch.Body;
            Vector3 light=state.Lamp;
            state.SetLamp(state.LightId,state.Enemy+new Vector3(30,4,0));ink.Update(state);
            Vector2 torso=ink.Rig.TorsoCenter;
            state.SetLamp(state.LightId,state.Enemy+new Vector3(60,4,0));ink.Update(state);
            if(Vector2.Distance(torso,ink.Rig.TorsoCenter)>.1f || !ink.Contains(ink.Aim))
                throw new InvalidOperationException($"shallow-light torso drift {Vector2.Distance(torso,ink.Rig.TorsoCenter)}, aim {ink.Aim} hit {ink.Contains(ink.Aim)}, feet {state.Enemy}");
            state.SetLamp(state.LightId,light);ink.Update(state);
            report.passed.Add("doubling the lateral light distance at grazing incidence caps torso travel and retains a hittable aim");
            var combat=(WallCombat)typeof(ShadowLab).GetField("combat",flags).GetValue(lab);
            for(int i=0;i<6;i++)combat.Shoot(Bolt.Holy,ink.Aim,true,true);
            var audio=lab.Eye.GetComponent<ShiyinAudio>();
            int bells=audio.DemonSoundCount(DemonSound.Bell);
            bool tell=false,first=false,second=false;
            var rings=lab.GetComponentsInChildren<LineRenderer>(true).Where(r=>r.name.StartsWith("Bell wave collision boundary")).ToArray();
            for(int i=0;i<160 && !second;i++) {
                Step();tell|=lab.BossAction==HuntBoss.Move.BellTell;
                if(lab.BossAction!=HuntBoss.Move.BellWaves)continue;
                first|=rings.Any(r=>r.name.EndsWith("1") && r.enabled);
                second|=rings.Any(r=>r.name.EndsWith("2") && r.enabled);
            }
            if(!tell || !first || !second || audio.DemonSoundCount(DemonSound.Bell)!=bells+2)
                throw new InvalidOperationException("missing final-room warning, visible rings or bell cues");
            report.passed.Add("six holy hits on the live damage counter trigger the two visible rings and both release sounds in the final room");
        }

        static void TestExit(ShadowLab lab,Report report,string folder) {
            void Check(bool ok,string msg) {
                if(!ok)throw new InvalidOperationException(msg);
                report.passed.Add(msg);
            }
            var guardian=Object.FindFirstObjectByType<FixedLanternGuardian>();
            var gate=guardian.ExitSeal;
            Check(gate!=null && gate.activeInHierarchy && guardian.Alive,"living chapel guardian seals the authored exit before engagement");
            var barrier=gate.GetComponent<BoxCollider>();
            var renderer=gate.GetComponent<MeshRenderer>();
            Check(!barrier.isTrigger && renderer.enabled && renderer.sharedMaterial.shader.isSupported &&
                renderer.sharedMaterial.passCount==1 && gate.GetComponent<MeshFilter>().sharedMesh.triangles.Length==6 &&
                gate.GetComponentsInChildren<ParticleSystem>().Length==0 && gate.GetComponentsInChildren<Light>().Length==0,
                "black curtain has one two-triangle pass, a solid collider, zero particle emitters and zero lights");
            var arch=GameObject.Find("CW_Obstacle_Authored_A09__Chapel_North_Portal_Merged").GetComponent<MeshCollider>().bounds;
            var plane=barrier.bounds;
            Check(Mathf.Abs(plane.center.x-arch.center.x)<1f && Mathf.Abs(plane.center.z-arch.center.z)<1f,
                "black curtain is centred inside the existing northern stone arch");
            Vector3 foot=new Vector3(plane.center.x,plane.min.y+.3f,plane.center.z-2);
            var body=lab.transform.Find("Hunter capsule").GetComponent<CharacterController>();
            float Cross(float x) {
                body.enabled=false;body.transform.position=foot+Vector3.right*x;body.enabled=true;
                for(int i=0;i<32;i++)body.Move(Vector3.forward*.13f+Vector3.down*.012f);
                return body.transform.position.z-plane.center.z;
            }
            foreach(float x in new[]{-1.5f,0,1.5f})Check(Cross(x)<0,"hunter capsule stopped at lateral offset "+x);
            var camera=lab.Eye;
            camera.transform.SetPositionAndRotation(foot+Vector3.back*6+Vector3.up*1.7f,Quaternion.LookRotation(plane.center-(foot+Vector3.back*6+Vector3.up*1.7f)));
            var picture=new RenderTexture(1280,720,24);
            camera.targetTexture=picture;camera.Render();RenderTexture.active=picture;
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(folder,"chapel-exit.png"),image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=null;picture.Release();Object.Destroy(picture);Object.Destroy(image);
            MeasureGates(renderer,Object.FindFirstObjectByType<BossFogGate>(),report);
            guardian.BeginEncounter();
            TestGuardianProjection(guardian,report);
            for(int i=0;i<100 && guardian.Alive;i++)guardian.ReceiveShadowHit(Bolt.Holy,guardian.transform.position);
            Check(!guardian.Alive && !gate.activeInHierarchy,"lethal holy damage releases both the renderer and collider immediately");
            foreach(float x in new[]{-1.5f,0,1.5f})Check(Cross(x)>1f,"hunter capsule crosses the open arch at lateral offset "+x);
            lab.RestartEncounter();
            Check(gate.activeInHierarchy && Cross(0)<0,"starting a fresh hunt restores the physical seal");
        }

        static void TestGuardianProjection(FixedLanternGuardian guardian,Report report) {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var view=(FixedGuardianView)typeof(FixedLanternGuardian).GetField("view",flags).GetValue(guardian);
            var caster=(GuardianShadowCaster)typeof(FixedGuardianView).GetField("shadowCaster",flags).GetValue(view);
            var frame=guardian.CurrentFrame();
            Vector3 inward=Vector3.ProjectOnPlane(frame.ArenaCenter-frame.Origin,Vector3.up).normalized;
            Vector2 axis=new Vector2(inward.x,inward.z),across=new Vector2(axis.y,-axis.x);
            frame.LampCount=6;
            // Measure contact from the visible high-detail mesh, independently of the caster proxy.
            var skin=guardian.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.bones.Any(b=>b.name=="Hips"));
            var model=skin.sharedMesh.vertices;
            var weights=skin.sharedMesh.boneWeights;
            var bind=skin.sharedMesh.bindposes;
            var joints=skin.bones.Select((b,i)=>b.localToWorldMatrix*bind[i]).ToArray();
            for(int i=0;i<model.Length;i++) {
                var p=model[i];var w=weights[i];
                model[i]=joints[w.boneIndex0].MultiplyPoint3x4(p)*w.weight0+
                    joints[w.boneIndex1].MultiplyPoint3x4(p)*w.weight1+
                    joints[w.boneIndex2].MultiplyPoint3x4(p)*w.weight2+
                    joints[w.boneIndex3].MultiplyPoint3x4(p)*w.weight3;
            }
            float soleY=model.Min(p=>p.y);
            var soles=model.Where(p=>p.y<soleY+.06f).GroupBy(p=>guardian.transform.InverseTransformPoint(p).x<0)
                .Select(g=>g.Aggregate(Vector3.zero,(s,p)=>s+p)/g.Count()).ToArray();
            if(soles.Length!=2)throw new InvalidOperationException("Could not locate both F01 boot soles");
            var ink=guardian.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Guardian ground shadow");
            var inkTexture=(Texture2D)ink.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
            var displayed=ink.gameObject.AddComponent<MeshCollider>();
            foreach(int lamps in new[]{6,2,1})foreach(float bearing in new[]{-80f,0f,80f}) {
                frame.LitCount=lamps;
                caster.Draw(frame,frame.Origin-Quaternion.AngleAxis(bearing,Vector3.up)*inward*20,guardian.ShadowScale);
                displayed.sharedMesh=null;displayed.sharedMesh=ink.sharedMesh;
                foreach(var sole in soles) {
                    var ray=new Ray(sole+Vector3.up,Vector3.down);
                    if(!guardian.TryHitShadow(ray,2,out float footHit) || Mathf.Abs(ray.GetPoint(footHit).y-sole.y)>.15f ||
                        !displayed.Raycast(ray,out var pixel,2) || Mathf.Abs(pixel.distance-footHit)>.015f ||
                        inkTexture.GetPixelBilinear(pixel.textureCoord.x,pixel.textureCoord.y).r<.5f)
                        throw new InvalidOperationException($"No matching rendered and hittable armour ink under boot {sole}, lamps={lamps}, bearing={bearing}, distance={footHit}, covered={view.ShadowProjection.Contains(new Vector2(sole.x,sole.z))}, floor={guardian.Encounter.GroundHeightAt(sole.x,sole.z)}");
                }
            }
            displayed.enabled=false;Object.Destroy(displayed);
            report.passed.Add("both F01 boot soles overlap the rendered shadow texture and hit the same floor surface at three light levels and three light bearings");
            Vector2 Size(int lit) {
                frame.LitCount=lit;
                caster.Draw(frame,frame.Origin-inward*20,guardian.ShadowScale);
                var points=(Vector2[])typeof(GuardianShadowCaster).GetField("points",flags).GetValue(caster);
                float Extent(Vector2 dir)=>points.Max(p=>Vector2.Dot(p,dir))-points.Min(p=>Vector2.Dot(p,dir));
                return new Vector2(Extent(across),Extent(axis));
            }
            Vector2 all=Size(6),two=Size(2),one=Size(1);
            if(!(all.y<two.y && Mathf.Abs(one.y-two.y)<.02f && one.x>two.x))
                throw new InvalidOperationException($"armour shadow extents at six, two and one lights: {all} {two} {one}");
            var aim=view.ShadowProjection.Aim;
            var arrow=new Ray(new Vector3(aim.x,frame.Origin.y+12,aim.y),Vector3.down);
            if(!guardian.TryHitShadow(arrow,25,out float distance) || !guardian.TryIgniteShadow(arrow.GetPoint(distance)))
                throw new InvalidOperationException("bounded armour ink could not be hit and ignited");
            report.passed.Add("guardian armour reaches its length limit in low light, still widens as lamps go out, and the resulting ink accepts shots and fire");
        }

        // Compare only the gate draws on the same GPU at equal projected height.
        // A tiny synchronous readback flushes each batch instead of timing submission alone.
        static void MeasureGates(MeshRenderer black,BossFogGate grey,Report report) {
            const int layer=30;
            var greyRenderers=grey.GetComponentsInChildren<MeshRenderer>();
            if(greyRenderers.Length!=1 || grey.GetComponentsInChildren<ParticleSystem>().Length!=0)
                throw new InvalidOperationException("Expected one mesh and no simulated particles in the white gate");
            var objects=greyRenderers.Select(r=>r.gameObject).Append(black.gameObject).Distinct().ToArray();
            var layers=objects.Select(o=>o.layer).ToArray();
            foreach(var obj in objects)obj.layer=layer;
            var cam=new GameObject("Gate benchmark camera").AddComponent<Camera>();cam.enabled=false;
            cam.orthographic=true;cam.cullingMask=1<<layer;cam.clearFlags=CameraClearFlags.SolidColor;
            var rt=new RenderTexture(1920,1080,24);cam.targetTexture=rt;
            var pixel=new Texture2D(1,1,TextureFormat.RGBA32,false);
            var samples=new[]{new List<double>(),new List<double>(),new List<double>()};
            try {
                for(int round=0;round<7;round++)for(int j=0;j<3;j++) {
                    int mode=(round+j)%3;
                    cam.cullingMask=mode==0?0:1<<layer;
                    bool reference=mode==2;
                    float height=reference?grey.Height:black.bounds.size.y;
                    Vector3 center=reference?grey.FloorCenter+Vector3.up*height*.5f:black.bounds.center;
                    Vector3 normal=reference?grey.Inward:black.transform.forward;
                    cam.transform.SetPositionAndRotation(center-normal*12,Quaternion.LookRotation(normal));
                    cam.orthographicSize=height*.62f;
                    var clock=Stopwatch.StartNew();
                    for(int i=0;i<16;i++)cam.Render();
                    RenderTexture.active=rt;pixel.ReadPixels(new Rect(0,0,1,1),0,0);
                    clock.Stop();
                    if(round>0)samples[mode].Add(clock.Elapsed.TotalMilliseconds/16);
                }
                foreach(var s in samples)s.Sort();
                report.emptyMs=samples[0][3];report.blackGateMs=samples[1][3];report.greyGateMs=samples[2][3];
            } finally {
                for(int i=0;i<objects.Length;i++)objects[i].layer=layers[i];
                cam.targetTexture=null;RenderTexture.active=null;rt.Release();
                Object.Destroy(cam.gameObject);Object.Destroy(pixel);Object.Destroy(rt);
            }
        }
    }
}
