using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Chapter01.Runtime.Testing
{
    // Opt-in player evidence only. Never attached to an authored scene.
    public sealed class Chapter01Round4Check : MonoBehaviour
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] sealed class PoseRecord {
            public float seconds,motionAge;
            public bool preparing,released,visible,inFlight;
            public int knives;
            public Vector3 knifePosition,weaponLocalPosition,weaponLocalEuler;
        }
        [Serializable] sealed class AudioRecord {
            public string resource;public float seconds;public int samples,channels,frequency;
        }
        [Serializable] sealed class Report {
            public string result="RUNNING",failure,scene,startedUtc,completedUtc,evidenceFolder,captureMode;
            public string scope="Controlled actual authored spawn and fixed real Eye. Only knife gameplay and weapon pose clocks are stepped during screenshots; no authored world/actor Tick. Six offscreen native-Canvas images are not a manual playthrough, audio recording, listening test or FPS benchmark. Final cancellation checks deliberately enter menu and apply lethal hunter damage. Afterwards a disposable, distant guardian/lamp resource smoke fixture is explicitly ticked 0.02 seconds; it is never photographed or saved as an authored encounter.";
            public bool controlledClock=true,fullPlaythrough=false,listeningVerified=false;
            public Vector3 hunterSpawn,cameraPosition,cameraEuler;
            public float firstSurfaceDistance;
            public float guardianSmokeHeight;
            public int guardianReadableSkins;
            public int checks;
            public List<string> passed=new List<string>(),screenshots=new List<string>(),unexpectedErrors=new List<string>();
            public List<PoseRecord> poses=new List<PoseRecord>();
            public List<AudioRecord> audio=new List<AudioRecord>();
            public List<Vector3> emblemCornersInSeal=new List<Vector3>();
            public List<Chapter01Round3Check.ScreenPixels> screenPixels=new List<Chapter01Round3Check.ScreenPixels>();
        }
        ShadowLab lab;
        HunterWeaponView weapon;
        HolyKnifeThrowMotion motion;
        Round3FrameCapture frameCapture;
        Report report;
        bool completed;
        float deadline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-gameplay-round4")<0)return;
            var runner=new GameObject("Opt-in chapter round 4 knife acceptance").AddComponent<Chapter01Round4Check>();
            runner.report=new Report {startedUtc=DateTime.UtcNow.ToString("o"),evidenceFolder=Argument("-chapter01-evidence") ??
                Path.Combine(Application.persistentDataPath,"GameplayRound4Check")};
            Directory.CreateDirectory(runner.report.evidenceFolder);
            runner.deadline=Time.realtimeSinceStartup+180;
            Application.logMessageReceived+=runner.ObserveLog;
            runner.StartCoroutine(runner.Run());
        }
        void Update() {
            if(!completed && deadline>0 && Time.realtimeSinceStartup>deadline) {
                StopAllCoroutines();Finish(new TimeoutException("Round 4 did not finish its controlled frame captures."));
            }
        }
        void ObserveLog(string message,string trace,LogType kind) {
            if(kind==LogType.Error || kind==LogType.Exception || kind==LogType.Assert)report.unexpectedErrors.Add(message);
        }
        IEnumerator Run() {
            Exception error=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0) {
                bool more=false;object current=null;
                try {more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}
                catch(Exception failure) {error=failure;break;}
                if(!more) {stack.Pop();continue;}
                if(current is IEnumerator nested) {stack.Push(nested);continue;}
                yield return current;
            }
            while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
            Finish(error);
        }
        IEnumerator Exercise() {
            float until=Time.realtimeSinceStartup+20;
            while(Time.realtimeSinceStartup<until) {
                lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
                if(lab!=null && lab.GameplayReady && lab.UI!=null && UnityEngine.Rendering.SplashScreen.isFinished)break;
                yield return null;
            }
            Check(lab!=null && lab.GameplayReady && lab.UI!=null,"actual chapter gameplay and native Canvas initialize");
            lab.enabled=false;
            var hall=lab.GetComponent<IronHallEncounter>();
            Check(hall!=null && hall.UseAuthoredLayout,"authored chapter layout is active");
            report.scene=lab.gameObject.scene.name;
            lab.RestartEncounter();
            SetField(lab.UI,"sessionStarted",true);Invoke(lab.UI,"SetView","battle",null);
            lab.Developer.ClearPauseReasons();lab.Developer.SetHudHidden(false);
            weapon=(HunterWeaponView)Field(lab,"weaponView");motion=(HolyKnifeThrowMotion)Field(lab,"knifeMotion");
            Check(weapon!=null && lab.Eye!=null && motion!=null,"production weapon and knife preparation controller exist");
            // Restart uses the author's existing grounded spawn. No actor is moved or created.
            report.hunterSpawn=lab.HunterGroundPosition;
            lab.RefreshView();ChooseClearFixedView();
            report.cameraPosition=lab.Eye.transform.position;report.cameraEuler=lab.Eye.transform.eulerAngles;
            weapon.SetLocomotion(Vector3.zero,Vector3.zero);weapon.SyncPose(lab.Weapon,0);
            frameCapture=Round3FrameCapture.Attach(lab);report.captureMode=frameCapture.CaptureMode;
            yield return null;Canvas.ForceUpdateCanvases();CheckHudSeal();
            yield return CheckAudioResources();

            var actors=UnityEngine.Object.FindObjectsByType<FireThiefAcolyte>(FindObjectsSortMode.None);
            var actorAges=actors.Select(a=>(float)Field(a,"age")).ToArray();
            int before=lab.HolyKnifeCount;
            Check(before>0 && lab.ThrowHolyKnife(),"production E-action begins with the actual starting inventory");
            Check(lab.HolyKnifeCount==before-1,"preparation spends exactly one knife");
            float advanced=0;
            foreach(float target in new[]{0f,.10f,.18f,.22f,.30f,.65f}) {
                float delta=target-advanced;
                if(delta>0) {Invoke(lab,"TickHolyKnives",delta);weapon.SyncPose(lab.Weapon,delta);}
                advanced=target;
                var flight=(HolyKnifeFlight)Field(lab,"knifeFlight");
                Check(flight!=null && flight.Visible,"knife model is visible at t="+target.ToString("F2"));
                if(target==.10f || target==.30f) {
                    var root=((GameObject)Field(flight,"root")).transform;
                    var tongues=root.GetComponentInChildren<LanternFire>().transform.Find("Flame tongues");
                    var main=tongues.GetComponent<ParticleSystem>().main;
                    float height=root.InverseTransformVector(tongues.TransformVector(Vector3.up*main.startSizeY.constant)).magnitude;
                    Check(Mathf.Abs(height-2*.40f*1.22f*.36f)<.001f,
                        "the knife carries a double-height flame in its held and released poses at t="+target.ToString("F2"));
                }
                if(target<HolyKnifeThrowMotion.ReleaseTime) {
                    Check(lab.HolyKnifeThrowing && !motion.Released && !flight.Active,"knife stays held before release at t="+target.ToString("F2"));
                    Vector3 expected=lab.Eye.transform.TransformPoint(HolyKnifeThrowMotion.Position(target));
                    Check(Vector3.Distance(flight.Position,expected)<.003f,"held pose follows the production left-side trajectory at t="+target.ToString("F2"));
                } else {
                    Check(motion.Released && flight.Active,"knife physically flies after the release boundary at t="+target.ToString("F2"));
                    Check(Vector3.Distance(lab.HolyKnifeLastReleasePosition,
                        lab.Eye.transform.TransformPoint(HolyKnifeThrowMotion.Position(HolyKnifeThrowMotion.ReleaseTime)))<.003f,
                        "release origin remains the visible left-side knife position at t="+target.ToString("F2"));
                }
                if(target>=HolyKnifeThrowMotion.Duration) {
                    Check(!lab.HolyKnifeThrowing && weapon.transform.localPosition.sqrMagnitude<.000001f,
                        "throw recovery completes and crossbow returns to its resting root pose");
                }
                report.poses.Add(new PoseRecord {seconds=target,motionAge=motion.Age,preparing=lab.HolyKnifeThrowing,
                    released=motion.Released,visible=flight.Visible,inFlight=flight.Active,knives=lab.HolyKnifeCount,
                    knifePosition=flight.Position,weaponLocalPosition=weapon.transform.localPosition,weaponLocalEuler=weapon.transform.localEulerAngles});
                yield return Capture("round4-knife-"+Mathf.RoundToInt(target*100).ToString("000"));
                Check(Vector3.Distance(lab.HunterGroundPosition,report.hunterSpawn)<.0001f &&
                    Vector3.Distance(lab.Eye.transform.position,report.cameraPosition)<.0001f &&
                    Quaternion.Angle(lab.Eye.transform.rotation,Quaternion.Euler(report.cameraEuler))<.001f,
                    "real spawn and camera remain fixed for t="+target.ToString("F2"));
            }
            for(int i=0;i<actors.Length;i++)Check(Mathf.Abs((float)Field(actors[i],"age")-actorAges[i])<.000001f,
                "placed acolyte gameplay clock was not advanced: "+actors[i].name);
            Check(lab.HolyKnifeCount==before-1,"one complete clear-path animation produces no extra spend or lamp reward");
            CheckHudSeal();

            Check(lab.ThrowHolyKnife(),"a second production preparation can begin after cooldown");
            Invoke(lab,"TickHolyKnives",.10f);weapon.SyncPose(lab.Weapon,.10f);
            Invoke(lab.UI,"ReturnToMenu");
            Check(lab.UI.CurrentView=="menu" && !lab.HolyKnifeThrowing && !AnyVisibleKnife(),
                "returning to the main menu cancels preparation and clears every held/flying knife");
            Invoke(lab,"TickHolyKnives",1f);
            Check(!motion.Released && !AnyVisibleKnife(),"a cancelled menu preparation cannot release on a later clock step");

            Invoke(lab.UI,"RestartNow");lab.enabled=false;
            Check(lab.HolyKnifeCount==lab.StartingHolyKnives && lab.ThrowHolyKnife(),"a new hunt restores stock and can begin preparation again");
            Invoke(lab,"TickHolyKnives",.10f);
            if(lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            lab.DamageHunter(10000);
            Check(lab.Finished && lab.HunterHealth==0 && !lab.HolyKnifeThrowing && !AnyVisibleKnife(),
                "real lethal hunter damage cancels an unreleased knife through the ending path");
            GuardianResourceSmoke();
            yield return null; // Let Destroy complete before the Player writes its final report and quits.
        }
        void GuardianResourceSmoke() {
            var fixture=new GameObject("Round 4 disposable guardian resource smoke - NOT AUTHORED");
            fixture.SetActive(false);fixture.transform.position=new Vector3(10000,0,10000);
            try {
                var source=Resources.Load<GameObject>("Guardian/F01");
                Check(source!=null,"Windows Player includes the imported guardian character resource");
                var sourceSkins=source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Check(sourceSkins.Length>0,"original F01 includes its weighted combat mesh");
                foreach(var skin in sourceSkins) {
                    Check(skin.sharedMesh!=null && skin.sharedMesh.isReadable,
                        "Player skeletal mesh is CPU-readable for pose verification: "+skin.name);
                    var weights=skin.sharedMesh.boneWeights;
                    Check(weights.Length==skin.sharedMesh.vertexCount && weights.Length>0,
                        "Player can actually read every imported vertex bone weight: "+skin.name);
                    report.guardianReadableSkins++;
                }
                var lampObject=new GameObject("Disposable explicitly linked ordinary lamp");
                lampObject.transform.SetParent(fixture.transform,false);lampObject.transform.localPosition=new Vector3(6,3,5);
                var light=lampObject.AddComponent<Light>();light.type=LightType.Point;light.range=24;light.intensity=1;
                var lamp=lampObject.AddComponent<EncounterLantern>();lamp.Encounter=lab;lamp.Wick=lampObject.transform;
                lamp.EffectiveLight=light;lamp.StartsLit=true;
                var guardianObject=new GameObject("Disposable guardian package smoke");guardianObject.transform.SetParent(fixture.transform,false);
                var guardian=guardianObject.AddComponent<FixedLanternGuardian>();guardian.Encounter=lab;
                guardian.StartOnArenaEntry=false;guardian.AutoLinkNearbyOrdinaryLamps=false;guardian.LinkedLanterns=new[]{lamp};
                fixture.SetActive(true);guardian.BeginEncounter();guardian.Tick(.02f);
                Check(guardian.HasImportedArmour && guardian.State!=null && guardian.State.Action!=FixedGuardianState.Move.Dormant,
                    "Player assembles imported armour and begins with its one explicit ordinary lamp");
                Check(guardian.LinkedLampCount==1 && guardian.LitLinkedLamps==1,
                    "disposable guardian uses only its disposable lamp");
                var armour=guardian.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Original F01 guardian armour");
                Check(armour!=null,"Player armour hierarchy contains the imported assembled model");
                var skins=armour.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Check(skins.Length>0,"Player armour retains real skeletal renderers");
                foreach(var skin in skins) {
                    Check(skin.sharedMesh!=null && skin.sharedMesh.isReadable && Finite(skin.bounds.center) && Finite(skin.bounds.extents),
                        "assembled skeletal renderer has readable source and finite world bounds: "+skin.name);
                }
                bool any=false;Bounds assembly=default;
                foreach(var renderer in armour.GetComponentsInChildren<Renderer>(true)) {
                    if(!renderer.enabled || renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer)continue;
                    Check(Finite(renderer.bounds.center) && Finite(renderer.bounds.extents),"assembled armour renderer bounds are finite: "+renderer.name);
                    if(!any) {assembly=renderer.bounds;any=true;}else assembly.Encapsulate(renderer.bounds);
                }
                report.guardianSmokeHeight=assembly.size.y;
                Check(any && assembly.size.y>=6 && assembly.size.y<=9,
                    "assembled Player giant including helmet has expected 6–9 metre world height ("+assembly.size.y.ToString("F3")+"m)");
            } finally {
                fixture.SetActive(false);UnityEngine.Object.Destroy(fixture);
            }
        }
        static bool Finite(Vector3 point)=>!float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        void ChooseClearFixedView() {
            Physics.SyncTransforms();var eye=lab.Eye;float startYaw=eye.transform.eulerAngles.y;
            foreach(float pitch in new[]{-5f,-15f,-30f,-55f})foreach(float offset in new[]{0f,30f,-30f,60f,-60f,90f,-90f,180f}) {
                eye.transform.rotation=Quaternion.Euler(pitch,startYaw+offset,0);
                Ray aim=eye.ViewportPointToRay(new Vector3(.5f,.5f,0));
                bool hit=FirstSurface(aim,out var surface);
                if(hit && surface.distance<28)continue;
                Vector3 from=eye.transform.TransformPoint(HolyKnifeThrowMotion.Position(HolyKnifeThrowMotion.ReleaseTime));
                Vector3 toHand=from-eye.transform.position;
                if(FirstSurface(new Ray(eye.transform.position,toHand.normalized),out var handBlock) && handBlock.distance<=toHand.magnitude+.05f)continue;
                Vector3 target=hit ? surface.point : aim.GetPoint(40);
                Vector3 direction=(target-from).normalized;
                if(FirstSurface(new Ray(from,direction),out var pathBlock) && pathBlock.distance<26)continue;
                SetField(lab,"yaw",startYaw+offset);SetField(lab,"pitch",pitch);
                report.firstSurfaceDistance=hit?surface.distance:-1;
                Check(true,"existing spawn has an unobstructed camera and left-hand path for all six samples");return;
            }
            throw new InvalidOperationException("No clear 26m knife view at the authored spawn; no substitute player placement was invented.");
        }
        void CheckHudSeal() {
            Canvas.ForceUpdateCanvases();
            var hunter=lab.UI.UiCanvas.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t=>t.name=="Hunter health" && t.gameObject.activeInHierarchy);
            Check(hunter!=null,"active native HUD has the Hunter health section");
            var seal=hunter.Find("Hunter covenant seal") as RectTransform;
            var icon=hunter.Find("Icon emblem") as RectTransform;
            Check(seal!=null && icon!=null && icon.GetComponent<Image>()?.sprite!=null,"native emblem and its engraved frame exist");
            Check(Mathf.Abs(Mathf.DeltaAngle(seal.localEulerAngles.z,45))<.01f,"covenant frame is actually rotated 45 degrees");
            var corners=new Vector3[4];icon.GetWorldCorners(corners);
            Rect inner=seal.rect;inner.xMin+=1;inner.xMax-=1;inner.yMin+=1;inner.yMax-=1;
            for(int i=0;i<4;i++) {
                Vector3 local=seal.InverseTransformPoint(corners[i]);
                if(report.emblemCornersInSeal.Count<4)report.emblemCornersInSeal.Add(local);
                Check(local.x>=inner.xMin-.01f && local.x<=inner.xMax+.01f && local.y>=inner.yMin-.01f && local.y<=inner.yMax+.01f,
                    "emblem corner "+i+" fits inside the rotated frame's one-pixel inner border");
            }
        }
        IEnumerator CheckAudioResources() {
            string[] names={"Crossbow_01","Crossbow_02","Crossbow_03","ArrowImpact_01","ArrowImpact_02","ArrowImpact_03", "KnifeThrow_01","KnifeThrow_02","LampIgnite_01","LampIgnite_02"};
            foreach(string name in names) {
                var clip=Resources.Load<AudioClip>("GameplaySfx/"+name);
                Check(clip!=null,"recorded combat resource loads: "+name);
                clip.LoadAudioData();float until=Time.realtimeSinceStartup+5;
                while(clip.loadState==AudioDataLoadState.Loading && Time.realtimeSinceStartup<until)yield return null;
                Check(clip.loadState==AudioDataLoadState.Loaded && clip.samples>0 && clip.channels==1 && clip.frequency==48000,
                    "recorded resource has readable mono 48kHz audio data: "+name);
                report.audio.Add(new AudioRecord {resource="GameplaySfx/"+name,seconds=clip.length,samples=clip.samples,channels=clip.channels,frequency=clip.frequency});
            }
            var sound=lab.Eye.GetComponent<ShiyinAudio>();
            Check(sound!=null && sound.CombatSoundsAvailable,"production audio router finds all four recorded combat cue groups");
        }
        bool AnyVisibleKnife() {
            foreach(var flight in (IEnumerable<HolyKnifeFlight>)Field(lab,"knifeFlights"))if(flight.Visible)return true;
            return false;
        }
        bool FirstSurface(Ray ray,out RaycastHit hit) {
            object[] args={ray,default(RaycastHit)};
            bool found=(bool)typeof(ShadowLab).GetMethod("FirstShotSurface",Private).Invoke(lab,args);
            hit=(RaycastHit)args[1];return found;
        }
        IEnumerator Capture(string name) {
            Invoke(lab.UI,"RefreshLiveHud");Canvas.ForceUpdateCanvases();
            int target=frameCapture.RenderedFrames+2;float until=Time.realtimeSinceStartup+15;
            while(frameCapture.RenderedFrames<target && Time.realtimeSinceStartup<until) {
                if(!string.IsNullOrEmpty(frameCapture.Failure))throw new InvalidOperationException(frameCapture.Failure);
                yield return null;
            }
            Check(frameCapture.RenderedFrames>=target,"two completed offscreen render frames precede "+name);
            Texture2D image=frameCapture.ReadFrame();
            try {
                var pixels=Chapter01Round3Check.InspectScreen(image,name);
                Chapter01Round3Check.RequireVisibleScreen(pixels);report.screenPixels.Add(pixels);
                string file=name+".png";File.WriteAllBytes(Path.Combine(report.evidenceFolder,file),image.EncodeToPNG());report.screenshots.Add(file);
            } finally {if(image!=null)UnityEngine.Object.Destroy(image);}
        }
        void Check(bool condition,string message) {
            if(!condition)throw new InvalidOperationException(message);
            report.checks++;report.passed.Add(message);Debug.Log("PASS ROUND4 "+message);
        }
        void Finish(Exception error) {
            if(completed)return;completed=true;Application.logMessageReceived-=ObserveLog;
            if(error==null && report.unexpectedErrors.Count>0)error=new InvalidOperationException(string.Join(" | ",report.unexpectedErrors));
            report.result=error==null?"PASS":"FAIL";report.failure=error?.ToString();report.completedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(report.evidenceFolder,"round4-check.json"),JsonUtility.ToJson(report,true));
            if(error==null)Debug.Log("GAMEPLAY_ROUND4_CHECK_PASS checks="+report.checks);else Debug.LogError("GAMEPLAY_ROUND4_CHECK_FAIL "+error);
            Application.Quit(error==null?0:1);
        }
        static object Field(object owner,string name)=>owner.GetType().GetField(name,Private).GetValue(owner);
        static void SetField(object owner,string name,object value)=>owner.GetType().GetField(name,Private).SetValue(owner,value);
        static void Invoke(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,Private).Invoke(owner,args);
        static string Argument(string name) {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0 && i+1<args.Length?args[i+1]:null;}
    }
}
