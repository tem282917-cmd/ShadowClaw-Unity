using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Opt-in player evidence only. No attachment or changes to the authored scene.
    public sealed class Chapter01BossReadabilityCheck : MonoBehaviour
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] sealed class Shot {
            public string name, ground, action, shadowMaskHash;
            public Vector3 foot, eye, aim, bossGround, gameplayLight, shadowHitGroundPoint;
            public Bounds bodyWorldBounds;
            public Rect bodyViewport, shadowViewport, shadowGroundRect;
            public float fieldOfView, shadowScale, hittableShadowArea;
            public int lamps, lightMask, shadowCount, shadowPixels, clearBodySightSamples, bodySightSamples;
            public bool presentationEnabled, bodyInFront, bodyFullyInViewport, productionShadowRayHit;
            public Chapter01Round3Check.ScreenPixels pixels;
        }
        [Serializable] sealed class Fill {
            public string name, type, shadows, renderMode;
            public Vector3 position;
            public Color color;
            public float range, intensity, bossGroundDistance;
        }
        [Serializable] sealed class Report {
            public string result="RUNNING", failure, scene, startedUtc, completedUtc, evidenceFolder, captureMode, suppressedAuthoredPreview;
            public string scope="Controlled grounded camera placements in the actual authored Boss room, actual Eye world rendering and native Canvas composition. " +
                "Boss/lamp states are set through runtime test setup; gameplay Update is disabled and poses are frozen at their actual current state. " +
                "No authored enemy positions, scene assets, FOV, exposure or materials are changed. This is not a manual playthrough or an FPS benchmark. " +
                "Body projection and coverage are diagnostics only: image readability requires human image review. Lifecycle checks simulate component disable/enable, not a scene-unload playthrough.";
            public string shadowAreaMethod="Count the production 224-square coverage mask at the production >=128 hit threshold, multiplied by its world rectangle area. " +
                "Inactive all-dark shadows have zero hittable area, regardless of the cached mask. Rectangular receiver mesh area is not used.";
            public int checks, ordinaryLamps, bossViews;
            public List<string> passed=new List<string>(), unexpectedErrors=new List<string>();
            public List<Shot> shots=new List<Shot>();
            public List<Fill> fills=new List<Fill>();
            public List<FrameSample> frameSamples=new List<FrameSample>();
        }
        [Serializable] sealed class FrameSample {
            public string order;
            public string scope="120 controlled frames with actual world and Canvas offscreen rendering; includes capture overhead, not normal gameplay FPS or isolated GPU light cost";
            public bool presentationEnabled;
            public int renderedFrames;
            public double medianMilliseconds,p95Milliseconds;
        }
        sealed class Viewpoint { public string name,ground; public Vector3 foot; }
        ShadowLab lab; IronHallEncounter hall; ShadowDemonView demon; BossRoomReadability readability;
        Round3FrameCapture capture; Report report; bool completed; float deadline;
        int[] ordinaryIds; Color ambient,sky,equator,groundColor; float ambientIntensity,reflectionIntensity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-boss-readability")<0)return;
            var runner=new GameObject("Opt-in Boss room readability evidence").AddComponent<Chapter01BossReadabilityCheck>();
            runner.report=new Report {startedUtc=DateTime.UtcNow.ToString("o"),evidenceFolder=Argument("-chapter01-evidence") ??
                Path.Combine(Application.persistentDataPath,"BossReadabilityCheck")};
            Directory.CreateDirectory(runner.report.evidenceFolder);
            runner.deadline=Time.realtimeSinceStartup+180;
            Application.logMessageReceived+=runner.ObserveLog;runner.StartCoroutine(runner.Run());
        }
        void Update() {
            if(!completed && deadline>0 && Time.realtimeSinceStartup>deadline) {
                StopAllCoroutines();Finish(new TimeoutException("Boss readability evidence timed out; unfinished images are not accepted."));
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
                catch(Exception caught) {error=caught;break;}
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
            Check(lab!=null && lab.GameplayReady && lab.UI!=null,"actual chapter gameplay, UI and initial assets are ready");
            hall=lab.GetComponent<IronHallEncounter>();
            Check(hall!=null && hall.UseAuthoredLayout,"the authored main room is loaded");
            report.scene=lab.gameObject.scene.name;lab.enabled=false;
            lab.RestartEncounter();typeof(ShiyinUI).GetField("sessionStarted",Private).SetValue(lab.UI,true);
            Invoke(lab.UI,"SetView","battle",null);lab.Developer.ClearPauseReasons();
            if(!lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            demon=(ShadowDemonView)Field(lab,"modelView");readability=hall.Readability;
            Check(demon!=null && readability!=null,"original Boss and authored-room presentation component exist");
            report.bossViews=UnityEngine.Object.FindObjectsByType<ShadowDemonView>(FindObjectsSortMode.None).Count(x=>x.isActiveAndEnabled);
            Check(report.bossViews==1,"exactly one active original Boss view exists");
            var castle=lab.gameObject.scene.GetRootGameObjects().FirstOrDefault(x=>x.name=="Castle");
            var staticPreview=castle!=null?castle.transform.Find("ShadowDemon"):null;
            Check(staticPreview!=null && staticPreview.GetComponent<EncounterAuthoringPreview>()!=null && !staticPreview.gameObject.activeInHierarchy,
                "the known Castle/ShadowDemon architecture preview exists but cannot render as a second Boss");
            report.suppressedAuthoredPreview=PathOf(staticPreview);
            Check(hall.GalleryLights.Length==6 && readability.FillLights.Count==3,"six combat lamps and three independent fill lights exist");
            capture=Round3FrameCapture.Attach(lab);report.captureMode=capture.CaptureMode;
            lab.RefreshOrdinarySceneLamps();ordinaryIds=OrdinaryIds();report.ordinaryLamps=ordinaryIds.Length;
            ambient=RenderSettings.ambientLight;sky=RenderSettings.ambientSkyColor;equator=RenderSettings.ambientEquatorColor;
            groundColor=RenderSettings.ambientGroundColor;ambientIntensity=RenderSettings.ambientIntensity;reflectionIntensity=RenderSettings.reflectionIntensity;
            foreach(var light in readability.FillLights) {
                Check(light!=null && light.type==LightType.Point && light.shadows==LightShadows.None,"fill is a real shadowless point light");
                Check(!hall.GalleryLights.Contains(light) && !UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None).Any(x=>x.ControlsLight(light)),
                    "fill is excluded from six-lamp and ordinary-lamp ownership: "+light.name);
                report.fills.Add(new Fill {name=light.name,type=light.type.ToString(),shadows=light.shadows.ToString(),renderMode=light.renderMode.ToString(),
                    position=light.transform.position,color=light.color,range=light.range,intensity=light.intensity,
                    bossGroundDistance=Vector3.Distance(light.transform.position,lab.ShadowState.Enemy)});
            }
            var enemy=lab.ShadowState.Enemy;
            Vector3 inward=hall.FogGate!=null?hall.FogGate.Inward:Vector3.forward;
            Vector3 side=Vector3.Cross(Vector3.up,inward).normalized;
            var seeds=new[] {
                hall.FogGate!=null?hall.FogGate.FloorCenter+inward*(hall.FogGate.EntryClearance+1.8f):enemy-inward*22,
                enemy-inward*12,
                enemy-inward*7+side*7,
                enemy-inward*5
            };
            string[] names={"entrance","middle","side","near"};
            var views=new List<Viewpoint>();
            int[] masks={63,21,4,0,63};string[] states={"six-lamps","three-lamps","one-lamp","all-dark","relit"};
            // Preserve completed evidence even if a later camera cannot be placed.
            for(int viewIndex=0;viewIndex<seeds.Length;viewIndex++) {
                lab.RestartEncounter();Invoke(lab.UI,"SetView","battle",null);lab.Developer.ClearPauseReasons();
                Check(FindGrounded(seeds[viewIndex],out var foot,out var support,names[viewIndex]=="side",names[viewIndex]=="entrance"),
                    "a grounded capsule-clear "+names[viewIndex]+" viewpoint with a clear body sightline exists");
                var view=new Viewpoint {name=names[viewIndex],foot=foot,ground=support};views.Add(view);
                // Commit through the real gate first. A legal room viewpoint
                // need not lie within the doorway's narrow lateral footprint.
                Place(views[0].foot);Invoke(lab,"TryBeginEncounter");
                Check(lab.EncounterStarted,"real grounded fog-gate entry starts the encounter before the "+view.name+" viewpoint");
                Place(view.foot);
                float sixScale=0,oneScale=0;
                for(int i=0;i<masks.Length;i++) {
                    SetLamps(masks[i]);Aim();
                    Check(lab.LitLamps==CountBits(masks[i]) && lab.ShadowCount==(masks[i]==0?0:1),"lamp state and shootable shadow activation: "+view.name+" "+states[i]);
                    Check(AmbientUnchanged(),"global room environment stays constant: "+view.name+" "+states[i]);
                    if(i==0)sixScale=lab.ShadowState.SilhouetteScale;if(i==2)oneScale=lab.ShadowState.SilhouetteScale;
                    yield return Save(view.name+"-"+states[i],view);
                    if(i==1 && view.name=="middle") {
                        string before=ShadowHash();Vector3 source=lab.GameplayLight;int lightId=lab.ShadowState.LightId;float scale=lab.ShadowState.SilhouetteScale;
                        readability.PresentationEnabled=false;lab.RefreshView();
                        Check(readability.FillLights.All(x=>!x.enabled) && (float)Field(demon,"surfaceReadability")==1 && (float)Field(demon,"rimReadability")==1,
                            "presentation-off disables fill and restores original Boss material gain");
                        Check(before==ShadowHash() && source==lab.GameplayLight && lightId==lab.ShadowState.LightId && scale==lab.ShadowState.SilhouetteScale,
                            "presentation-off preserves the exact shadow hit mask, source, lamp mask and size");
                        yield return Save("middle-three-lamps-presentation-off",view);
                        readability.PresentationEnabled=true;lab.RefreshView();
                        Check(before==ShadowHash() && source==lab.GameplayLight,"presentation-on restores lighting without altering the shadow");
                    }
                }
                Check(oneScale>sixScale,"one lamp enlarges the production shadow scale relative to six lamps: "+view.name);
            }
            Check(ordinaryIds.SequenceEqual(OrdinaryIds()),"all lamp states and presentation toggles preserve the ordinary lamp registry");
            Check(!staticPreview.gameObject.activeInHierarchy,"restarting all four viewpoint sessions never reactivates the static Boss preview");
            string pauseMask=ShadowHash();int pauseLights=lab.LitLamps;
            Invoke(lab.UI,"SetView","pause",null);yield return null;
            Check(lab.GameplayPaused && lab.LitLamps==pauseLights && pauseMask==ShadowHash(),"pause preserves lamp and target-shadow state");
            Invoke(lab.UI,"SetView","battle",null);lab.Developer.ClearPauseReasons();yield return null;
            Check(!lab.GameplayPaused && AmbientUnchanged(),"resume restores battle while retaining the original room environment");
            readability.enabled=false;lab.RefreshView();
            Check(readability.FillLights.All(x=>!x.enabled) && (float)Field(demon,"surfaceReadability")==1 && (float)Field(demon,"rimReadability")==1 && AmbientUnchanged(),
                "component-exit lifecycle disables fill and restores original body gain without changing global environment");
            readability.enabled=true;lab.RefreshView();
            Check(readability.FillLights.All(x=>x.enabled) && pauseMask==ShadowHash(),"component re-entry restores presentation with identical target shadow");
            Check(report.shots.Count==21,"twenty viewpoint/state images and one same-state presentation-off comparison are recorded");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-benchmark-boss-readability")>=0) {
                Place(views[1].foot);SetLamps(21);Aim();
                int qualityVsync=QualitySettings.vSyncCount,frameLimit=Application.targetFrameRate;
                QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
                try {
                    bool[] order={true,false,false,true};
                    for(int group=0;group<order.Length;group++) {
                        readability.PresentationEnabled=order[group];lab.RefreshView();
                        for(int warm=0;warm<20;warm++)yield return null;
                        int first=capture.RenderedFrames;var samples=new List<double>(120);
                        double previous=Time.realtimeSinceStartupAsDouble;
                        for(int frame=0;frame<120;frame++) {
                            yield return null;
                            double now=Time.realtimeSinceStartupAsDouble;samples.Add((now-previous)*1000);previous=now;
                        }
                        samples.Sort();
                        report.frameSamples.Add(new FrameSample {order=(group+1).ToString(),presentationEnabled=order[group],
                            renderedFrames=capture.RenderedFrames-first,medianMilliseconds=(samples[59]+samples[60])*.5,p95Milliseconds=samples[113]});
                        Check(capture.RenderedFrames-first>=119,"actual room rendering remains active throughout fill-cost sample "+(group+1));
                    }
                } finally {QualitySettings.vSyncCount=qualityVsync;Application.targetFrameRate=frameLimit;readability.PresentationEnabled=true;lab.RefreshView();}
            }
            Check(report.unexpectedErrors.Count==0,"no unexpected player errors occurred during evidence capture");
        }

        bool FindGrounded(Vector3 requested,out Vector3 foot,out string support,bool sideView=false,bool entranceView=false) {
            var candidates=new List<Vector3> {requested};
            if(sideView) {
                candidates.Clear();
                Vector3 inward=hall.FogGate!=null?hall.FogGate.Inward:Vector3.forward;
                Vector3 forward=-Vector3.ProjectOnPlane(inward,Vector3.up).normalized;
                // Search both sides of the actual Boss at fixed ground-plane
                // distances. No offset may quietly turn this into a front view.
                foreach(float distance in new[]{8f,9f,10f})
                    foreach(float degrees in new[]{50f,-50f,65f,-65f,80f,-80f,100f,-100f,120f,-120f,140f,-140f,35f,-35f})
                        candidates.Add(lab.ShadowState.Enemy+Quaternion.AngleAxis(degrees,Vector3.up)*forward*distance);
            }
            foreach(var seed in candidates)
            foreach(float radius in sideView?new[]{0f}:new[]{0f,1f,2f,3f})for(int i=0;i<(radius==0?1:12);i++) {
                float angle=i*Mathf.PI/6;var p=seed+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                foreach(var hit in Physics.RaycastAll(new Vector3(p.x,Mathf.Max(seed.y,lab.ShadowState.Enemy.y)+9,p.z),Vector3.down,25,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(x=>x.distance)) {
                    if(!hall.IsGround(hit.collider) || hit.normal.y<.7f || Mathf.Abs(hit.point.y-lab.ShadowState.Enemy.y)>7)continue;
                    p=hit.point+Vector3.up*.04f;
                    if(entranceView) {
                        if(hall.StartWithExploration && !hall.ShouldStartEncounter(p))continue;
                    } else if(!hall.PlayArea.Contains(new Vector2(p.x,p.z)))continue;
                    if(Physics.OverlapCapsule(p+Vector3.up*.38f,p+Vector3.up*1.42f,.34f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                        .Any(x=>x!=(CharacterController)Field(lab,"playerBody")))continue;
                    if(!ClearSight(p+Vector3.up*1.65f,lab.ShadowState.Enemy+Vector3.up*1.2f))continue;
                    foot=p;support=PathOf(hit.transform);return true;
                }
            }
            foot=default;support="";return false;
        }
        bool ClearSight(Vector3 origin,Vector3 target) {
            // The original Caster primitives retain body-hit colliders after
            // their placeholder renderers are hidden by the skinned view.
            // Ignore only this Boss hierarchy, never the entire lab/room.
            Transform caster=lab.transform.Find("Caster");
            foreach(var hit in Physics.RaycastAll(origin,(target-origin).normalized,Vector3.Distance(origin,target)-.1f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) {
                if(hit.collider==(CharacterController)Field(lab,"playerBody") || hit.collider==(CharacterController)Field(lab,"enemyBody") ||
                    hit.transform.IsChildOf(demon.transform) || caster!=null && hit.transform.IsChildOf(caster))continue;
                return false;
            }
            return true;
        }
        void Place(Vector3 foot) {
            var body=(CharacterController)Field(lab,"playerBody");body.enabled=false;body.transform.position=foot;body.enabled=true;body.Move(Vector3.down*.05f);
            lab.ShadowState.Player=body.transform.position;Physics.SyncTransforms();lab.RefreshView();Aim();
        }
        void Aim() {
            Bounds b=BodyBounds();Vector3 target=b.center;target.y=Mathf.Lerp(lab.ShadowState.Enemy.y,b.max.y,.43f);
            Quaternion rotation=Quaternion.LookRotation(target-lab.Eye.transform.position);Vector3 e=rotation.eulerAngles;
            typeof(ShadowLab).GetField("yaw",Private).SetValue(lab,e.y);
            typeof(ShadowLab).GetField("pitch",Private).SetValue(lab,e.x>180?e.x-360:e.x);
            lab.RefreshView();
        }
        void SetLamps(int mask) {
            var lamps=(Array)Field(lab,"lamps");
            for(int i=0;i<lamps.Length;i++)lamps.GetValue(i).GetType().GetField("Remaining").SetValue(lamps.GetValue(i),(mask&(1<<i))!=0?120f:0f);
            Invoke(lab,"ApplyLighting");lab.RefreshView();Physics.SyncTransforms();
        }
        IEnumerator Save(string name,Viewpoint view) {
            lab.RefreshView();Physics.SyncTransforms();
            Invoke(lab.UI,"RefreshLiveHud");Canvas.ForceUpdateCanvases();
            int previous=capture.RenderedFrames;float until=Time.realtimeSinceStartup+10;
            while(capture.RenderedFrames<previous+2 && Time.realtimeSinceStartup<until) {
                if(!string.IsNullOrEmpty(capture.Failure))throw new InvalidOperationException(capture.Failure);yield return null;
            }
            Check(capture.RenderedFrames>=previous+2,"two new controlled offscreen frames rendered after pose/camera synchronization: "+name);
            var row=Describe(name,view);Texture2D image=capture.ReadFrame();
            try {
                row.pixels=Chapter01Round3Check.InspectScreen(image,name);
                File.WriteAllBytes(Path.Combine(report.evidenceFolder,name+".png"),image.EncodeToPNG());
                Chapter01Round3Check.RequireVisibleScreen(row.pixels);report.shots.Add(row);
            } finally {if(image!=null)Destroy(image);}
        }
        Shot Describe(string name,Viewpoint view) {
            Bounds body=BodyBounds();var points=new List<Vector3>();
            for(int i=0;i<8;i++)points.Add(body.center+Vector3.Scale(body.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
            Rect projected=Project(points,out bool front);
            var projection=lab.ShadowState.Witch.Body;
            var mask=(byte[])Field(projection,"mask");Rect area=(Rect)Field(projection,"area");int covered=mask.Count(x=>x>=128);
            var border=new List<Vector3>();if(lab.ShadowCount>0)projection.ReadBorder(border);
            Vector3 hitGround=new Vector3(projection.Aim.x,hall.SurfaceHeight(projection.Aim.x,projection.Aim.y),projection.Aim.y);
            bool rayHit=lab.ShadowCount>0 && !float.IsInfinity(projection.HitDistance(new Ray(hitGround+Vector3.up*3,Vector3.down)));
            if(lab.ShadowCount>0)Check(covered>0 && projection.Contains(projection.Aim) && rayHit,"active production shadow has covered pixels and a real terrain ray target: "+name);
            int clear=0;foreach(float t in new[]{.25f,.5f,.8f})if(ClearSight(lab.Eye.transform.position,new Vector3(body.center.x,Mathf.Lerp(body.min.y,body.max.y,t),body.center.z)))clear++;
            return new Shot {name=name,ground=view.ground,action=lab.BossAction.ToString(),foot=lab.HunterGroundPosition,eye=lab.Eye.transform.position,
                aim=lab.Eye.transform.position+lab.Eye.transform.forward*Vector3.Distance(lab.Eye.transform.position,body.center),bossGround=lab.ShadowState.Enemy,
                gameplayLight=lab.GameplayLight,bodyWorldBounds=body,bodyViewport=projected,shadowViewport=Project(border,out _),shadowGroundRect=area,
                shadowHitGroundPoint=hitGround,productionShadowRayHit=rayHit,
                fieldOfView=lab.Eye.fieldOfView,shadowScale=lab.ShadowState.SilhouetteScale,hittableShadowArea=lab.ShadowCount>0?covered/(float)mask.Length*area.width*area.height:0,
                lamps=lab.LitLamps,lightMask=lab.ShadowState.LightId,shadowCount=lab.ShadowCount,shadowPixels=lab.ShadowCount>0?covered:0,shadowMaskHash=ShadowHash(),
                clearBodySightSamples=clear,bodySightSamples=3,presentationEnabled=readability.PresentationEnabled,bodyInFront=front,
                bodyFullyInViewport=front && projected.xMin>=0 && projected.yMin>=0 && projected.xMax<=1 && projected.yMax<=1};
        }
        Bounds BodyBounds() {
            Bounds result=default;bool any=false;
            foreach(var renderer in demon.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                if(!renderer.enabled || renderer.sharedMesh==null)continue;
                var mesh=new Mesh();try {renderer.BakeMesh(mesh);foreach(var v in mesh.vertices) {
                    Vector3 p=renderer.transform.TransformPoint(v);if(!any){result=new Bounds(p,Vector3.zero);any=true;}else result.Encapsulate(p);
                }}finally {Destroy(mesh);}
            }
            if(!any)throw new InvalidOperationException("Original Boss has no enabled skinned mesh for world-space diagnostics.");return result;
        }
        Rect Project(List<Vector3> points,out bool front) {
            front=points.Count>0;if(points.Count==0)return default;
            float old=lab.Eye.aspect;lab.Eye.aspect=Round3FrameCapture.Width/(float)Round3FrameCapture.Height;
            try {Vector2 lo=new Vector2(float.MaxValue,float.MaxValue),hi=-lo;
                foreach(var p in points){Vector3 v=lab.Eye.WorldToViewportPoint(p);front&=v.z>0;lo=Vector2.Min(lo,v);hi=Vector2.Max(hi,v);}return Rect.MinMaxRect(lo.x,lo.y,hi.x,hi.y);
            }finally {lab.Eye.aspect=old;}
        }
        string ShadowHash() {
            uint hash=2166136261;foreach(byte b in (byte[])Field(lab.ShadowState.Witch.Body,"mask"))hash=unchecked((hash^b)*16777619);
            return hash.ToString("X8")+"/active="+lab.ShadowCount;
        }
        int[] OrdinaryIds()=>UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None).Where(x=>x.Encounter==lab && x.isActiveAndEnabled).Select(x=>x.GetInstanceID()).OrderBy(x=>x).ToArray();
        bool AmbientUnchanged()=>RenderSettings.ambientLight==ambient && RenderSettings.ambientSkyColor==sky && RenderSettings.ambientEquatorColor==equator &&
            RenderSettings.ambientGroundColor==groundColor && RenderSettings.ambientIntensity==ambientIntensity && RenderSettings.reflectionIntensity==reflectionIntensity;
        static int CountBits(int mask) {int n=0;while(mask>0){n+=mask&1;mask>>=1;}return n;}
        void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);report.checks++;report.passed.Add(message);Debug.Log("PASS BOSS_READABILITY "+message);}
        void Finish(Exception error) {
            if(completed)return;completed=true;Application.logMessageReceived-=ObserveLog;
            if(error==null && report.unexpectedErrors.Count>0)error=new InvalidOperationException(string.Join(" | ",report.unexpectedErrors));
            report.result=error==null?"PASS":"FAIL";report.failure=error?.ToString();report.completedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(report.evidenceFolder,"boss-readability-check.json"),JsonUtility.ToJson(report,true));
            if(error==null)Debug.Log("BOSS_READABILITY_CHECK_PASS checks="+report.checks);else Debug.LogError("BOSS_READABILITY_CHECK_FAIL "+error);
            Application.Quit(error==null?0:1);
        }
        static object Field(object owner,string name)=>owner.GetType().GetField(name,Private).GetValue(owner);
        static void Invoke(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,Private).Invoke(owner,args);
        static string PathOf(Transform value)=>value.parent==null?value.name:PathOf(value.parent)+"/"+value.name;
        static string Argument(string name) {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0 && i+1<args.Length?args[i+1]:null;}
    }
}
