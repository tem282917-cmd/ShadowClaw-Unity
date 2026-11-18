using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Opt-in actual-scene evidence. Never creates an encounter or saves authoring changes.
    public sealed class Chapter01GuardianPlacementCheck : MonoBehaviour
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        [Serializable] sealed class LampRow { public string name;public int index;public Vector3 position;public float distance;public bool lit; }
        [Serializable] sealed class ViewRow {
            public string image,purpose,ground,action;public Vector3 player,eye,aim;public int litLamps;
        }
        [Serializable] sealed class Report {
            public string result="RUNNING",failure,startedUtc,completedUtc,scene,evidenceFolder,captureMode,guardian;
            public string scope="Controlled Player check of the guardian actually authored in the main scene. Gameplay Update is disabled; " +
                "grounded player/camera viewpoints and explicit real ShadowLab.Tick steps are used. No actor is spawned or moved, " +
                "no lamp is created and no scene asset is saved. This is not a manual playthrough, audio listening verdict or performance benchmark. " +
                "Nine upward ray samples are a limited overhead-clearance check, not full swept-arm collision proof; the editor placement report supplies broader geometry evidence.";
            public bool controlledClock=true,fullPlaythrough=false;
            public int checks,validStandPoints,guardianCount,finalBossViews;
            public Vector3 guardianPosition,arenaCenter,lockedSlamPoint,shadowProbePoint;
            public Vector2 arenaSize;public Bounds armourBounds;public float minimumHeadroom;
            public List<string> passed=new List<string>(),unexpectedErrors=new List<string>(),actions=new List<string>();
            public List<LampRow> lamps=new List<LampRow>();public List<ViewRow> views=new List<ViewRow>();
            public List<Chapter01Round3Check.ScreenPixels> pixels=new List<Chapter01Round3Check.ScreenPixels>();
        }
        ShadowLab lab;IronHallEncounter hall;FixedLanternGuardian guardian;Round3FrameCapture capture;
        Report report;bool completed;float deadline;Vector3 originalPosition,originalScale,aim;Quaternion originalRotation;
        Vector3 front;string support="";readonly List<Vector3> stands=new List<Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-guardian-placement")<0)return;
            var runner=new GameObject("Opt-in authored guardian placement evidence").AddComponent<Chapter01GuardianPlacementCheck>();
            runner.report=new Report {startedUtc=DateTime.UtcNow.ToString("o"),evidenceFolder=Argument("-chapter01-evidence") ??
                Path.Combine(Application.persistentDataPath,"GuardianPlacementCheck")};
            Directory.CreateDirectory(runner.report.evidenceFolder);runner.deadline=Time.realtimeSinceStartup+180;
            Application.logMessageReceived+=runner.Log;runner.StartCoroutine(runner.Run());
        }
        void Update() {if(!completed && deadline>0 && Time.realtimeSinceStartup>deadline) {StopAllCoroutines();Finish(new TimeoutException("Guardian placement evidence timed out."));}}
        void Log(string message,string trace,LogType type) {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)report.unexpectedErrors.Add(message);}
        IEnumerator Run() {
            Exception error=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0) {
                bool more=false;object current=null;
                try {more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception caught){error=caught;break;}
                if(!more){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();Finish(error);
        }
        IEnumerator Exercise() {
            float until=Time.realtimeSinceStartup+25;
            while(Time.realtimeSinceStartup<until) {
                lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
                if(lab!=null && lab.GameplayReady && lab.UI!=null && UnityEngine.Rendering.SplashScreen.isFinished)break;yield return null;
            }
            Check(lab!=null && lab.GameplayReady && lab.UI!=null,"actual chapter gameplay and native HUD initialize");
            lab.enabled=false;hall=lab.GetComponent<IronHallEncounter>();
            Check(hall!=null && hall.UseAuthoredLayout,"the authored main scene is loaded");report.scene=lab.gameObject.scene.name;
            var all=UnityEngine.Object.FindObjectsByType<FixedLanternGuardian>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Where(g=>g.gameObject.scene==lab.gameObject.scene).ToArray();
            report.guardianCount=all.Length;Check(all.Length==1,"main scene contains exactly one authored intermediate guardian");guardian=all[0];
            Check(guardian.isActiveAndEnabled,"the authored guardian component is enabled");
            originalPosition=guardian.transform.position;originalRotation=guardian.transform.rotation;originalScale=guardian.transform.localScale;
            report.guardian=PathOf(guardian.transform);report.guardianPosition=originalPosition;
            lab.RestartEncounter();Set(lab.UI,"sessionStarted",true);Call(lab.UI,"SetView","battle",null);lab.Developer.ClearPauseReasons();
            lab.Developer.SetHudHidden(false);if(!lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            report.finalBossViews=UnityEngine.Object.FindObjectsByType<ShadowDemonView>(FindObjectsSortMode.None).Count(v=>v.isActiveAndEnabled);
            Check(report.finalBossViews==1,"the original final Boss still has exactly one active runtime view");
            Check(guardian.HasImportedArmour && Mathf.Abs(guardian.BodyHeight-7.8f)<.01f,"placed guardian loads its imported armour at the intended 7.8-metre scale");
            var armour=guardian.GetComponentsInChildren<Transform>().Where(t=>t.name=="Original F01 guardian armour").ToArray();
            Check(armour.Length==1,"exactly one runtime armour assembly renders; no authoring preview duplicates it");
            var skin=armour[0].GetComponentInChildren<SkinnedMeshRenderer>();
            Check(skin!=null && skin.sharedMesh!=null && skin.sharedMaterial.mainTexture!=null &&
                armour[0].GetComponentsInChildren<Transform>().Any(t=>t.name=="F02 chest lamp"),
                "the playable guardian has its textured F01 skin and original F02 lamp");
            var hand=skin.bones.First(b=>b!=null && b.name=="Palm.R");
            foreach(string name in new[]{"F01__Lamplighter__0","F02__Lamplighter_Chest_Lamp__0"}) {
                var display=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .Where(t=>t.gameObject.scene==guardian.gameObject.scene && t.name==name && !t.IsChildOf(guardian.transform)).ToArray();
                Check(display.Length==1 && !display[0].gameObject.activeInHierarchy,"only the gameplay model is visible; old display disabled: "+name);
            }
            report.armourBounds=ArmourBounds(armour[0]);
            float authoredHeight=guardian.BodyHeight*guardian.transform.lossyScale.y;
            Check(Mathf.Abs(report.armourBounds.size.y-authoredHeight)<.1f*authoredHeight,
                "assembled armour height follows the authored guardian scale");
            foreach(var renderer in armour[0].GetComponentsInChildren<Renderer>())if(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                foreach(var material in renderer.sharedMaterials)Check(material!=null && material.shader!=null && material.shader.isSupported &&
                    material.shader.name!="Hidden/InternalErrorShader","guardian Player material shader is usable: "+renderer.name);
            Check(guardian.StartActive && guardian.StartOnArenaEntry && guardian.State.Action==FixedGuardianState.Move.Dormant,
                "the placed encounter begins dormant and is configured for real arena-entry activation");
            var frame=guardian.CurrentFrame();report.arenaCenter=frame.ArenaCenter;report.arenaSize=frame.ArenaSize;
            front=Vector3.ProjectOnPlane(guardian.transform.forward,Vector3.up).normalized;
            var links=(IEnumerable<int>)Read(guardian,"links");
            foreach(int index in links) {
                Check(index>=HuntBoss.LampCapacity && lab.IsOrdinaryLamp(index),"guardian link uses an ordinary lamp, never a final-Boss lamp: "+index);
                Vector3 p=lab.GetLampPosition(index);float horizontal=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(originalPosition.x,originalPosition.z));
                Check(horizontal<=guardian.LampSearchRadius+.1f,"linked lamp lies within the authored nearby-lamp range: "+index);
                report.lamps.Add(new LampRow {index=index,name=FindLampName(index),position=p,distance=Vector3.Distance(p,originalPosition),lit=lab.IsLampLit(index)});
            }
            Check(guardian.LinkedLampCount==3 && guardian.LitLinkedLamps==3,"the placed guardian starts with three effective nearby ordinary lamps");
            MeasureClearance();FindStandPoints(frame);
            capture=Round3FrameCapture.Attach(lab);report.captureMode=capture.CaptureMode;

            Vector3 outside=frame.ArenaCenter+front*(Mathf.Max(frame.ArenaSize.x,frame.ArenaSize.y)*.5f+1.6f);
            Check(FindView(outside,2.5f,false,out var entry),"a grounded frontal approach outside the arena has clear sight of the guardian");
            Place(entry);Aim(originalPosition+Vector3.up*3.8f);Step(.05f);
            float restingHandHeight=hand.position.y;
            Check(guardian.State.Action==FixedGuardianState.Move.Dormant && !lab.EncounterStarted,
                "standing outside this arena neither activates the guardian nor starts the final boss");
            yield return Capture("guardian-entry-front","actual frontal approach before arena activation");
            yield return CheckChapelLighting(skin, links.ToArray());

            Vector3 side=Vector3.Cross(Vector3.up,front);
            Vector3 overviewSeed=frame.ArenaCenter+front*(frame.ArenaSize.y*.32f)+side*(frame.ArenaSize.x*.34f);
            Check(FindView(overviewSeed,3f,true,out var overview),"a grounded side overview exists within the authored battle floor");
            Place(overview);Aim(Vector3.Lerp(frame.ArenaCenter,originalPosition,.65f)+Vector3.up*2.5f);Step(.05f);
            Check(guardian.State.Action!=FixedGuardianState.Move.Dormant && lab.ActiveGuardian==guardian && !lab.EncounterStarted,
                "entering the actual arena activates the guardian through ShadowLab.Tick while the final fight stays dormant");
            GuardianVoice("mechanism","Awaken");
            yield return Capture("guardian-arena-overview","authored arena, existing lamps and active guardian");
            if(FindView(originalPosition+front*9.5f,2f,true,out var close)) {
                Place(close);Aim(originalPosition+Vector3.up*4.1f);
                yield return Capture("guardian-original-close","F01 armour and F02 chest lamp from a clear playable chapel viewpoint");
                Place(overview);
            }

            for(int i=0;i<100 && guardian.State.Action!=FixedGuardianState.Move.SlamLocked;i++)Step(.05f);
            Check(guardian.State.Action==FixedGuardianState.Move.SlamLocked,"the placed guardian naturally reaches its first tracked-and-locked slam");
            GuardianVoice("mechanism","Lock");
            Check(hand.position.y>restingHandHeight+.65f,"the original F01 right hand is raised in the actual locked slam");
            report.lockedSlamPoint=guardian.State.LockedPoint;
            var boundary=guardian.GetComponentsInChildren<LineRenderer>().FirstOrDefault(r=>r.name=="Guardian exact attack boundary");
            Check(boundary!=null && boundary.enabled && boundary.positionCount>=32,"the real locked-slam warning boundary is visible");
            Check(Mathf.Abs(FixedGuardianState.HorizontalDistance(boundary.GetPosition(0),guardian.State.LockedPoint)-4.2f)<.02f &&
                Mathf.Abs(guardian.State.Remaining-.6f)<.02f,"the authored slam draws its 4.2 m radius for the 0.6-second lock");
            Check(FindShadowProbe(out var shadowPoint),"the real projected guardian shadow contains a production hittable ground point");report.shadowProbePoint=shadowPoint;
            // Keep the visible warning and enemy frozen while taking the decisive frame.
            Aim(Vector3.Lerp(report.lockedSlamPoint,originalPosition,.65f)+Vector3.up*1.9f);
            yield return Capture("guardian-slam-shadow","locked slam warning and real hittable guardian ground shadow");
            int damageEvents=guardian.State.DamageEvents;Vector3 locked=guardian.State.LockedPoint;
            var escape=stands.Where(p=>FixedGuardianState.HorizontalDistance(p,locked)>guardian.Tuning.SlamRadius+1f)
                .OrderBy(p=>Vector3.Distance(p,lab.HunterGroundPosition)).ToArray();
            Check(escape.Length>0,"the authored floor supplies a clear standing position outside the locked slam radius");
            Place(escape[0]);Step(.1f);Check(guardian.State.LockedPoint==locked,"moving on the actual floor does not move a locked attack target");
            Step(guardian.State.Remaining+.02f);
            Check(guardian.State.Action==FixedGuardianState.Move.SlamImpact && guardian.State.DamageEvents==damageEvents,
                "leaving the telegraph radius on the actual floor avoids the released slam");
            CheckRemainingGuardianSounds();
            yield return CheckShadowFire(links.ToArray());
            Check(Vector3.Distance(originalPosition,guardian.transform.position)<.00001f && Quaternion.Angle(originalRotation,guardian.transform.rotation)<.001f &&
                originalScale==guardian.transform.localScale,"fixed guardian root position, rotation and scale remain exactly authored");
            yield return null;
        }
        IEnumerator CheckChapelLighting(SkinnedMeshRenderer armour,int[] links) {
            var chapel=guardian.Readability;
            Check(chapel!=null && chapel!=hall.Readability && chapel.FillLights.Count==3,
                "the chapel has its own three local presentation lights");
            lab.RefreshOrdinarySceneLamps();
            var ordinary=UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None);
            yield return CheckVisibleChapelCandles(ordinary,links);
            foreach(var fill in chapel.FillLights) {
                Check(fill.enabled && fill.type==LightType.Point && fill.shadows==LightShadows.None &&
                    !ordinary.Any(l=>l.ControlsLight(fill)) && !hall.GalleryLights.Contains(fill) && !hall.Readability.FillLights.Contains(fill),
                    "non-shadowing chapel fill is separate from interactive ordinary lamps and the final hall: "+fill.name);
            }
            var tint=new MaterialPropertyBlock();armour.GetPropertyBlock(tint);
            Color litGain=tint.GetColor("_EmissionColor");
            Check(litGain.b>armour.sharedMaterial.GetColor("_EmissionColor").b,"the original F01 texture receives an instance-local readability boost");
            chapel.PresentationEnabled=false;
            Check(chapel.FillLights.All(l=>!l.enabled) && hall.Readability.FillLights.All(l=>l.enabled),"chapel comparison switch affects only its own room");
            yield return Capture("guardian-entry-fill-off","same grounded entrance viewpoint with presentation lighting disabled");
            chapel.PresentationEnabled=true;
            foreach(int index in links)Check(lab.TryExtinguishLamp(index),"actual linked lamp can extinguish independently of chapel fill: "+index);
            Step(.05f);
            armour.GetPropertyBlock(tint);
            Check(guardian.LitLinkedLamps==0 && chapel.LampPower01==0 && chapel.FillLights.All(l=>l.enabled) &&
                tint.GetColor("_EmissionColor").b>litGain.b,"all three mechanics lights are dark while the chapel floor lights and F01 readability remain active");
            yield return Capture("guardian-entry-blackout","real entrance viewpoint with the three linked flames extinguished");
            foreach(int index in links) {
                ordinary.First(l=>lab.GetLampIndex(l)==index).Ignite();
                Check(lab.IsLampLit(index),"linked ordinary lamp restored for the next check: "+index);
            }
            Step(.05f);
            Check(guardian.LitLinkedLamps==3 && chapel.LampPower01==1 && guardian.State.Action==FixedGuardianState.Move.Dormant,
                "relighting restores the three-lamp state before arena entry");
        }
        IEnumerator CheckVisibleChapelCandles(EncounterLantern[] ordinary,int[] links) {
            var candles=ordinary.Where(l=>l.EffectiveLight!=null &&
                l.EffectiveLight.name.StartsWith("WarmPool__D03__Chapel_Candle_",StringComparison.Ordinal))
                .OrderBy(l=>Vector3.Distance(l.Position,lab.HunterGroundPosition)).ToArray();
            Check(candles.Length==4 && candles.Count(l=>links.Contains(lab.GetLampIndex(l)))==3,
                "all four physical chapel candles are interactive while the guardian binds three of them");
            Vector3 entry=lab.HunterGroundPosition,oldAim=aim;
            string oldSupport=support;
            EncounterLantern portrait=null;
            foreach(var candle in candles) {
                for(int angle=0;angle<12 && portrait==null;angle++) {
                    Vector3 direction=Quaternion.Euler(0,angle*30,0)*Vector3.forward;
                    if(!TryFoot(candle.Position+direction*2.4f,out var foot,out var ground) ||
                        !ClearSight(foot+Vector3.up*1.65f,candle.Position+Vector3.up*.1f))continue;
                    portrait=candle;support=ground;Place(foot);Aim(candle.Position+Vector3.up*.12f);
                }
                if(portrait!=null)break;
            }
            Check(portrait!=null,"a chapel candle is visible up close from walkable ground");
            var props=new MaterialPropertyBlock();
            foreach(var candle in candles) {
                var mesh=candle.VisualSurfaces[0].GetComponentsInChildren<MeshRenderer>()
                    .Single(m=>m.name.StartsWith("Candle_Flame__",StringComparison.Ordinal));
                var flame=candle.GetComponentInChildren<LanternFire>().GetComponentInChildren<ParticleSystemRenderer>();
                Color sharedWarm=mesh.sharedMaterial.GetColor("_EmissionColor");
                mesh.GetPropertyBlock(props,0);Color warm=props.GetColor("_EmissionColor"),baseWarm=props.GetColor("_Color");
                Check(warm.r>warm.b && baseWarm.r>baseWarm.b && candle.ProjectsAt(candle.Position,1),
                    "authored chapel flame and local projection are warm and active: "+mesh.name);
                if(candle==portrait)yield return Capture("guardian-candle-warm","authored warm candle at a grounded firing position");
                Check(lab.TryExtinguishLamp(lab.GetLampIndex(candle)),"extinguish "+mesh.name);
                mesh.GetPropertyBlock(props,0);Color glow=props.GetColor("_EmissionColor"),baseColor=props.GetColor("_Color");
                flame.GetPropertyBlock(props);
                Check(glow.b>glow.r && glow.r>glow.g*2 && baseColor.b>baseColor.r && props.GetFloat("_Dormant")>.9f &&
                    !candle.EffectiveLight.enabled && candle.EffectiveLight.intensity==0 && !candle.ProjectsAt(candle.Position,1),
                    "the imported flame and animated marker turn violet while the actual emitter and shadow source go dark: "+mesh.name);
                Check(candles.Where(l=>l!=candle).All(l=>l.Lit) && mesh.sharedMaterial.GetColor("_EmissionColor")==sharedWarm,
                    "other candles and the shared fire material retain their warmth");
                if(candle==portrait)yield return Capture("guardian-candle-purple","same candle with only its violet status glow");
                Check(lab.TryIgniteLamp(candle.Target),"the production wick-hit route relights "+mesh.name);
                mesh.GetPropertyBlock(props,0);
                Check(props.GetColor("_EmissionColor")==warm && props.GetColor("_Color")==baseWarm && candle.ProjectsAt(candle.Position,1),
                    "relighting restores authored flame colors and shadow projection: "+mesh.name);
                if(candle==portrait)yield return Capture("guardian-candle-relit","same authored candle after a wick hit");
                candle.ResetLantern();
            }
            support=oldSupport;Place(entry);Aim(oldAim);
        }
        void MeasureClearance() {
            report.minimumHeadroom=30;
            foreach(float x in new[]{-1.6f,0,1.6f})foreach(float z in new[]{-1.2f,0,1.2f}) {
                Vector3 origin=originalPosition+guardian.transform.rotation*new Vector3(x,.12f,z);
                foreach(var hit in Physics.RaycastAll(origin,Vector3.up,30,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) {
                    if(hit.transform.IsChildOf(guardian.transform))continue;
                    report.minimumHeadroom=Mathf.Min(report.minimumHeadroom,hit.distance+.12f);
                }
            }
            Check(report.minimumHeadroom>=9f,"nine footprint rays provide at least nine metres of static overhead clearance");
        }
        void FindStandPoints(FixedGuardianFrame frame) {
            foreach(float x in new[]{-.3f,0,.3f})foreach(float z in new[]{-.3f,0,.3f}) {
                Vector3 seed=frame.ArenaCenter+frame.ArenaRight*frame.ArenaSize.x*x+frame.ArenaForward*frame.ArenaSize.y*z;
                if(TryFoot(seed,out var p,out _))stands.Add(p);
            }
            report.validStandPoints=stands.Count;Check(stands.Count>=4,"at least four of nine interior floor samples support a clear hunter capsule");
        }
        bool FindView(Vector3 seed,float radius,bool inside,out Vector3 foot) {
            foreach(float r in new[]{0f,radius*.5f,radius})for(int i=0;i<(r==0?1:12);i++) {
                float a=i*Mathf.PI/6;Vector3 p=seed+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);
                if(!TryFoot(p,out p,out var ground))continue;
                var f=guardian.CurrentFrame();f.Hunter=p;if(f.InsideArena!=inside)continue;
                if(!ClearSight(p+Vector3.up*1.65f,originalPosition+Vector3.up*4))continue;
                support=ground;foot=p;return true;
            }
            foot=default;return false;
        }
        bool TryFoot(Vector3 seed,out Vector3 foot,out string ground) {
            var player=(CharacterController)Read(lab,"playerBody");
            foreach(var hit in Physics.RaycastAll(new Vector3(seed.x,originalPosition.y+3,seed.z),Vector3.down,6,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)) {
                if(!hall.IsGround(hit.collider) || hit.normal.y<.7f || Mathf.Abs(hit.point.y-originalPosition.y)>1.1f)continue;
                Vector3 p=hit.point+Vector3.up*.04f;
                if(Physics.OverlapCapsule(p+Vector3.up*.38f,p+Vector3.up*1.42f,.34f,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Any(c=>c!=player))continue;
                foot=p;ground=PathOf(hit.transform);return true;
            }
            foot=default;ground="";return false;
        }
        bool ClearSight(Vector3 eye,Vector3 target) {
            var player=(CharacterController)Read(lab,"playerBody");
            return !Physics.RaycastAll(eye,(target-eye).normalized,Vector3.Distance(eye,target)-.1f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Any(h=>h.collider!=player && !h.transform.IsChildOf(guardian.transform));
        }
        bool FindShadowProbe(out Vector3 point,bool clearShot=false) {
            var filter=guardian.GetComponentsInChildren<MeshFilter>().FirstOrDefault(m=>m.name=="Guardian ground shadow");
            if(filter!=null && filter.sharedMesh!=null && filter.GetComponent<Renderer>().enabled) {
                Bounds b=filter.GetComponent<Renderer>().bounds;
                // Start inside the broad torso region, then sample out to both hands and feet.
                for(int r=0;r<=15;r++)for(int x=-r;x<=r;x++)for(int z=-r;z<=r;z++) {
                    if(Mathf.Max(Mathf.Abs(x),Mathf.Abs(z))!=r)continue;
                    var seed=new Vector3(b.center.x+b.extents.x*x/16,originalPosition.y+3,b.center.z+b.extents.z*z/16);
                    var ray=new Ray(seed,Vector3.down);
                    if(!guardian.TryHitShadow(ray,5f,out float distance))continue;
                    Vector3 p=ray.GetPoint(distance);
                    if(FixedGuardianState.HorizontalDistance(p,originalPosition)<2)continue;
                    if(clearShot && (!ClearSight(lab.Eye.transform.position,p) ||
                        !guardian.TryHitShadow(new Ray(lab.Eye.transform.position,(p-lab.Eye.transform.position).normalized),40,out _)))continue;
                    if(Physics.RaycastAll(ray,5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                        .Any(h=>hall.IsGround(h.collider) && Mathf.Abs(h.point.y-p.y)<.18f)) {point=p;return true;}
                }
            }
            point=default;return false;
        }
        AudioSource GuardianVoice(string channel,string cue) {
            var source=guardian.transform.Find("Guardian "+channel)?.GetComponent<AudioSource>();
            Check(source!=null && source.clip!=null && source.clip.name==cue && source.spatialBlend==1,
                "the real guardian emits the spatial "+cue+" cue on "+channel);
            return source;
        }
        void CheckRemainingGuardianSounds() {
            var strike=GuardianVoice("impact","Slam");
            Check(Vector3.Distance(strike.transform.position,guardian.State.LockedPoint)<.02f,
                "the missed slam sounds at its locked floor mark");
            var mixer=lab.Eye.GetComponent<ShiyinAudio>();
            mixer.SetPaused(true);
            Check(!strike.isPlaying,"pausing freezes the guardian impact");
            mixer.SetPaused(false);
            Check(strike.isPlaying,"resuming continues the guardian impact");
            mixer.SetLevels(0,lab.UI.Settings.Music);
            Check(strike.volume==0,"the effects slider mutes the guardian");
            mixer.SetLevels(lab.UI.Settings.Effects,lab.UI.Settings.Music);
            void Until(FixedGuardianState.Move move) {
                for(int i=0;i<40 && guardian.State.Action!=move;i++)guardian.Tick(guardian.State.Remaining+.015f);
                Check(guardian.State.Action==move,"audio exercise reaches "+move);
            }
            Until(FixedGuardianState.Move.SweepTell);GuardianVoice("mechanism","DrawArm");
            Until(FixedGuardianState.Move.SweepImpact);GuardianVoice("impact","Sweep");
            Until(FixedGuardianState.Move.WaveTell);GuardianVoice("mechanism","ChestChime");
            Until(FixedGuardianState.Move.WaveTravel);GuardianVoice("impact","Wave");
            Until(FixedGuardianState.Move.Ritual);GuardianVoice("mechanism","Prayer");
            var ordinary=UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None);
            int id=guardian.State.RitualLamp;
            var lamp=ordinary.Single(l=>lab.GetLampIndex(l)==id);
            guardian.Tick(guardian.State.Remaining+.015f);
            var snuff=GuardianVoice("impact","Snuff");
            Check(!lamp.Lit && Vector3.Distance(snuff.transform.position,lamp.Position)<.02f,
                "completed prayer sounds at the extinguished chapel lamp");
            var wickMesh=lamp.VisualSurfaces[0].GetComponentsInChildren<MeshRenderer>()
                .Single(m=>m.name.StartsWith("Candle_Flame__",StringComparison.Ordinal));
            var properties=new MaterialPropertyBlock();
            wickMesh.GetPropertyBlock(properties,0);
            Color ritualGlow=properties.GetColor("_EmissionColor");
            Check(ritualGlow.b>ritualGlow.r && !lamp.ProjectsAt(lamp.Position,1),
                "the guardian prayer turns its own visible candle violet and removes its projected light");
            lamp.Ignite();
            Until(FixedGuardianState.Move.SlamTrack);GuardianVoice("mechanism","RaiseHammer");
            Until(FixedGuardianState.Move.Ritual);
            lamp=ordinary.Single(l=>lab.GetLampIndex(l)==guardian.State.RitualLamp);
            Check(lab.TryIgniteLamp(lamp.Target) && guardian.State.Action==FixedGuardianState.Move.Recover,
                "a real lamp shot interrupts the guardian prayer");
            GuardianVoice("impact","Stagger");
        }
        IEnumerator CheckShadowFire(int[] links) {
            for(int i=1;i<links.Length;i++)Check(lab.TryExtinguishLamp(links[i]),"a real chapel lamp goes dark to enlarge the target");
            Step(.05f);
            Check(guardian.LitLinkedLamps==1,"one chapel lamp still casts an attackable guardian silhouette");
            Vector3 target=default;bool found=false;
            foreach(Vector3 stand in stands.OrderBy(p=>(p-originalPosition).sqrMagnitude)) {
                Place(stand);
                if(!FindShadowProbe(out target,true))continue;
                found=true;break;
            }
            Check(found,"a grounded hunter has a clear line to ink on the remaining lamp's silhouette");
            Aim(target);yield return Capture("guardian-one-lamp-shadow","grounded first-person aim at the one-lamp shadow before ignition");
            int stock=lab.HolyKnifeCount, health=guardian.State.Health;
            Check(lab.ThrowHolyKnife(),"the hunter starts the normal left-hand holy knife throw");
            for(int i=0;i<100 && !guardian.ShadowBurning;i++)Call(lab,"TickHolyKnives",.025f);
            Check(guardian.ShadowBurning && lab.HolyKnifeCount==stock-1,"the released blade actually intersects and ignites guardian ink");
            for(int i=0;i<10;i++)Step(.05f);
            Check(guardian.State.Health<health && guardian.ShadowBurning,"the burning silhouette damages the placed guardian over time");
            var tongues=guardian.GetComponentsInChildren<ParticleSystem>().Single(system=>system.name=="Silhouette flames");
            var particles=new ParticleSystem.Particle[tongues.particleCount];
            int particleCount=tongues.GetParticles(particles);
            Check(particleCount>0 && particles.Take(particleCount).All(p=>p.startSize3D.y>=.479f && p.startSize3D.y<=.941f),
                "the actual emitted fires along the guardian's projected shadow are twice their original height");
            GuardianVoice("armour","Hurt");
            yield return Capture("guardian-shadow-burning","fire travels up the projected F01 outline after a real left-hand throw");
            yield return CaptureShadowOverview(target);
            Check(!lab.HasGuardianRelic && lab.GuardianRelicDrop==null,"the hunter has no healing relic before the guardian falls");
            Check(FindShadowProbe(out target,true),"the posed shadow remains reachable for the finishing bolt");
            guardian.State.ReceiveDamage(guardian.State.Health-1);Aim(target);lab.Shoot();
            var death=GuardianVoice("impact","Death");
            Check(death.isPlaying && death.clip.length>2,"the finishing shot starts the full armour-collapse sound");
            Check(!guardian.Alive && lab.GuardianRelicDrop!=null,"a real holy bolt releases the relic on the finishing shadow hit");
            var narrator=lab.GetComponent<FairyTaleNarrator>();
            Check(narrator!=null && !PlayingPickup(narrator) && PendingPickups(narrator)==0,
                "the guardian's death and relic drop do not request the pickup story");
            Vector3 drop=lab.GuardianRelicDrop.transform.position;
            Check(TryFoot(drop,out var foot,out support),"the dropped relic rests on a floor that supports the hunter");
            Aim(drop+Vector3.up*.45f);yield return Capture("guardian-relic-drop","the chest lamp's relic on the playable chapel floor");
            Place(foot);Step(.05f);
            Check(lab.HasGuardianRelic && lab.GuardianRelicCharges==3 && lab.GuardianRelicDrop==null,"approaching the actual relic grants three charges");
            yield return CheckPickupNarration(narrator);
            Aim(originalPosition+Vector3.up*1.65f);
            if(lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            lab.DamageHunter(65);int wounded=lab.HunterHealth;
            Check(lab.FirstBossWoundHint==null,"a chapel wound does not trigger the final-room tutorial");
            Check(lab.UseGuardianRelic() && lab.HunterHealth==wounded+30 && lab.GuardianRelicCharges==2,
                "the Q action immediately heals 30 HP and consumes one charge");
            Step(.15f);
            Check(lab.GuardianHealingEffect!=null && lab.GuardianHealingEffect.GetComponentsInChildren<ParticleSystem>()
                .Any(system=>system.particleCount>0),"the imported heal effect emits visible particles in the actual player");
            yield return Capture("hunter-relic-heal","first-person healing effect and the live Q charge display");
            var ordinary=UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None);
            var first=ordinary.First(l=>lab.GetLampIndex(l)==links[1]).Target;
            var second=ordinary.First(l=>lab.GetLampIndex(l)==links[2]).Target;
            lab.TryIgniteLamp(first);
            Check(lab.GuardianRelicCharges==2 && lab.GuardianRelicRelights==1,"one extinguished chapel lamp supplies half a recharge");
            lab.TryIgniteLamp(first);
            Check(lab.GuardianRelicCharges==2 && lab.GuardianRelicRelights==1,"an already-lit lamp grants no extra charge");
            lab.TryIgniteLamp(second);
            Check(lab.GuardianRelicCharges==3 && lab.GuardianRelicRelights==0,"a second real relight restores the third charge");
            yield return CheckFirstBossWound();
        }
        static bool PlayingPickup(FairyTaleNarrator narrator) => narrator.CurrentId==FairyTaleNarrator.GuardianRelicPickupId;
        static int PendingPickups(FairyTaleNarrator narrator) => narrator.IsPending(FairyTaleNarrator.GuardianRelicPickupId) ? 1 : 0;
        IEnumerator CheckPickupNarration(FairyTaleNarrator narrator) {
            Call(lab.UI,"RefreshLiveHud");
            Check(PendingPickups(narrator)==1 && lab.UI.Snapshot.Toast.Contains("Q 恢复生命"),
                "collecting the real relic requests one story and displays its Q-use prompt");
            lab.Developer.SetFrozen(true);
            Call(narrator,"Advance",60f);
            Check(PendingPickups(narrator)==1 && !PlayingPickup(narrator),"pausing preserves the queued pickup story");
            lab.Developer.SetFrozen(false);
            for(int i=0;i<24 && !PlayingPickup(narrator);i++)Call(narrator,"Advance",20f);
            var voice=(AudioSource)Read(narrator,"voice");
            var parts=narrator.Subtitle(FairyTaleNarrator.GuardianRelicPickupId).Split('\n');
            Check(PlayingPickup(narrator) && parts.Length==2 && narrator.CurrentSubtitle==parts[0] &&
                voice.clip!=null && voice.clip.name==FairyTaleNarrator.GuardianRelicPickupId,
                "the queued pickup starts its first English recording with only the first approved subtitle");
            int firstSample=voice.timeSamples;
            float deadline=Time.realtimeSinceStartup+3f;
            while(voice.timeSamples<=firstSample+256 && Time.realtimeSinceStartup<deadline)yield return null;
            Check(voice.isPlaying && voice.timeSamples>firstSample+256 && PlayingPickup(narrator) &&
                narrator.CurrentSubtitle==parts[0],
                "the real Player audio device advances the English pickup sample cursor while its exact Chinese line is displayed");
            lab.RequestNarration(FairyTaleNarrator.GuardianRelicPickupId);
            Check(PendingPickups(narrator)==0,"a second request while the pickup story plays does not queue it again");
            AudioClip first=voice.clip;
            float remainingFirst=(first.samples-voice.timeSamples)/(float)first.frequency;
            float waitStarted=Time.realtimeSinceStartup;
            deadline=waitStarted+remainingFirst+5f;
            bool firstTextStayed=true;
            while(voice.clip==first && Time.realtimeSinceStartup<deadline) {
                firstTextStayed &= narrator.CurrentSubtitle==parts[0];
                yield return null;
            }
            Check(firstTextStayed && Time.realtimeSinceStartup-waitStarted+.2f>=remainingFirst,
                "the first pickup subtitle stays in place throughout the actual first recording");
            Check(PlayingPickup(narrator) && narrator.CurrentSubtitle==parts[1] && voice.clip!=null &&
                voice.clip.name==FairyTaleNarrator.GuardianRelicPickupId+"_02",
                "pickup line two starts with its own recording only after the previous recording finishes");
            deadline=Time.realtimeSinceStartup+voice.clip.length+5f;
            while(PlayingPickup(narrator) && Time.realtimeSinceStartup<deadline)yield return null;
            lab.RequestNarration(FairyTaleNarrator.GuardianRelicPickupId);
            Check(!PlayingPickup(narrator) && PendingPickups(narrator)==0,"a completed pickup story is remembered");
        }
        IEnumerator CheckFirstBossWound() {
            Vector3 stand=hall.DemonStart+new Vector3(-.81f,0,2.21f);
            stand.y=hall.SurfaceHeight(stand.x,stand.z)+.08f;
            void EnterHall() {
                Place(stand);Step(.02f);Aim(lab.ShadowState.Enemy+Vector3.up*.9f);
                Check(lab.EncounterStarted && lab.FirstBossWoundHint==null,"entering the final room waits for actual damage before prompting");
            }
            EnterHall();
            lab.Developer.HandleKey(KeyCode.F2);
            lab.DamageHunter(10);
            Check(lab.FirstBossWoundHint==null,"health-locked hits do not consume the first-wound tutorial");
            lab.Developer.HandleKey(KeyCode.F2);
            lab.DamageHunter(10);
            support="authored final hall floor";
            yield return Capture("boss-first-wound-relic","first wound in the final room with the relic picked up from the chapel guardian");
            Check((lab.UI.Snapshot.CombatGuides & CombatGuidance.HealWithRelic)!=0 &&
                lab.UI.UiCanvas.GetComponentsInChildren<UnityEngine.UI.Text>().Any(label=>label.text.Contains("按q使用灯心回血")),
                "the persistent healing instruction renders on the battle HUD with a charged relic");
            int injured=lab.HunterHealth;
            Check(lab.UseGuardianRelic() && lab.HunterHealth==injured+30 && lab.FirstBossWoundHint==null,
                "Q heals the first wound and dismisses the tutorial");
            lab.DamageHunter(10);
            Check(lab.FirstBossWoundHint==null && (lab.ActiveCombatGuides & CombatGuidance.HealWithRelic)!=0,
                "later damage restores the persistent guide without repeating the first-wound toast");
            lab.RestartEncounter();
            var narrator=lab.GetComponent<FairyTaleNarrator>();
            lab.RequestNarration(FairyTaleNarrator.GuardianRelicPickupId);
            Check(!PlayingPickup(narrator) && PendingPickups(narrator)==0,"restarting the hunt remembers the completed pickup story");
            EnterHall();lab.DamageHunter(10);Call(lab.UI,"RefreshLiveHud");
            Check(!lab.HasGuardianRelic && lab.UI.Snapshot.Toast=="尚未获得守灯者灯心 · 避开伤害 5 秒后会自动回血",
                "a restarted hunt shows the no-relic instructions on its first surviving wound");
        }
        IEnumerator CaptureShadowOverview(Vector3 reticle) {
            Transform ink=guardian.GetComponentsInChildren<MeshFilter>().First(m=>m.name=="Guardian ground shadow").transform;
            Bounds b=ink.GetComponent<Renderer>().bounds;
            Vector3 start=lab.HunterGroundPosition;
            string oldSupport=support;
            Vector3 focus=Vector3.Lerp(b.center,originalPosition+Vector3.up*2,.35f);
            Vector3 best=default;string bestSupport="";float bestScore=-1;
            var frame=guardian.CurrentFrame();
            for(int i=0;i<18;i++)foreach(float radius in new[]{9f,13f,17f}) {
                float angle=i*Mathf.PI/9;
                Vector3 trial=focus+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
                if(!TryFoot(trial,out var foot,out string floorName))continue;
                frame.Hunter=foot;if(!frame.InsideArena)continue;
                Vector3 eye=foot+Vector3.up*1.65f;
                if(!ClearSight(eye,b.center) || !ClearSight(eye,originalPosition+Vector3.up*4))continue;
                Place(foot);Aim(focus);
                float minX=1,maxX=0,minY=1,maxY=0;
                foreach(Vector3 point in new[]{new Vector3(b.min.x,b.center.y,b.min.z),new Vector3(b.min.x,b.center.y,b.max.z),
                    new Vector3(b.max.x,b.center.y,b.min.z),new Vector3(b.max.x,b.center.y,b.max.z),originalPosition+Vector3.up*guardian.BodyHeight}) {
                    Vector3 screen=lab.Eye.WorldToViewportPoint(point);
                    if(screen.z<=0){minX=-1;break;}
                    minX=Mathf.Min(screen.x,minX);maxX=Mathf.Max(screen.x,maxX);
                    minY=Mathf.Min(screen.y,minY);maxY=Mathf.Max(screen.y,maxY);
                }
                if(minX<.04f || minY<.08f || maxX>.96f || maxY>.92f)continue;
                float score=maxX-minX+.35f*(maxY-minY);
                if(score>bestScore){bestScore=score;best=foot;bestSupport=floorName;}
            }
            if(bestScore>=0) {
                Place(best);Aim(focus);support=bestSupport;
                yield return Capture("guardian-burning-overview","full giant and burning silhouette together from a clear walking position");
            }
            Place(start);Aim(reticle);support=oldSupport;
        }
        void Place(Vector3 foot) {
            var body=(CharacterController)Read(lab,"playerBody");body.enabled=false;body.transform.position=foot;body.enabled=true;body.Move(Vector3.down*.05f);
            lab.ShadowState.Player=body.transform.position;Physics.SyncTransforms();lab.RefreshView();
        }
        void Aim(Vector3 point) {aim=point;var e=Quaternion.LookRotation(point-lab.Eye.transform.position).eulerAngles;Set(lab,"yaw",e.y);Set(lab,"pitch",e.x>180?e.x-360:e.x);lab.RefreshView();}
        void Step(float dt) {Call(lab,"Tick",dt);string action=guardian.State.Action.ToString();if(report.actions.Count==0 || report.actions[report.actions.Count-1]!=action)report.actions.Add(action);}
        IEnumerator Capture(string name,string purpose) {
            Call(lab.UI,"RefreshLiveHud");Canvas.ForceUpdateCanvases();int frames=capture.RenderedFrames+2;float until=Time.realtimeSinceStartup+15;
            while(capture.RenderedFrames<frames && Time.realtimeSinceStartup<until) {if(!string.IsNullOrEmpty(capture.Failure))throw new InvalidOperationException(capture.Failure);yield return null;}
            Check(capture.RenderedFrames>=frames,"two native-Canvas frames rendered before "+name);var image=capture.ReadFrame();
            try {
                var pixels=Chapter01Round3Check.InspectScreen(image,name);Chapter01Round3Check.RequireVisibleScreen(pixels);report.pixels.Add(pixels);
                string file=name+".png";File.WriteAllBytes(Path.Combine(report.evidenceFolder,file),image.EncodeToPNG());
                report.views.Add(new ViewRow {image=file,purpose=purpose,ground=support,action=guardian.State.Action.ToString(),player=lab.HunterGroundPosition,
                    eye=lab.Eye.transform.position,aim=aim,litLamps=guardian.LitLinkedLamps});
            }finally {UnityEngine.Object.Destroy(image);}
        }
        Bounds ArmourBounds(Transform root) {
            bool any=false;Bounds bounds=default;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>()) {
                if(!renderer.enabled || !(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))continue;
                if(!any){bounds=renderer.bounds;any=true;}else bounds.Encapsulate(renderer.bounds);
            }
            Check(any,"imported guardian includes enabled armour mesh bounds");return bounds;
        }
        string FindLampName(int index) {
            foreach(var lamp in UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None))if(lab.GetLampIndex(lamp)==index)return PathOf(lamp.transform);
            return "ordinary lamp "+index;
        }
        void Check(bool condition,string text) {if(!condition)throw new InvalidOperationException(text);report.checks++;report.passed.Add(text);Debug.Log("PASS GUARDIAN_PLACEMENT "+text);}
        void Finish(Exception error) {
            if(completed)return;completed=true;Application.logMessageReceived-=Log;
            if(error==null && report.unexpectedErrors.Count>0)error=new InvalidOperationException(string.Join(" | ",report.unexpectedErrors));
            report.result=error==null?"PASS":"FAIL";report.failure=error?.ToString();report.completedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(report.evidenceFolder,"guardian-placement-check.json"),JsonUtility.ToJson(report,true));
            if(error==null)Debug.Log("GUARDIAN_PLACEMENT_CHECK_PASS checks="+report.checks);else Debug.LogError("GUARDIAN_PLACEMENT_CHECK_FAIL "+error);
            Application.Quit(error==null?0:1);
        }
        static string PathOf(Transform t){string p=t.name;while(t.parent!=null){t=t.parent;p=t.name+"/"+p;}return p;}
        static object Read(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
        static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,Flags).Invoke(o,args);
        static string Argument(string name){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0 && i+1<args.Length?args[i+1]:null;}
    }
}
