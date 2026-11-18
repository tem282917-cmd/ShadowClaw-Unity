using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // An explicit, controlled player acceptance. Never attached to a saved scene.
    public sealed class Chapter01Round3Check : MonoBehaviour
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] sealed class ActorTiming {
            public string name,kind;public int calls;public double totalMilliseconds,maxMilliseconds,p95Milliseconds;
            public int shadowBuilds,shadowReuses;
            public string shadowCacheDiagnostic;
        }
        [Serializable] public sealed class ScreenPixels {
            public string name;public int width,height,samples,visibleSamples,minimumLuminance,maximumLuminance;
        }
        [Serializable] sealed class WickPixels {
            public string frame;
            public RectInt region;
            public int warm,violet;
        }
        [Serializable] sealed class Report {
            public string result="RUNNING",failure,scene,startedUtc,completedUtc,evidenceFolder;
            public string captureMode="controlled offscreen Eye render followed by separate native Canvas composition; not a screen capture";
            public string imageScope="Offscreen frames validate world/HUD imagery under controlled rendering. No complete gameplay FPS claim.";
            public string acolyte,lamp,standingGround,firstHit,blockedBy,blockedGround;
            public int checks,placedAcolytes,ordinaryLamps,initialKnives,afterRelight,afterDuplicate,afterDarkKnife,afterLitKnife;
            public Vector3 standingPoint,aimPoint,blockedStandingPoint;
            public bool firstHitWasHousing,controlledPlayerPlacement=true,controlledClock=true,controlledNoLampRadius=true,fullPlaythrough=false,ambientBenchmarkRequested;
            public string scope="Actual authored acolyte/lamp, grounded player capsule, camera first-hit ordering, crossbow and production E-action. " +
                "Acolyte clocks are advanced explicitly; a no-lamp case temporarily sets its search radius to zero. " +
                "Shadow protection calls the actual rendered shadow ray/hit path. The stair helper creates disposable runtime fixtures. " +
                "No authored enemy root placement is changed or added; swarm members may pursue through their normal Tick. This is not a manual playthrough.";
            public List<string> passed=new List<string>(),screenshots=new List<string>(),unexpectedErrors=new List<string>();
            public List<ActorTiming> actorTickCpu=new List<ActorTiming>();
            public List<ScreenPixels> screenPixels=new List<ScreenPixels>();
            public List<WickPixels> wickPixels=new List<WickPixels>();
            public string timingScope="Stopwatch CPU elapsed around each actual authored GroundShadowSwarm/FireThiefAcolyte Tick only. Includes 30 consecutive 1/60-second ticks per placed actor and explicitly stepped combat phases; excludes screen rendering/GPU and is not a frame-time benchmark. No arbitrary performance pass threshold.";
        }
        readonly Dictionary<ShadowEncounterActor,List<double>> tickSamples=new Dictionary<ShadowEncounterActor,List<double>>();
        EncounterLantern[] actualLamps=Array.Empty<EncounterLantern>();
        ShadowLab lab;Report report;Round3FrameCapture frameCapture;bool completed;float deadline;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-gameplay-round3")<0)return;
            var runner=new GameObject("Opt-in chapter round 3 acceptance").AddComponent<Chapter01Round3Check>();
            runner.report=new Report {startedUtc=DateTime.UtcNow.ToString("o"),evidenceFolder=Argument("-chapter01-evidence") ??
                Path.Combine(Application.persistentDataPath,"GameplayRound3Check")};
            Directory.CreateDirectory(runner.report.evidenceFolder);runner.deadline=Time.realtimeSinceStartup+240;
            Application.logMessageReceived+=runner.ObserveLog;runner.StartCoroutine(runner.Run());
        }
        void Update() {
            if(!completed && deadline>0 && Time.realtimeSinceStartup>deadline) {
                StopAllCoroutines();Finish(new TimeoutException("Round 3 timed out; unfinished screen frames or benchmark work are not claimed passed."));
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
            if(error!=null && frameCapture!=null) {
                yield return null;
                try {CapturePixels("round3-failure");}catch(Exception captureError) {report.unexpectedErrors.Add("Failure capture: "+captureError.Message);}
            }
            Finish(error);
        }
        IEnumerator Exercise() {
            float until=Time.realtimeSinceStartup+15;
            while(Time.realtimeSinceStartup<until) {
                lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
                if(lab!=null && lab.GameplayReady && lab.UI!=null && UnityEngine.Rendering.SplashScreen.isFinished)break;
                yield return null;
            }
            Check(lab!=null && lab.GameplayReady && lab.UI!=null,"the actual chapter gameplay and native UI initialize");
            lab.enabled=false; // Explicitly step only the actor/action under test.
            frameCapture=Round3FrameCapture.Attach(lab);report.captureMode=frameCapture.CaptureMode;
            var hall=lab.GetComponent<IronHallEncounter>();report.scene=lab.gameObject.scene.name;
            Check(hall!=null && hall.UseAuthoredLayout,"the authored main chapter is loaded");
            lab.RestartEncounter();typeof(ShiyinUI).GetField("sessionStarted",Private).SetValue(lab.UI,true);
            Invoke(lab.UI,"SetView","battle",null);lab.Developer.ClearPauseReasons();
            if(!lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            lab.RefreshOrdinarySceneLamps();Physics.SyncTransforms();
            var lamps=UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None)
                .Where(x=>x.Encounter==lab && x.isActiveAndEnabled).ToArray();
            actualLamps=lamps;
            var actors=UnityEngine.Object.FindObjectsByType<FireThiefAcolyte>(FindObjectsSortMode.None)
                .Where(x=>x.Encounter==lab && x.isActiveAndEnabled && x.Activated && x.Alive).ToArray();
            report.ordinaryLamps=lamps.Length;report.placedAcolytes=actors.Length;
            Check(lamps.Any(x=>x.RuntimeSceneAdapter) && actors.Length>0,"real ordinary adapters and the author's placed acolytes exist");
            CheckLampMovement(hall,lamps);
            foreach(var actor in actors)TickActor(actor,.01f);
            var claimed=new HashSet<int>();
            foreach(var actor in actors)Check(actor.Link<0 || actor.Link>=HuntBoss.LampCapacity && claimed.Add(actor.Link),
                "actual acolyte claim is exclusive and excludes boss lamps: "+PathOf(actor.transform));

            FireThiefAcolyte selectedActor=null;EncounterLantern selectedLamp=null;Vector3 foot=default,aim=default;string ground="";
            foreach(var actor in actors.Where(x=>x.Link>=0)) {
                var lamp=lamps.FirstOrDefault(x=>lab.GetLampIndex(x)==actor.Link);
                if(lamp!=null && lamp.EffectiveLight!=null && lamp.EffectiveLight.name.StartsWith("WarmPool__D01__",StringComparison.Ordinal) &&
                    FindPosition(hall,lamp,actor,true,false,out foot,out aim,out ground) &&
                    FindPosition(hall,lamp,null,false,true,out _,out _,out _)) {selectedActor=actor;selectedLamp=lamp;break;}
            }
            Check(selectedActor!=null,"a real claimed ordinary lamp has a grounded, capsule-clear shot whose first hit is its housing");
            var acolyte=selectedActor;var chosen=selectedLamp;float oldRadius=acolyte.LampSearchRadius;
            report.acolyte=PathOf(acolyte.transform);report.lamp=PathOf(chosen.transform);
            report.standingPoint=foot;report.aimPoint=aim;report.standingGround=ground;
            try {
                PlaceHunter(foot,aim);
                var actualMinors=UnityEngine.Object.FindObjectsByType<ShadowEncounterActor>(FindObjectsSortMode.None)
                    .Where(x=>x.Encounter==lab && x.isActiveAndEnabled && x.Activated && x.Alive && (x is GroundShadowSwarm || x is FireThiefAcolyte)).ToArray();
                for(int sample=0;sample<30;sample++)foreach(var actor in actualMinors)TickActor(actor,1f/60f);
                acolyte.ResetEncounter();TickActor(acolyte,.01f);
                Check(acolyte.Link==lab.GetLampIndex(chosen) && acolyte.Engaged,"chosen actual acolyte retains its nearby claimed lamp and engages from the firing position");
                Check(FirstSurface(CameraRay(),out var first) && chosen.OwnsInteractionCollider(first.collider) && first.collider!=chosen.Target,
                    "the real player camera first hits an InteractionSurfaces housing collider, not the wick trigger");
                report.firstHit=PathOf(first.collider.transform);report.firstHitWasHousing=true;
                report.initialKnives=lab.HolyKnifeCount;Check(report.initialKnives==2 && lab.Weapon.Loaded==Bolt.Holy,"initial inventory is two knives and the holy-water crossbow");
                chosen.Extinguish();Check(Dormant(chosen) && !chosen.Lit,"extinction produces the existing purple dormant flame");
                yield return Capture("round3-00-real-lamp-purple-housing-aim");
                Shoot();report.afterRelight=lab.HolyKnifeCount;
                Check(chosen.Lit && !Dormant(chosen) && report.afterRelight==3,"holy crossbow housing hit restores warm fire and grants exactly one knife");
                Shoot();report.afterDuplicate=lab.HolyKnifeCount;
                Check(chosen.Lit && report.afterDuplicate==report.afterRelight,"shooting the already lit housing cannot farm a second knife");
                chosen.Extinguish();Knife();report.afterDarkKnife=lab.HolyKnifeCount;
                Check(chosen.Lit && !Dormant(chosen) && report.afterDarkKnife==report.afterDuplicate,"production E-action relights the same housing and refunds its single spent knife");
                Knife();report.afterLitKnife=lab.HolyKnifeCount;
                Check(chosen.Lit && report.afterLitKnife==report.afterDarkKnife-1,"E-action on already warm fire spends one knife without a reward");
                yield return Capture("round3-01-real-lamp-warm-inventory");

                Check(FindPosition(hall,chosen,null,false,true,out var blockedFoot,out var blockedAim,out var blockedGround),
                    "a real wall-obstructed grounded firing position exists for the same lamp");
                PlaceHunter(blockedFoot,blockedAim);Check(FirstSurface(CameraRay(),out var obstruction) && IsWall(hall,chosen,obstruction),
                    "real level geometry is the first hit before the hidden lamp");
                report.blockedStandingPoint=blockedFoot;report.blockedBy=PathOf(obstruction.collider.transform);report.blockedGround=blockedGround;
                chosen.Extinguish();int before=lab.HolyKnifeCount;Shoot();
                Check(!chosen.Lit && lab.HolyKnifeCount==before,"a crossbow arrow cannot ignite or reward a lamp through a real wall");
                Knife();Check(!chosen.Lit && lab.HolyKnifeCount==before-1,"E-action stops at the wall and cannot remotely relight the hidden lamp");
                yield return Capture("round3-02-real-wall-blocks-lamp");

                PlaceHunter(foot,aim);chosen.Ignite();acolyte.ResetEncounter();
                Bounds startingTarget=chosen.Target.bounds;float startingRange=chosen.EffectiveLight.range;
                Color warmLight=chosen.EffectiveLight.color;float startingIntensity=chosen.EffectiveLight.intensity;
                CheckFlameSize(chosen);
                yield return Capture("thief-lamp-00-before",chosen,false);
                TickActor(acolyte,.01f);
                var cycle=(FireThiefCycle)Field(acolyte,"cycle");
                Check(acolyte.Engaged && acolyte.Action==FireThiefCycle.Action.Prayer && acolyte.ProtectedByLamp &&
                    cycle.AttacksReleased==0 && ((IList)Field(acolyte,"projectiles")).Count==0,
                    "the actual acolyte's first in-range tick immediately starts fire theft and protection without an opening volley");
                Color marked=chosen.EffectiveLight.color;
                Check(chosen.Lit && chosen.ProjectsAt(chosen.Position,.15f) && Dormant(chosen) &&
                    marked.b>marked.r && marked.r>marked.g && Mathf.Approximately(chosen.EffectiveLight.intensity,startingIntensity),
                    "the protected prayer visibly marks the D01 wick violet while its physical light still projects");
                yield return Capture("round3-03-first-sight-steals-fire",chosen,true);
                int earlyKnives=lab.HolyKnifeCount;
                Shoot();
                Check(!acolyte.ProtectedByLamp && chosen.Lit && !Dormant(chosen) && lab.HolyKnifeCount==earlyKnives,
                    "shooting the violet lamp during prayer cancels the shield and restores warm fire without a relight reward");
                acolyte.ResetEncounter();TickActor(acolyte,.01f);
                int health=acolyte.Health;HitRealShadow(acolyte);
                Check(acolyte.Health==health,"a hit on the rendered acolyte shadow does no damage while connected");
                FinishPhase(acolyte);
                Check(!chosen.Lit && acolyte.Connected && acolyte.Empowered && Dormant(chosen),"prayer extinguishes the linked lamp while the connection shield persists");
                Check(!chosen.EffectiveLight.enabled && !chosen.ProjectsAt(chosen.Position,.15f),
                    "after the theft the violet D01 lamp stops lighting ground and cannot project enemy shadows");
                CheckFlameSize(chosen);
                yield return Capture("thief-lamp-02-stolen",chosen,true);
                for(int i=0;i<3;i++)Check(NextVolley(acolyte)==3 && acolyte.Connected && acolyte.ProtectedByLamp,
                    "dark connected attack remains an empowered three-projectile volley "+(i+1));
                HitRealShadow(acolyte);Check(acolyte.Health==health,"waiting through dark volleys does not expire the shield");

                // Controlled distance-only disengagement, without moving the authored actor.
                Vector3 realFoot=lab.ShadowState.Player;
                try {lab.ShadowState.Player=acolyte.transform.position+Vector3.right*(acolyte.DisengageDistance+acolyte.NoticeDistance+20);TickActor(acolyte,.01f);
                    Check(!acolyte.Engaged && acolyte.Connected && acolyte.ProtectedByLamp,"distance disengagement pauses attacks without breaking an established lamp shield");}
                finally {lab.ShadowState.Player=realFoot;}
                TickActor(acolyte,.01f);PlaceHunter(foot,aim);
                yield return Capture("round3-04-persistent-dark-lamp-shield");
                Shoot();
                Check(chosen.Lit && !acolyte.Connected && !acolyte.ProtectedByLamp && acolyte.Action==FireThiefCycle.Action.Stagger && cycle.AttacksReleased==0,
                    "real housing shot relights the lamp, interrupts protection and resets the three-attack count");
                Check(!Dormant(chosen) && chosen.ProjectsAt(chosen.Position,.15f) &&
                    chosen.Target.bounds==startingTarget && chosen.EffectiveLight.range==startingRange &&
                    chosen.EffectiveLight.color==warmLight && Mathf.Approximately(chosen.EffectiveLight.intensity,startingIntensity),
                    "relighting restores its shadow source, with the same crossbow target and physical light range");
                yield return Capture("thief-lamp-03-restored",chosen,false);
                HitRealShadow(acolyte);Check(acolyte.Health==health-10,"after shooting its lamp the same real shadow accepts ten holy damage");
                for(int i=1;i<=3;i++) {
                    int emitted=NextVolley(acolyte);
                    Check(emitted==1 && cycle.AttacksReleased==i && acolyte.Connected==(i==3),"after interruption the actual acolyte completes a fresh three-shot sequence: "+i);
                }
                yield return Capture("round3-05-reconnected-after-three");
                acolyte.LampSearchRadius=0;TickActor(acolyte,.01f);
                Check(acolyte.Link<0 && !acolyte.Connected && !acolyte.ProtectedByLamp && !acolyte.Empowered,"controlled zero-radius availability removes the lamp claim and shield");
                for(int i=0;i<4;i++)Check(NextVolley(acolyte)==1 && !acolyte.Connected && acolyte.Action!=FireThiefCycle.Action.Prayer,
                    "without an available lamp the actual acolyte keeps ordinary attacks, including attack "+(i+1));
                yield return Capture("round3-06-no-lamp-normal-attacks");
            } finally {acolyte.LampSearchRadius=oldRadius;acolyte.ResetEncounter();}

            foreach(string row in MinorShadowGroundIntegrationCases.Run(lab,report.evidenceFolder))Check(true,row);
            Check(UnityEngine.Object.FindObjectsByType<FireThiefAcolyte>(FindObjectsSortMode.None).Count(x=>x.Encounter==lab && x.isActiveAndEnabled && x.Activated)==report.placedAcolytes,
                "runtime checks preserve the author's placed acolyte count");
            var args=Environment.GetCommandLineArgs();
            report.ambientBenchmarkRequested=Array.IndexOf(args,"-ambient-benchmark")>=0 || Array.IndexOf(args,"--ambient-benchmark")>=0;
            if(report.ambientBenchmarkRequested) {
                yield return EnvironmentRound3Probe.Run(lab,report.evidenceFolder);
                Check(true,"real fixed-view environment ABBA sampling completed before acceptance exit");
            }
            yield return Capture("round3-07-final-native-hud");
            Check(report.unexpectedErrors.Count==0,"no unexpected player errors occurred during the controlled acceptance");
        }
        void CheckLampMovement(IronHallEncounter hall,EncounterLantern[] ordinaryLamps) {
            var ordinary=ordinaryLamps.First(lamp=>lamp.RuntimeSceneAdapter && lamp.Lit &&
                lamp.EffectiveLight!=null && lamp.ProjectsAt(lamp.Position,.1f) && lamp.VisualSurfaces.Length>0);
            VerifyMovedWick(ordinary.VisualSurfaces[0].transform,ordinary.Wick,ordinary.EffectiveLight,
                ordinary.Target,lab.GetLampIndex(ordinary),true);

            var boss=hall.WickAnchors[0].GetComponentInParent<GrandCombatLantern>();
            Check(boss!=null && boss.Wick.GetComponent<Light>()==hall.GalleryLights[0],
                "the first encounter gallery slot uses the actual Light component on the grand lantern wick");
            var target=boss.Wick.GetComponentsInChildren<SphereCollider>().Single(collider=>collider.isTrigger &&
                collider.GetComponentInChildren<LanternFire>()!=null);
            VerifyMovedWick(boss.transform,boss.Wick,hall.GalleryLights[0],target,0,false);
        }

        void VerifyMovedWick(Transform fixture,Transform wick,Light light,Collider target,int slot,bool ordinary) {
            var flame=wick.GetComponentInChildren<LanternFire>();
            Check(flame!=null && target!=null && light.transform.IsChildOf(fixture),"emitter and interactive wick found on "+fixture.name);
            Vector3 start=fixture.position,oldWick=wick.position,localWick=fixture.InverseTransformPoint(oldWick);
            Quaternion rotation=fixture.rotation;
            try {
                fixture.SetPositionAndRotation(start+new Vector3(8,3,-5),Quaternion.AngleAxis(37,Vector3.up)*rotation);
                Physics.SyncTransforms();
                Vector3 expected=fixture.TransformPoint(localWick);
                bool Near(Vector3 point) => Vector3.Distance(expected,point)<.015f;
                Check(Vector3.Distance(oldWick,expected)>1 && Near(wick.position) && Near(light.transform.position) &&
                    Near(flame.transform.position) && Near(lab.GetLampPosition(slot)),
                    "moving and turning "+fixture.name+" carries its flame, light and registered gameplay position");
                float reach=target.bounds.extents.magnitude+1;
                Check(Near(target.bounds.center) && target.Raycast(new Ray(expected+Vector3.right*reach,Vector3.left),out _,reach*2),
                    "the crossbow wick collider can be hit at the moved "+fixture.name);
                if(ordinary) {
                    var atNew=lab.GetMinorShadowLightingAt(expected,.15f);
                    var atOld=lab.GetMinorShadowLightingAt(oldWick,.15f);
                    Check(atNew.IsLocal && atNew.LampIndex==slot && Near(atNew.Position) && atOld.LampIndex!=slot,
                        "minor enemies project from the new light location and release the old one");
                }
            } finally {
                fixture.SetPositionAndRotation(start,rotation);
                Physics.SyncTransforms();
            }
        }

        int NextVolley(FireThiefAcolyte actor) {
            if(actor.Action==FireThiefCycle.Action.Stagger)FinishPhase(actor);
            Check(actor.Action==FireThiefCycle.Action.Waiting,"actual acolyte is ready for its next windup");
            FinishPhase(actor);Check(actor.Action==FireThiefCycle.Action.Windup,"actual attack enters its readable windup");
            var shots=(IList)Field(actor,"projectiles");var old=new HashSet<object>();foreach(var shot in shots)old.Add(shot);
            FinishPhase(actor);int emitted=0;foreach(var shot in shots)if(!old.Contains(shot))emitted++;return emitted;
        }
        void FinishPhase(FireThiefAcolyte actor) {
            float remaining=((FireThiefCycle)Field(actor,"cycle")).Remaining;
            if(remaining>.002f)TickActor(actor,remaining-.001f);
            TickActor(actor,.002f); // Newly released projectiles advance only a few millimetres.
        }
        void TickActor(ShadowEncounterActor actor,float dt) {
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            try {actor.Tick(dt);}finally {
                double elapsed=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                if(!tickSamples.TryGetValue(actor,out var samples))tickSamples[actor]=samples=new List<double>();samples.Add(elapsed);
            }
        }
        void HitRealShadow(FireThiefAcolyte actor) {
            var shadow=(MinorEnemyShadow)Field(actor,"shadow");Check(shadow.Visible,"actual acolyte publishes a visible shadow for protection testing");
            foreach(var mesh in actor.GetComponentsInChildren<MeshFilter>()) {
                if(!mesh.name.Contains("Shootable") || mesh.sharedMesh==null)continue;
                var vertices=mesh.sharedMesh.vertices;var triangles=mesh.sharedMesh.triangles;
                for(int i=0;i<triangles.Length;i+=3) {
                    Vector3 center=mesh.transform.TransformPoint((vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3);
                    var ray=new Ray(center+Vector3.up,Vector3.down);
                    if(!actor.TryHitShadow(ray,1.1f,out float distance))continue;
                    actor.ReceiveShadowHit(Bolt.Holy,ray.GetPoint(distance));return;
                }
            }
            throw new InvalidOperationException("No rendered acolyte shadow triangle accepted its own downward ray.");
        }
        void Shoot() {
            if(lab.Weapon.Rounds==0)lab.Weapon.Request(Bolt.Holy);
            for(int i=0;i<8 && lab.Weapon.Phase!=HunterCrossbow.Motion.Ready;i++)lab.Weapon.Step(1);
            int rounds=lab.Weapon.Rounds;lab.Shoot();Check(lab.Weapon.Rounds==rounds-1,"production crossbow shot consumes one actual holy round");
        }
        void Knife() {
            Invoke(lab,"TickHolyKnives",lab.HolyKnifeCooldown+.01f);
            Check(lab.ThrowHolyKnife(),"production E-action accepts the actual holy knife throw");
            for(int i=0;i<24;i++)Invoke(lab,"TickHolyKnives",.05f);
        }
        bool FindPosition(IronHallEncounter hall,EncounterLantern lamp,FireThiefAcolyte actor,bool housing,bool wall,
            out Vector3 point,out Vector3 aim,out string ground) {
            var targets=new List<Vector3>{lamp.Position};
            if(housing)foreach(var surface in lamp.InteractionSurfaces ?? Array.Empty<Collider>())if(surface!=null) {
                Bounds b=surface.bounds;targets.Add(b.center);
                foreach(Vector3 axis in new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back})targets.Add(b.center+Vector3.Scale(b.extents,axis)*.75f);
            }
            foreach(float radius in new[]{4f,6f,8f,3f,10f,14f})for(int i=0;i<24;i++) {
                float angle=i*Mathf.PI/12,x=lamp.Position.x+Mathf.Cos(angle)*radius,z=lamp.Position.z+Mathf.Sin(angle)*radius;
                foreach(var floor in Physics.RaycastAll(new Vector3(x,lamp.Position.y+8,z),Vector3.down,50,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(hit=>hit.distance)) {
                    if(floor.normal.y<.7f || !hall.IsGround(floor.collider))continue;
                    Vector3 foot=floor.point+Vector3.up*.04f;
                    if(Mathf.Abs(foot.y-lamp.Position.y)>8 || actor!=null && actor.NoticeDistance>0 &&
                        Vector2.Distance(new Vector2(foot.x,foot.z),new Vector2(actor.transform.position.x,actor.transform.position.z))>actor.NoticeDistance-1)continue;
                    if(Physics.CheckCapsule(foot+Vector3.up*.38f,foot+Vector3.up*1.42f,.34f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
                    foreach(Vector3 target in targets) {
                        var ray=new Ray(foot+Vector3.up*1.65f,(target-foot-Vector3.up*1.65f).normalized);
                        if(!FirstSurface(ray,out var hit))continue;
                        bool valid=wall?IsWall(hall,lamp,hit) && hit.distance<Vector3.Distance(ray.origin,target)-.5f:
                            lamp.OwnsInteractionCollider(hit.collider) && (!housing || hit.collider!=lamp.Target);
                        if(!valid)continue;
                        point=foot;aim=target;ground=PathOf(floor.transform);return true;
                    }
                }
            }
            point=aim=default;ground="";return false;
        }
        bool IsWall(IronHallEncounter hall,EncounterLantern lamp,RaycastHit hit) => hit.collider!=null && !hit.collider.isTrigger &&
            !lamp.OwnsInteractionCollider(hit.collider) && !hall.IsGround(hit.collider) && Mathf.Abs(hit.normal.y)<.5f &&
            hit.collider.GetComponentInParent<ShadowEncounterActor>()==null && hit.collider.GetComponentInParent<EncounterLantern>()==null &&
            !actualLamps.Any(other=>other.OwnsInteractionCollider(hit.collider));
        void PlaceHunter(Vector3 foot,Vector3 target) {
            var body=(CharacterController)Field(lab,"playerBody");body.enabled=false;body.transform.position=foot;body.enabled=true;body.Move(Vector3.down*.05f);
            lab.ShadowState.Player=body.transform.position;Physics.SyncTransforms();lab.RefreshView();
            Quaternion rotation=Quaternion.LookRotation(target-lab.Eye.transform.position);Vector3 euler=rotation.eulerAngles;
            typeof(ShadowLab).GetField("yaw",Private).SetValue(lab,euler.y);typeof(ShadowLab).GetField("pitch",Private).SetValue(lab,euler.x>180?euler.x-360:euler.x);
            lab.Eye.transform.rotation=rotation;
        }
        Ray CameraRay()=>lab.Eye.ViewportPointToRay(new Vector3(.5f,.5f,0));
        bool FirstSurface(Ray ray,out RaycastHit hit) {
            object[] args={ray,default(RaycastHit)};bool yes=(bool)typeof(ShadowLab).GetMethod("FirstShotSurface",Private).Invoke(lab,args);hit=(RaycastHit)args[1];return yes;
        }
        static bool Dormant(EncounterLantern lamp) {
            var flame=(LanternFire)Field(lamp,"flame");var renderer=(ParticleSystemRenderer)Field(flame,"tongueRenderer");
            var properties=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);return properties.GetFloat("_Dormant")>.5f;
        }
        void CheckFlameSize(EncounterLantern lamp) {
            var fire=lamp.Wick.GetComponentInChildren<LanternFire>();
            var tongue=fire.transform.Find("Flame tongues").GetComponent<ParticleSystem>();
            float oldScale=Mathf.Lerp(.14f,1.22f,Mathf.Sqrt(lamp.Lit ? lamp.Strength : .42f));
            float previousWidth=fire.transform.parent.TransformVector(Vector3.right*(.31f*oldScale)).magnitude;
            float previousHeight=fire.transform.parent.TransformVector(Vector3.up*(.40f*oldScale)).magnitude;
            float width=tongue.transform.TransformVector(Vector3.right*tongue.main.startSizeX.constant).magnitude;
            float height=tongue.transform.TransformVector(Vector3.up*tongue.main.startSizeY.constant).magnitude;
            Check(Mathf.Abs(width/previousWidth-2)<.005f && Mathf.Abs(height/previousHeight-2)<.005f,
                "actual "+(lamp.Lit?"warm":"violet")+" lamp particle renders at twice its former width and height");
        }
        IEnumerator Capture(string name,EncounterLantern lamp=null,bool violet=false) {
            Invoke(lab.UI,"RefreshLiveHud");Canvas.ForceUpdateCanvases();
            int previous=frameCapture.RenderedFrames;float timeout=Time.realtimeSinceStartup+10;
            while(frameCapture.RenderedFrames<=previous && Time.realtimeSinceStartup<timeout) {
                if(!string.IsNullOrEmpty(frameCapture.Failure))throw new InvalidOperationException(frameCapture.Failure);
                yield return null;
            }
            if(frameCapture.RenderedFrames<=previous)throw new TimeoutException("No new controlled offscreen frame for "+name);
            CapturePixels(name,lamp,violet);
        }
        void CapturePixels(string name,EncounterLantern lamp=null,bool violet=false) {
            Texture2D image=frameCapture.ReadFrame();
            try {
                var pixels=InspectScreen(image,name);report.screenPixels.Add(pixels);
                string file=name+".png";File.WriteAllBytes(Path.Combine(report.evidenceFolder,file),image.EncodeToPNG());
                RequireVisibleScreen(pixels);report.screenshots.Add(file);
                if(lamp!=null)CheckGlassPixels(name,image,lamp,violet);
            }
            finally {if(image!=null)UnityEngine.Object.Destroy(image);}
        }
        void CheckGlassPixels(string name,Texture2D image,EncounterLantern lamp,bool violet) {
            var eye=lab.Eye;
            Vector3 center=eye.WorldToViewportPoint(lamp.Position);
            Vector3 above=eye.WorldToViewportPoint(lamp.Position+eye.transform.up*.47f);
            int radius=Mathf.Clamp(Mathf.RoundToInt((above.y-center.y)*image.height),24,145);
            int x=Mathf.RoundToInt(center.x*image.width),y=Mathf.RoundToInt(center.y*image.height);
            int left=Mathf.Clamp(x-radius,0,image.width),bottom=Mathf.Clamp(y-radius/2,0,image.height);
            int right=Mathf.Clamp(x+radius,0,image.width),top=Mathf.Clamp(y+radius,0,image.height);
            Check(center.z>0 && right-left>=32 && top-bottom>=32,"the D01 glass is in frame for "+name);
            var result=new WickPixels {frame=name,region=new RectInt(left,bottom,right-left,top-bottom)};
            var pixels=image.GetPixels32();
            for(int row=bottom;row<top;row++)for(int col=left;col<right;col++) {
                var c=pixels[row*image.width+col];
                if(c.r>80 && c.r>c.b*1.55f && c.r>c.g*1.08f)result.warm++;
                if(c.r>60 && c.b>76 && c.b>c.g*1.25f && c.r>c.g*1.18f && c.b>c.r*.78f)result.violet++;
            }
            int baseline=report.wickPixels.Count==0 ? result.warm : report.wickPixels[0].warm;
            int wanted=violet ? result.violet : result.warm,other=violet ? result.warm : result.violet;
            report.wickPixels.Add(result);
            Check(wanted>80 && wanted>other && wanted>baseline*.3f,
                name+" shows the lantern glass as "+(violet?"violet":"orange")+" in the rendered image: warm="+result.warm+", violet="+result.violet);
        }
        public static ScreenPixels InspectScreen(Texture2D image,string name) {
            if(image==null || image.width<320 || image.height<200)throw new InvalidOperationException("Invalid evidence image dimensions: "+name);
            var result=new ScreenPixels {name=name,width=image.width,height=image.height,minimumLuminance=255};
            var pixels=image.GetPixels32();int stride=Math.Max(1,pixels.Length/20000);
            for(int i=0;i<pixels.Length;i+=stride) {
                var color=pixels[i];int luminance=(54*color.r+183*color.g+19*color.b)/256;
                result.samples++;if(luminance>=16)result.visibleSamples++;
                result.minimumLuminance=Math.Min(result.minimumLuminance,luminance);
                result.maximumLuminance=Math.Max(result.maximumLuminance,luminance);
            }
            return result;
        }
        public static void RequireVisibleScreen(ScreenPixels pixels) {
            // Reject an empty backbuffer or a flat clear color; this only proves
            // basic image content, never visual correctness of gameplay or UI.
            if(pixels.visibleSamples<16 || pixels.maximumLuminance-pixels.minimumLuminance<8)
                throw new InvalidOperationException("Evidence image has no usable visible content: "+pixels.name+
                    "; visible="+pixels.visibleSamples+"/"+pixels.samples+", luminance="+pixels.minimumLuminance+".."+pixels.maximumLuminance+
                    ". A black or flat backbuffer is not accepted as rendered evidence.");
        }
        void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);report.checks++;report.passed.Add(message);Debug.Log("PASS ROUND3 "+message);}
        void Finish(Exception error) {
            if(completed)return;completed=true;Application.logMessageReceived-=ObserveLog;
            foreach(var pair in tickSamples) {
                var ordered=pair.Value.OrderBy(x=>x).ToArray();
                int builds=0,reuses=0;var cacheDiagnostics=new List<string>();
                void CountShadow(MinorEnemyShadow shadow) {if(shadow!=null) {builds+=shadow.SurfaceBuildCount;reuses+=shadow.SurfaceReuseCount;cacheDiagnostics.Add(shadow.SurfaceCacheDiagnostic);}}
                if(pair.Key is FireThiefAcolyte)CountShadow((MinorEnemyShadow)Field(pair.Key,"shadow"));
                else if(pair.Key is GroundShadowSwarm)foreach(var member in (IList)Field(pair.Key,"members"))
                    CountShadow((MinorEnemyShadow)member.GetType().GetField("Shadow",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(member));
                report.actorTickCpu.Add(new ActorTiming {name=pair.Key!=null?PathOf(pair.Key.transform):"destroyed runtime actor",kind=pair.Key.GetType().Name,
                    calls=ordered.Length,totalMilliseconds=ordered.Sum(),maxMilliseconds=ordered.Last(),p95Milliseconds=ordered[Mathf.Clamp((int)Math.Ceiling(ordered.Length*.95)-1,0,ordered.Length-1)],
                    shadowBuilds=builds,shadowReuses=reuses,shadowCacheDiagnostic=string.Join(" | ",cacheDiagnostics)});
            }
            if(error==null && report.unexpectedErrors.Count>0)error=new InvalidOperationException(string.Join(" | ",report.unexpectedErrors));
            report.result=error==null?"PASS":"FAIL";report.failure=error?.ToString();report.completedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(report.evidenceFolder,"round3-check.json"),JsonUtility.ToJson(report,true));
            if(error==null)Debug.Log("GAMEPLAY_ROUND3_CHECK_PASS checks="+report.checks);else Debug.LogError("GAMEPLAY_ROUND3_CHECK_FAIL "+error);
            Application.Quit(error==null?0:1);
        }
        static object Field(object owner,string field)=>owner.GetType().GetField(field,Private).GetValue(owner);
        static void Invoke(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,Private).Invoke(owner,args);
        static string PathOf(Transform value)=>value.parent==null?value.name:PathOf(value.parent)+"/"+value.name;
        static string Argument(string name) {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0 && i+1<args.Length?args[i+1]:null;}
    }
}
