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
    // Opt-in Player evidence only. Never attached to or saved in an authored scene.
    public sealed class Chapter01Round5Check : MonoBehaviour
    {
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        [Serializable] sealed class ActorRecord {
            public string path,model;public Vector3 position,localScale;public Bounds modelBounds;
            public int renderers,triangles,linkedLamp;public List<string> materials=new List<string>();
        }
        [Serializable] sealed class AudioRecord {
            public string resource,cue;public int samples,channels,frequency;public float seconds;
        }
        [Serializable] sealed class CueRecord { public string cue;public int count; }
        [Serializable] sealed class ViewRecord {
            public string image,purpose,ground,actor,feedback;public Vector3 player,eye,aim;
            public bool controlledPlacement=true,nativeCanvas=true;public float shotDistance;
        }
        [Serializable] sealed class Report {
            public string result="RUNNING",failure,scene,startedUtc,completedUtc,evidenceFolder,captureMode;
            public string scope="Controlled actual chapter Player smoke, not a manual playthrough, sound recording, listening verdict or FPS benchmark. " +
                "Automatic gameplay Update is disabled; player/camera positions are explicitly chosen on existing scene ground. " +
                "The two authored acolyte root transforms are preserved. Their real clocks open the lamp shield and steal fire; " +
                "a real crossbow shot at a production shadow generates the native HUD message. Boss entry uses the authored gate, " +
                "then the actual ShadowLab.Tick drives natural cues. The separate final audio integration cases deliberately arrange " +
                "transient combat states and reset the hunt; no scene assets or authored placements are saved.";
            public bool controlledClock=true,fullPlaythrough=false,listeningVerified=false;
            public float naturalBossSeconds;public int checks,ordinaryLampCount;
            public List<string> passed=new List<string>(),unexpectedErrors=new List<string>();
            public List<ActorRecord> actors=new List<ActorRecord>();
            public List<AudioRecord> audio=new List<AudioRecord>();
            public List<CueRecord> naturalBossCues=new List<CueRecord>();
            public List<ViewRecord> views=new List<ViewRecord>();
            public List<Chapter01Round3Check.ScreenPixels> screenPixels=new List<Chapter01Round3Check.ScreenPixels>();
        }
        ShadowLab lab;IronHallEncounter hall;Round3FrameCapture capture;Report report;
        FireThiefAcolyte[] actors;Vector3[] originalPositions,originalScales;Quaternion[] originalRotations;
        bool completed;float deadline;string support="";Vector3 aim;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-gameplay-round5")<0)return;
            var runner=new GameObject("Opt-in chapter round 5 model and boss sound evidence").AddComponent<Chapter01Round5Check>();
            runner.report=new Report {startedUtc=DateTime.UtcNow.ToString("o"),evidenceFolder=Argument("-chapter01-evidence") ??
                Path.Combine(Application.persistentDataPath,"GameplayRound5Check")};
            Directory.CreateDirectory(runner.report.evidenceFolder);runner.deadline=Time.realtimeSinceStartup+240;
            Application.logMessageReceived+=runner.ObserveLog;runner.StartCoroutine(runner.Run());
        }
        void Update() {
            if(!completed && deadline>0 && Time.realtimeSinceStartup>deadline) {
                StopAllCoroutines();Finish(new TimeoutException("Round 5 did not finish its controlled Player evidence."));
            }
        }
        void ObserveLog(string message,string trace,LogType type) {
            if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)report.unexpectedErrors.Add(message);
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
            float until=Time.realtimeSinceStartup+25;
            while(Time.realtimeSinceStartup<until) {
                lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
                if(lab!=null && lab.GameplayReady && lab.UI!=null && UnityEngine.Rendering.SplashScreen.isFinished)break;
                yield return null;
            }
            Check(lab!=null && lab.GameplayReady && lab.UI!=null,"actual chapter gameplay and native Canvas initialize");
            lab.enabled=false;hall=lab.GetComponent<IronHallEncounter>();
            Check(hall!=null && hall.UseAuthoredLayout,"actual authored layout is loaded");report.scene=lab.gameObject.scene.name;
            actors=UnityEngine.Object.FindObjectsByType<FireThiefAcolyte>(FindObjectsSortMode.None)
                .Where(a=>a.gameObject.scene==lab.gameObject.scene).OrderBy(a=>PathOf(a.transform)).ToArray();
            Check(actors.Length==2,"the authored scene retains exactly its two placed acolytes");
            originalPositions=actors.Select(a=>a.transform.position).ToArray();
            originalRotations=actors.Select(a=>a.transform.rotation).ToArray();
            originalScales=actors.Select(a=>a.transform.localScale).ToArray();
            BeginBattle();lab.Developer.SetHudHidden(false);
            if(!lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            Check(Resources.Load<GameObject>(FireThiefAcolyte.DefaultBodyResource)!=null,"Player includes the default imported acolyte character resource");
            foreach(var actor in actors)CheckActorModel(actor);
            lab.RefreshOrdinarySceneLamps();report.ordinaryLampCount=UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None)
                .Count(l=>l.Encounter==lab && l.isActiveAndEnabled);
            capture=Round3FrameCapture.Attach(lab);
            report.captureMode=capture.CaptureMode+"; protected-hit image is synchronously rendered on the real shot frame so its finite HUD toast cannot expire while waiting for capture";
            yield return CheckAudioResources();

            var subject=actors.FirstOrDefault(a=>a.Activated && a.Link>=0 && lab.IsOrdinaryLamp(a.Link));
            Check(subject!=null,"an existing placed acolyte has a claimed nearby ordinary lamp");
            Bounds model=ModelBounds(subject.ImportedBodyRoot);
            Check(FindBodyView(subject,model,out var foot),"an existing floor provides a clear close view of the actual imported acolyte");
            Place(foot);subject.Tick(.02f);Aim(model.center);Physics.SyncTransforms();
            Check(subject.Engaged && subject.ProtectedByLamp,"the placed acolyte actually sees the nearby hunter and opens with a lamp shield");
            for(int i=0;i<15;i++)subject.Tick(.04f);
            yield return Capture("round5-acolyte-model","actual placed imported model and first-contact protection",subject,null,0);

            // Advance only this actor's real prayer clock. No direct writes to its shield, lamp, health or HUD.
            for(int i=0;i<160 && !subject.Empowered;i++)subject.Tick(.05f);
            Check(subject.Empowered && subject.ProtectedByLamp && !lab.IsLampLit(subject.Link),
                "the real fire-theft prayer reaches connected purple-fire protection");
            Check(FindShadowShot(subject,out var shotRay,out float hitDistance),
                "a grounded clear aim ray actually hits this protected acolyte's production shadow");
            int health=subject.Health;lab.Weapon.Step(2);
            if(lab.Weapon.Rounds==0) {lab.Weapon.Request(Bolt.Holy);lab.Weapon.Step(2);}
            lab.Shoot();Invoke(lab.UI,"RefreshLiveHud");Canvas.ForceUpdateCanvases();
            var toast=(Text)Field(lab.UI,"vToast");var holder=(GameObject)Field(lab.UI,"vToastObject");
            Check(lab.LastShot==AcolyteProtectionFeedback.PurpleFlame && toast.text==lab.LastShot && holder.activeInHierarchy,
                "the actual crossbow hit reaches the visible native HUD with the purple-flame instruction");
            Check(subject.Health==health && (float)Field(lab.UI,"hitUntil")<=Time.time,
                "the protected hit preserves health and does not flash a successful-damage reticle");
            Check(Mathf.Abs((float)Field(lab.UI,"toastUntil")-Time.time-3.2f)<.05f,
                "the instruction uses its finite 3.2-second display time");
            // Real native Canvas + scene rendering, same frame as the real shot; no direct toast injection.
            Invoke(capture,"RenderFrame");
            SaveFrame("round5-acolyte-protected-shadow","real crossbow hit against purple-fire protection",subject,lab.LastShot,hitDistance);
            yield return CheckAnimatedAcolyte(subject);
            CheckActorTransforms();

            BeginBattle();
            yield return CheckNaturalBossAudio();
            foreach(string row in DemonAudioIntegrationCases.Run(lab))Check(true,row);
            CheckActorTransforms();
            BeginBattle();
            yield return CheckAuthoredGhosts();
            yield return null; // Complete pending Destroy operations before writing the final report.
        }
        IEnumerator CheckAuthoredGhosts() {
            var swarm=UnityEngine.Object.FindObjectsByType<GroundShadowSwarm>(FindObjectsSortMode.None)
                .First(s=>s.Encounter==lab && s.Activated && s.Alive && s.gameObject.scene==lab.gameObject.scene);
            var ghost=swarm.transform.Find("Ground shadow 1");
            Check(ghost!=null && lab.TryMinorGround(ghost.position,ghost.position.y-swarm.FloatHeight,out var floor) &&
                Mathf.Abs(ghost.position.y-floor.y-1.75f)<.12f,"the placed ghost floats 1.75 m above its actual storey");
            var filter=swarm.GetComponentsInChildren<MeshFilter>().First(m=>m.name=="Shootable ground shadow 1");
            var mesh=filter.sharedMesh;var verts=mesh.vertices;var tri=mesh.triangles;
            var points=new List<Vector3>();
            for(int i=0;i<tri.Length;i+=3)points.Add(filter.transform.TransformPoint((verts[tri[i]]+verts[tri[i+1]]+verts[tri[i+2]])/3f));
            Vector3 center=filter.GetComponent<Renderer>().bounds.center;
            var candidates=points.OrderBy(p=>(p-center).sqrMagnitude).Take(100).ToArray();
            var source=swarm.BodyPrefab.GetComponent<MeshFilter>().sharedMesh;
            var bodies=swarm.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh==source)
                .Select(f=>f.GetComponent<MeshRenderer>()).ToArray();
            Check(bodies.Length==swarm.LivingCount && source.name=="Hollow Wisp" &&
                source.triangles.Length/3<500 && bodies.All(r=>r.enabled && r.sharedMaterial.shader.isSupported &&
                    r.sharedMaterial.renderQueue>=3000 && r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off &&
                    r.GetComponent<Collider>()==null),"each living ghost renders its translucent low-poly mantle without a body hitbox");
            foreach(var seed in RingCandidates(ghost.position,new[]{4.5f,6f,8f})) {
                if(!TryFoot(seed,ghost.position.y-swarm.FloatHeight,out var foot,out var ground))continue;
                Place(foot);
                var eye=lab.Eye.transform.position;
                if(!ClearSight(eye,ghost.position,swarm.transform))continue;
                foreach(var point in candidates) {
                    var ray=new Ray(eye,(point-eye).normalized);
                    if(!FirstSurface(ray,out var hit) || !hall.IsGround(hit.collider) ||
                        bodies.Any(r=>r.bounds.IntersectRay(ray,out float distance) && distance<hit.distance))continue;
                    Aim(point);ray=lab.Eye.ViewportPointToRay(new Vector3(.5f,.5f,0));
                    Vector3 bodyScreen=lab.Eye.WorldToViewportPoint(ghost.position);
                    if(bodyScreen.z<0 || bodyScreen.x<.1f || bodyScreen.x>.9f || bodyScreen.y<.16f || bodyScreen.y>.9f ||
                        !swarm.TryHitShadow(ray,hit.distance+.1f,out float shadowHit))continue;
                    support=ground;
                    yield return Capture("ghost-hover-and-shadow","grounded first-person reticle on exposed ink, hovering ghost in view",null,null,shadowHit);
                    int count=swarm.LivingCount;lab.Shoot();
                    Check(swarm.LivingCount==count-1,"a normal crossbow shot along that reticle removes exactly one ghost");
                    yield break;
                }
            }
            throw new InvalidOperationException("No grounded camera can aim at this placed ghost's visible shadow.");
        }
        void BeginBattle() {
            lab.RestartEncounter();SetField(lab.UI,"sessionStarted",true);Invoke(lab.UI,"SetView","battle",null);
            lab.Developer.ClearPauseReasons();lab.Eye.GetComponent<ShiyinAudio>()?.SetPaused(false);Physics.SyncTransforms();
        }
        IEnumerator CheckAnimatedAcolyte(FireThiefAcolyte subject) {
            Transform visual=subject.ImportedBodyRoot;
            var skin=visual.GetComponentInChildren<SkinnedMeshRenderer>();
            Check(skin!=null && skin.enabled && skin.bones.Length>=15,"the game displays the weighted Lux3D acolyte");
            var hand=skin.bones.First(t=>t!=null && t.name=="RightHand");
            var foot=skin.bones.First(t=>t!=null && t.name=="LeftFoot");
            Vector3 Local(Transform bone)=>visual.InverseTransformPoint(bone.position);
            var lamp=UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None)
                .First(l=>lab.GetLampIndex(l)==subject.Link);
            Check(lab.TryIgniteLamp(lamp.Target) && subject.Action==FireThiefCycle.Action.Stagger,"relighting the claimed lamp starts the acolyte's stagger");
            subject.Tick(.12f);
            Aim(visual.position+Vector3.up);
            yield return Capture("acolyte-stagger","lamp interrupt reaction",subject,null,0);
            bool found=false;
            foreach(var candidate in RingCandidates(visual.position,new[]{11f,13f})) {
                if(!TryFoot(candidate,visual.position.y,out var ground,out var supportName))continue;
                if(!ClearSight(ground+Vector3.up*1.65f,visual.position+Vector3.up,subject.transform))continue;
                Place(ground);support=supportName;found=true;break;
            }
            Check(found,"clear grounded position for observing an actual approach");
            bool movie=Array.IndexOf(Environment.GetCommandLineArgs(),"-capture-acolyte-motion")>=0;
            int movieFrame=0;
            void SaveMotionFrame() {
                Aim(subject.FootPosition+Vector3.up*1.06f);
                Invoke(capture,"RenderFrame");
                var frame=capture.ReadFrame();
                try {
                    var folder=Path.Combine(report.evidenceFolder,"motion-frames");
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder,(movieFrame++).ToString("D4")+".jpg"),frame.EncodeToJPG(86));
                } finally { UnityEngine.Object.Destroy(frame); }
            }
            Vector3 start=visual.position,previousFoot=Local(foot);
            float footTravel=0;
            for(int i=0;i<220 && (Vector3.Distance(start,visual.position)<.48f || footTravel<.06f);i++) {
                subject.Tick(.04f);
                Vector3 next=Local(foot); footTravel+=Vector3.Distance(next,previousFoot);previousFoot=next;
                if(movie && i%2==0 && subject.Action==FireThiefCycle.Action.Waiting)SaveMotionFrame();
            }
            Check(Vector3.Distance(start,visual.position)>.4f && footTravel>.06f,"combat body approaches while the weighted foot moves relative to the body");
            Aim(visual.position+Vector3.up);yield return Capture("acolyte-walk","walking during the recovery interval",subject,null,0);
            for(int i=0;i<180 && subject.Action!=FireThiefCycle.Action.Windup;i++) {
                subject.Tick(.04f);if(movie && i%2==0)SaveMotionFrame();
            }
            Check(subject.Action==FireThiefCycle.Action.Windup,"the acolyte naturally begins its next aimed cast");
            Vector3 windupFoot=visual.position,initialHand=Local(hand);
            for(int i=0;i<120 && subject.Action==FireThiefCycle.Action.Windup;i++) {
                subject.Tick(.04f);if(movie && i%2==0)SaveMotionFrame();
            }
            Check(Vector3.Distance(windupFoot,visual.position)<.015f &&
                Vector3.Distance(initialHand,Local(hand))>.10f && ((System.Collections.ICollection)Field(subject,"projectiles")).Count>0,
                "cast plants the feet, moves the weighted casting hand and releases a projectile");
            Aim(visual.position+Vector3.up*1.15f);yield return Capture("acolyte-cast","hand pose at the actual projectile release",subject,null,0);
            if(movie)for(int i=0;i<8;i++) {subject.Tick(.08f);SaveMotionFrame();}
            for(int shots=0;shots<8 && subject.Alive;shots++) {
                Check(FindShadowShot(subject,out var ray,out float distance),"visible walking actor's projected shadow can be targeted");
                subject.ReceiveShadowHit(Bolt.Holy,ray.GetPoint(distance));
            }
            Check(!subject.Alive && visual.gameObject.activeInHierarchy,"a lethal shadow hit starts a visible death pose");
            subject.Tick(.38f);Aim(visual.position+Vector3.up*.72f);
            yield return Capture("acolyte-fall","death pose after a lethal shadow hit",subject,null,0);
            for(int i=0;i<100 && visual.gameObject.activeInHierarchy;i++)subject.Tick(.04f);
            Check(!visual.gameObject.activeInHierarchy,"the body disappears after the death motion finishes");
        }
        void CheckActorModel(FireThiefAcolyte actor) {
            var model=actor.ImportedBodyRoot;
            Check(model!=null && model.gameObject.activeInHierarchy,"placed acolyte owns an active imported body: "+PathOf(actor.transform));
            Check(!actor.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("Development Placeholder",StringComparison.Ordinal)),
                "placed acolyte has no development-placeholder hierarchy: "+actor.name);
            var meshes=model.GetComponentsInChildren<Renderer>(true).Where(r=>r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
            Check(meshes.Length>0,"imported body has real mesh renderers: "+actor.name);
            var row=new ActorRecord {path=PathOf(actor.transform),model=PathOf(model),position=actor.transform.position,
                localScale=actor.transform.localScale,modelBounds=ModelBounds(model),renderers=meshes.Length,linkedLamp=actor.Link};
            foreach(var renderer in meshes) {
                Check(renderer.enabled && renderer.sharedMaterials.Length>0,"imported renderer is enabled and has material slots: "+renderer.name);
                foreach(var material in renderer.sharedMaterials) {
                    Check(material!=null && material.shader!=null && material.shader.isSupported &&
                        material.shader.name!="Hidden/InternalErrorShader","imported material has a supported Player shader: "+renderer.name);
                    row.materials.Add(material.name+" | "+material.shader.name);
                }
                Mesh mesh=renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Check(mesh!=null && mesh.vertexCount>0,"imported renderer retains nonempty geometry: "+renderer.name);
                for(int i=0;i<mesh.subMeshCount;i++)if(mesh.GetTopology(i)==MeshTopology.Triangles)row.triangles+=(int)(mesh.GetIndexCount(i)/3);
            }
            Check(Finite(row.modelBounds.center) && Finite(row.modelBounds.extents) && row.modelBounds.size.y>1.75f && row.modelBounds.size.y<2.25f,
                "placed imported model retains the intended approximately two-metre world height: "+actor.name);
            report.actors.Add(row);
        }
        IEnumerator CheckAudioResources() {
            var clips=Resources.LoadAll<AudioClip>("DemonSfx");
            foreach(DemonSound cue in Enum.GetValues(typeof(DemonSound))) {
                var family=clips.Where(c=>c!=null && c.name.StartsWith(cue+"_",StringComparison.Ordinal)).ToArray();
                Check(family.Length>0,"recorded boss cue family is packaged: "+cue);
                foreach(var clip in family) {
                    clip.LoadAudioData();float until=Time.realtimeSinceStartup+5;
                    while(clip.loadState==AudioDataLoadState.Loading && Time.realtimeSinceStartup<until)yield return null;
                    Check(clip.loadState==AudioDataLoadState.Loaded && clip.samples>0 && clip.channels>0 && clip.frequency>0,
                        "recorded boss audio data loads: "+clip.name);
                    report.audio.Add(new AudioRecord {resource="DemonSfx/"+clip.name,cue=cue.ToString(),samples=clip.samples,
                        channels=clip.channels,frequency=clip.frequency,seconds=clip.length});
                }
            }
            var audio=lab.Eye.GetComponent<ShiyinAudio>();Check(audio!=null && audio.DemonSoundsAvailable,"production audio router finds all recorded boss cue families");
        }
        IEnumerator CheckNaturalBossAudio() {
            var audio=lab.Eye.GetComponent<ShiyinAudio>();
            var cues=(DemonSound[])Enum.GetValues(typeof(DemonSound));int[] before=cues.Select(audio.DemonSoundCount).ToArray();
            Check(hall.FogGate!=null,"actual chapter includes its authored fog-gate entry");
            Vector3 requested=hall.FogGate.FloorCenter+hall.FogGate.Inward*(hall.FogGate.EntryClearance+1.8f);
            bool placed=false;
            foreach(var seed in RingCandidates(requested,new[]{0f,.65f,1.2f})) {
                if(!TryFoot(seed,requested.y,out var foot,out var ground) || !hall.ShouldStartEncounter(foot))continue;
                support=ground;Place(foot);placed=true;break;
            }
            Check(placed,"a grounded viewpoint inside the real authored gate entry exists");
            Invoke(lab,"TryBeginEncounter");Check(lab.EncounterStarted,"the actual gate logic starts the final boss encounter");
            Invoke(lab,"Tick",.05f);
            Check(audio.DemonSoundCount(DemonSound.Arrival)==before[(int)DemonSound.Arrival]+1,
                "actual ShadowLab.Tick after authored gate entry emits the arrival cue once");
            DemonSound[] tells={DemonSound.ClawTell,DemonSound.RiftTell,DemonSound.BurstTell,DemonSound.LungeTell,DemonSound.MarkTell};
            DemonSound[] releases={DemonSound.Sweep,DemonSound.Impact,DemonSound.BodyImpact,DemonSound.Bell};
            bool Has(DemonSound[] group)=>group.Any(c=>audio.DemonSoundCount(c)>before[(int)c]);
            for(int i=0;i<400 && (!Has(tells) || !Has(releases));i++) {
                Invoke(lab,"Tick",.1f);report.naturalBossSeconds+=.1f;
                if(i%10==0) {Aim(lab.ShadowState.Enemy+Vector3.up*1.2f);yield return null;}
            }
            Check(Has(tells) && Has(releases),"actual boss gameplay clock naturally emits a telegraph and an attack-release/impact cue");
            Check(audio.DemonSoundCount(DemonSound.Arrival)==before[(int)DemonSound.Arrival]+1,
                "natural boss ticks do not replay arrival every frame");
            foreach(var cue in cues)report.naturalBossCues.Add(new CueRecord {cue=cue.ToString(),count=audio.DemonSoundCount(cue)-before[(int)cue]});
            var voices=lab.GetComponentsInChildren<AudioSource>(true)
                .Where(s=>s.name.StartsWith("Shadow demon voice",StringComparison.Ordinal)).ToArray();
            Check(voices.Length>0 && voices.Length<=6,"real boss playback uses its bounded world-space source pool");
            foreach(var voice in voices)Check(voice.spatialBlend==1 && voice.rolloffMode==AudioRolloffMode.Linear &&
                voice.maxDistance>voice.minDistance && !float.IsInfinity(voice.maxDistance) && !float.IsNaN(voice.maxDistance) &&
                voice.maxDistance>=Mathf.Max(65,hall.PlayArea.size.magnitude*2.5f)-.01f && Finite(voice.transform.position) &&
                voice.transform.parent!=lab.Eye.transform,"boss source has finite room-covering linear attenuation in world space: "+voice.name);
            Aim(lab.ShadowState.Enemy+Vector3.up*1.2f);
            yield return Capture("round5-boss-audio-hooks","actual authored gate encounter after natural gameplay sound triggers",null,null,0);
        }
        bool FindBodyView(FireThiefAcolyte actor,Bounds model,out Vector3 foot) {
            foreach(var seed in RingCandidates(actor.transform.position,new[]{3.5f,4.5f,6f})) {
                if(!TryFoot(seed,actor.transform.position.y,out var grounded,out var ground))continue;
                if(!ClearSight(grounded+Vector3.up*1.65f,model.center,actor.transform))continue;
                support=ground;foot=grounded;return true;
            }
            foot=default;return false;
        }
        bool FindShadowShot(FireThiefAcolyte actor,out Ray ray,out float distance) {
            var points=new List<Vector3>();
            foreach(var filter in actor.GetComponentsInChildren<MeshFilter>()) {
                if(!filter.name.Contains("Shootable") || filter.sharedMesh==null)continue;
                var vertices=filter.sharedMesh.vertices;var triangles=filter.sharedMesh.triangles;
                for(int i=0;i<triangles.Length;i+=3) {
                    Vector3 point=filter.transform.TransformPoint((vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3);
                    if(Vector3.ProjectOnPlane(point-actor.transform.position,Vector3.up).sqrMagnitude>.65f*.65f)points.Add(point);
                }
            }
            points=points.OrderBy(p=>(p-actor.transform.position).sqrMagnitude).Take(320).ToList();
            var seeds=new List<Vector3>{lab.HunterGroundPosition};
            seeds.AddRange(RingCandidates(actor.transform.position,new[]{3.5f,5f,7f}));
            foreach(var seed in seeds) {
                if(!TryFoot(seed,actor.transform.position.y,out var foot,out var ground))continue;
                Place(foot);
                foreach(var point in points) {
                    // Search analytically; changing the production pose for every
                    // triangle would repeatedly rebuild unrelated boss shadows.
                    var candidate=new Ray(lab.Eye.transform.position,(point-lab.Eye.transform.position).normalized);
                    if(!FirstSurface(candidate,out var surface) || !hall.IsGround(surface.collider) || surface.normal.y<.5f)continue;
                    if(!actor.TryHitShadow(candidate,surface.distance+.1f,out float hit))continue;
                    bool earlier=false;
                    foreach(var other in actors)if(other!=actor && other.Activated && other.Alive &&
                        other.TryHitShadow(candidate,hit,out float otherHit) && otherHit<hit) {earlier=true;break;}
                    if(earlier)continue;
                    Aim(point);candidate=lab.Eye.ViewportPointToRay(new Vector3(.5f,.5f,0));
                    if(!FirstSurface(candidate,out surface) || !actor.TryHitShadow(candidate,surface.distance+.1f,out hit))continue;
                    support=ground;ray=candidate;distance=hit;return true;
                }
            }
            ray=default;distance=0;return false;
        }
        IEnumerable<Vector3> RingCandidates(Vector3 center,float[] radii) {
            foreach(float radius in radii)for(int i=0;i<(radius==0?1:16);i++) {
                float angle=i*Mathf.PI/8;yield return center+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
            }
        }
        bool TryFoot(Vector3 requested,float referenceHeight,out Vector3 foot,out string ground) {
            var body=(CharacterController)Field(lab,"playerBody");
            foreach(var hit in Physics.RaycastAll(new Vector3(requested.x,referenceHeight+3,requested.z),Vector3.down,6,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)) {
                if(!hall.IsGround(hit.collider) || hit.normal.y<.7f || Mathf.Abs(hit.point.y-referenceHeight)>2)continue;
                Vector3 p=hit.point+Vector3.up*.04f;
                if(Physics.OverlapCapsule(p+Vector3.up*.38f,p+Vector3.up*1.42f,.34f,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Any(c=>c!=body))continue;
                foot=p;ground=PathOf(hit.transform);return true;
            }
            foot=default;ground="";return false;
        }
        bool ClearSight(Vector3 origin,Vector3 target,Transform subject) {
            var body=(CharacterController)Field(lab,"playerBody");
            return !Physics.RaycastAll(origin,(target-origin).normalized,Vector3.Distance(origin,target)-.05f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Any(h=>h.collider!=body && !h.transform.IsChildOf(subject));
        }
        void Place(Vector3 foot) {
            var body=(CharacterController)Field(lab,"playerBody");body.enabled=false;body.transform.position=foot;body.enabled=true;body.Move(Vector3.down*.05f);
            lab.ShadowState.Player=body.transform.position;Physics.SyncTransforms();lab.RefreshView();
        }
        void Aim(Vector3 point) {
            aim=point;var e=Quaternion.LookRotation(point-lab.Eye.transform.position).eulerAngles;
            SetField(lab,"yaw",e.y);SetField(lab,"pitch",e.x>180?e.x-360:e.x);lab.RefreshView();
        }
        bool FirstSurface(Ray ray,out RaycastHit hit) {
            object[] args={ray,default(RaycastHit)};bool found=(bool)Invoke(lab,"FirstShotSurface",args);hit=(RaycastHit)args[1];return found;
        }
        static Bounds ModelBounds(Transform model) {
            Bounds b=default;bool any=false;
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true)) {
                if(!renderer.enabled || !(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))continue;
                if(!any) {b=renderer.bounds;any=true;}else b.Encapsulate(renderer.bounds);
            }
            if(!any)throw new InvalidOperationException("Imported model has no visible mesh bounds.");return b;
        }
        void CheckActorTransforms() {
            for(int i=0;i<actors.Length;i++)Check(Vector3.Distance(actors[i].transform.position,originalPositions[i])<.00001f &&
                Quaternion.Angle(actors[i].transform.rotation,originalRotations[i])<.001f && actors[i].transform.localScale==originalScales[i],
                "authored acolyte root position, rotation and scale remain unchanged: "+actors[i].name);
        }
        IEnumerator Capture(string name,string purpose,FireThiefAcolyte actor,string feedback,float distance) {
            Invoke(lab.UI,"RefreshLiveHud");Canvas.ForceUpdateCanvases();
            int target=capture.RenderedFrames+2;float until=Time.realtimeSinceStartup+15;
            while(capture.RenderedFrames<target && Time.realtimeSinceStartup<until) {
                if(!string.IsNullOrEmpty(capture.Failure))throw new InvalidOperationException(capture.Failure);yield return null;
            }
            Check(capture.RenderedFrames>=target,"two completed native-Canvas offscreen frames precede "+name);
            SaveFrame(name,purpose,actor,feedback,distance);
        }
        void SaveFrame(string name,string purpose,FireThiefAcolyte actor,string feedback,float distance) {
            Texture2D image=capture.ReadFrame();
            try {
                var pixels=Chapter01Round3Check.InspectScreen(image,name);Chapter01Round3Check.RequireVisibleScreen(pixels);
                report.screenPixels.Add(pixels);string file=name+".png";
                File.WriteAllBytes(Path.Combine(report.evidenceFolder,file),image.EncodeToPNG());
                report.views.Add(new ViewRecord {image=file,purpose=purpose,ground=support,actor=actor!=null?PathOf(actor.transform):"",
                    feedback=feedback,player=lab.HunterGroundPosition,eye=lab.Eye.transform.position,aim=aim,shotDistance=distance});
            } finally {if(image!=null)UnityEngine.Object.Destroy(image);}
        }
        void Check(bool condition,string message) {
            if(!condition)throw new InvalidOperationException(message);report.checks++;report.passed.Add(message);Debug.Log("PASS ROUND5 "+message);
        }
        void Finish(Exception error) {
            if(completed)return;completed=true;Application.logMessageReceived-=ObserveLog;
            if(error==null && report.unexpectedErrors.Count>0)error=new InvalidOperationException(string.Join(" | ",report.unexpectedErrors));
            report.result=error==null?"PASS":"FAIL";report.failure=error?.ToString();report.completedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(report.evidenceFolder,"round5-check.json"),JsonUtility.ToJson(report,true));
            if(error==null)Debug.Log("GAMEPLAY_ROUND5_CHECK_PASS checks="+report.checks);else Debug.LogError("GAMEPLAY_ROUND5_CHECK_FAIL "+error);
            Application.Quit(error==null?0:1);
        }
        static bool Finite(Vector3 p)=>!float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
        static string PathOf(Transform node) {string path=node.name;while(node.parent!=null) {node=node.parent;path=node.name+"/"+path;}return path;}
        static object Field(object owner,string name)=>owner.GetType().GetField(name,Instance).GetValue(owner);
        static void SetField(object owner,string name,object value)=>owner.GetType().GetField(name,Instance).SetValue(owner,value);
        static object Invoke(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,Instance).Invoke(owner,args);
        static string Argument(string name) {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0 && i+1<args.Length?args[i+1]:null;}
    }
}
