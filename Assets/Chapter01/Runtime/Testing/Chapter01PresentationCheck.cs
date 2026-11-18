using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Opt-in acceptance of the installed main-scene presentation. Teleport is
    // only setup at the doorway; crossing and blocked return use Walk/Tick.
    public static class Chapter01PresentationCheck
    {
        [Serializable] sealed class LampReport {
            public int slot,targetCandidateCount,alignedTargetCount;
            public Vector3 bodySize,bodyMinimum,bodyMaximum,wick,targetPosition,sightOrigin;
            public float targetRadius,firstSightHitDistance;
            public bool targetFound,wickInsideBodyBounds,physicalSightClear;
            public string wickPath,targetPath,firstSightHitPath,standingGroundPath;
            public List<string> targetCandidates=new List<string>();
            public List<string> standingObstacles=new List<string>();
        }
        [Serializable] sealed class Report {
            public string result,failure,scene,timestampUtc,evidenceFolder;
            public Vector3 gateCenter,doorwaySetup,entryPosition,blockedReturn,jumpEntryBase;
            public float gateWidth,gateHeight,shadowLinearScale,jumpHeightAtEntry;
            public float fogChangedPixelFraction,fogMeanBrightnessGain;
            public int checks,visibleBossModels;
            public bool usedDoorwaySetupTeleport,walkedThroughGate,physicalReturnBlocked,restartRestored;
            public bool jumpEntryStartsBeforeLanding;
            public bool victoryPresentationOnly=true,fullBossKillTested=false;
            public string victoryScope="Gate completion/fade is exercised directly; this is not a full boss-kill test.";
            public string screenshotScope="Camera-only composition changes frame the installed geometry; gameplay entry/return use the actual hunter capsule.";
            public List<LampReport> lamps=new List<LampReport>();
            public List<string> passed=new List<string>();
            public List<string> screenshots=new List<string>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start() {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-chapter01-presentation")<0)return;
            var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
            if(!lab) {
                var report=NewReport();report.result="FAIL";report.failure="Main scene has no ShadowLab.";
                Save(report);Debug.LogError("CHAPTER01_PRESENTATION_CHECK_FAIL: "+report.failure);Application.Quit(1);return;
            }
            lab.StartCoroutine(Run(lab));
        }
        static Report NewReport() => new Report {
            timestampUtc=DateTime.UtcNow.ToString("o"),
            evidenceFolder=Argument("-chapter01-evidence") ?? Path.Combine(Application.persistentDataPath,"Chapter01PresentationCheck")
        };
        static IEnumerator Run(ShadowLab lab) {
            var report=NewReport();report.scene=lab.gameObject.scene.name;
            Directory.CreateDirectory(report.evidenceFolder);
            lab.Developer.SetFrozen(true);
            for(int frame=0;frame<3;frame++)yield return null;
            try {
                Exercise(lab,report);report.result="PASS";
                Debug.Log("CHAPTER01_PRESENTATION_CHECK_PASS checks="+report.checks);
            } catch(Exception error) {
                report.result="FAIL";report.failure=error.ToString();
                Debug.LogError("CHAPTER01_PRESENTATION_CHECK_FAIL: "+error.Message);Debug.LogException(error);
            } finally {
                lab.Developer.ClearPauseReasons();Save(report);
            }
            Application.Quit(report.result=="PASS" ? 0 : 1);
        }
        static void Exercise(ShadowLab lab,Report report) {
            void Require(bool ok,string message) {
                if(!ok)throw new InvalidOperationException(message);
                report.checks++;report.passed.Add(message);Debug.Log("PASS "+message);
            }
            var hall=lab.GetComponent<IronHallEncounter>();
            Require(hall && report.scene=="Chapter01_Main","the installed Chapter01_Main encounter is loaded");
            var gate=hall.FogGate;
            Require(gate && gate.Barrier && gate.CurtainMaterial,"the scene references a visible mist gate and its physical barrier");
            var hunter=lab.transform.Find("Hunter capsule").GetComponent<CharacterController>();
            var eye=lab.Eye;
            Require(hunter && eye,"the actual hunter capsule and gameplay camera are available");
            lab.RestartEncounter();
            if(!lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            report.gateCenter=gate.transform.position;report.gateWidth=gate.Width;report.gateHeight=gate.Height;
            Require(Vector3.Distance(report.gateCenter,new Vector3(0,68,83.475f))<.03f &&
                Mathf.Abs(gate.Width-7.6f)<.02f && Mathf.Abs(gate.Height-13.2f)<.02f && Vector3.Dot(gate.Inward,Vector3.forward)>.999f,
                "mist gate matches the measured entrance at z83.475, 7.6m wide and 13.2m high");
            Require(!lab.EncounterStarted && !gate.Sealed && gate.MistVisible && gate.MistOpacity>.99f,
                "before combat the entrance has visible mist and no blocking collider");
            var fog=gate.GetComponentsInChildren<ParticleSystem>(true);
            Require(fog.Length==0 && gate.GetComponentsInChildren<MeshRenderer>().Length==1,"the waterfall uses one mesh and zero simulated particles");
            Require(gate.GetComponentsInChildren<Light>(true).Length==0,"mist presentation adds no illumination source");

            Require(hall.Lanterns.Length==6 && hall.GalleryLights.Length==6 && hall.WickAnchors.Length==6,
                "all six authored combat lamp references are installed");
            var lamps=new GrandCombatLantern[6];
            var targets=new SphereCollider[6];
            Physics.SyncTransforms();
            for(int slot=0;slot<6;slot++) {
                var view=hall.Lanterns[slot].GetComponentInParent<GrandCombatLantern>();lamps[slot]=view;
                Require(view && view.Wick==hall.WickAnchors[slot] && hall.Lanterns[slot].enabled,
                    "lamp "+slot+" uses the large visible model and its authored wick");
                var surfaces=view.GetComponentsInChildren<Renderer>(true).Where(renderer=>renderer.enabled && !(renderer is ParticleSystemRenderer)).ToArray();
                var bounds=surfaces[0].bounds;foreach(var surface in surfaces)bounds.Encapsulate(surface.bounds);
                // Runtime fixtures are parented beneath the authored castle wick,
                // so they need not be descendants of the ShadowLab game object.
                var candidates=view.Wick.GetComponentsInChildren<SphereCollider>(true).Where(candidate=>candidate.isTrigger).ToArray();
                var aligned=candidates.Where(candidate=>Vector3.Distance(candidate.transform.position,view.Wick.position)<.02f).ToArray();
                var target=aligned.Length==1 ? aligned[0] : null;
                targets[slot]=target;
                var lampReport=new LampReport {
                    slot=slot,bodySize=bounds.size,bodyMinimum=bounds.min,bodyMaximum=bounds.max,wick=view.Wick.position,
                    wickPath=Hierarchy(view.Wick),targetCandidateCount=candidates.Length,alignedTargetCount=aligned.Length,
                    targetFound=target!=null,targetRadius=target ? target.radius : -1,
                    targetPosition=target ? target.transform.position : Vector3.zero,targetPath=target ? Hierarchy(target.transform) : "missing or ambiguous",
                    wickInsideBodyBounds=bounds.Contains(view.Wick.position),
                    targetCandidates=candidates.Select(candidate=>Hierarchy(candidate.transform)+" radius="+candidate.radius.ToString("F4")+
                        " position="+candidate.transform.position.ToString("F4")+" wickDistance="+Vector3.Distance(candidate.transform.position,view.Wick.position).ToString("F4")).ToList()
                };
                report.lamps.Add(lampReport);
                Require(Mathf.Abs(bounds.size.y-4)<.04f && Mathf.Abs(Mathf.Abs(bounds.center.x)-10.2f)<.04f &&
                    Mathf.Abs(bounds.min.y-70.5f)<.04f && new[]{99f,117f,136f}.Any(z=>Mathf.Abs(bounds.center.z-z)<.08f) &&
                    Mathf.Max(Mathf.Abs(bounds.min.x),Mathf.Abs(bounds.max.x))<11.6f,
                    "lamp "+slot+" is four metres high in its original bay, moved inside the columns at x=+/-10.2m");
                Require(target && Mathf.Abs(target.radius-.9f)<.001f && lampReport.wickInsideBodyBounds,
                    "lamp "+slot+" has a 0.9m shooting target aligned inside the visible chamber; candidates="+candidates.Length+
                    ", aligned="+aligned.Length+", radius="+lampReport.targetRadius.ToString("F4")+", target="+lampReport.targetPosition.ToString("F4")+
                    ", wick="+view.Wick.position.ToString("F4")+", bodyMin="+bounds.min.ToString("F4")+", bodyMax="+bounds.max.ToString("F4"));
                Vector3 stand=new Vector3(0,hall.SurfaceHeight(0,bounds.center.z),bounds.center.z);
                var ground=Physics.RaycastAll(stand+Vector3.up*.5f,Vector3.down,1,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                    .FirstOrDefault(hit=>hall.IsGround(hit.collider) && hit.normal.y>.5f && Mathf.Abs(hit.point.y-stand.y)<.15f);
                lampReport.standingGroundPath=ground.collider ? Hierarchy(ground.transform) : "missing ground";
                var obstructions=Physics.OverlapCapsule(stand+Vector3.up*(hunter.radius+.03f),
                    stand+Vector3.up*(hunter.height-hunter.radius),Mathf.Max(.05f,hunter.radius-hunter.skinWidth),
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Where(collider=>!hall.IsGround(collider)).ToArray();
                lampReport.standingObstacles=obstructions.Select(collider=>Hierarchy(collider.transform)).ToList();
                Require(ground.collider && obstructions.Length==0,"lamp "+slot+" has a grounded, unobstructed hunter-sized standing point at its bay centre: "+stand.ToString("F3"));
                lampReport.sightOrigin=stand+Vector3.up*1.65f;
                Vector3 sight=view.Wick.position-lampReport.sightOrigin;
                var first=Physics.RaycastAll(lampReport.sightOrigin,sight.normalized,sight.magnitude+.03f,
                        Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)
                    .Where(hit=>!hit.collider.isTrigger || hit.collider==target).OrderBy(hit=>hit.distance).FirstOrDefault();
                lampReport.firstSightHitPath=first.collider ? Hierarchy(first.transform) : "no hit";
                lampReport.firstSightHitDistance=first.distance;lampReport.physicalSightClear=first.collider==target;
                Require(lampReport.physicalSightClear,"lamp "+slot+" is directly shootable from its walkable bay centre; first hit="+
                    lampReport.firstSightHitPath+", distance="+first.distance.ToString("F3")+", wick="+view.Wick.position.ToString("F3"));
                var localLights=view.GetComponentsInChildren<Light>(true);
                Require(view.GameplayLight==hall.GalleryLights[slot] &&
                    Vector3.Distance(view.GameplayLight.transform.position,view.Wick.position)<.02f &&
                    localLights.All(light=>light==view.GameplayLight),
                    "lamp "+slot+" binds and aligns its original gameplay light without adding another");
                var flame=target.GetComponentInChildren<LanternFire>();
                Require(flame && flame.GetComponentsInChildren<Light>(true).Length==0 &&
                    Mathf.Abs(flame.transform.parent.localScale.x-3.8f)<.01f,"lamp "+slot+" has its enlarged light-free flame");
            }
            Require(lamps.Distinct().Count()==6,"the six lamp references are six distinct large models");

            void Tick() => lab.SendMessage("Tick",.02f);
            float DoorDepth() => gate.transform.InverseTransformPoint(hunter.transform.position).z;
            void TeleportForSetup(float depth) {
                Vector3 position=gate.FloorCenter+gate.Inward*depth;
                position.y=hall.SurfaceHeight(position.x,position.z);
                hunter.enabled=false;hunter.transform.position=position+Vector3.up*.08f;hunter.enabled=true;
                Physics.SyncTransforms();hunter.Move(Vector3.down*.15f);
                lab.ShadowState.Player=hunter.transform.position;Tick();lab.RefreshView();
                report.usedDoorwaySetupTeleport=true;
            }
            void Capture(string name,Vector3 from,Vector3 aim,float fieldOfView=60) {
                var oldPosition=eye.transform.position;var oldRotation=eye.transform.rotation;float oldFov=eye.fieldOfView;
                var weapon=eye.GetComponentInChildren<HunterWeaponView>();bool weaponActive=weapon && weapon.gameObject.activeSelf;
                try {
                    if(weapon)weapon.gameObject.SetActive(false);
                    eye.fieldOfView=fieldOfView;eye.transform.position=from;eye.transform.LookAt(aim);
                    Chapter01CombatCheck.Capture(eye,report.evidenceFolder,name);
                    if(File.Exists(Path.Combine(report.evidenceFolder,name+".png")))report.screenshots.Add(name+".png");
                } finally {
                    if(weapon)weapon.gameObject.SetActive(weaponActive);
                    eye.fieldOfView=oldFov;eye.transform.SetPositionAndRotation(oldPosition,oldRotation);
                }
            }
            TeleportForSetup(-2.5f);report.doorwaySetup=hunter.transform.position;
            Require(hunter.isGrounded && !lab.EncounterStarted,"controlled setup places the grounded hunter outside the real entrance");
            Capture("presentation-00-fog-outside",gate.FloorCenter-gate.Inward*15+Vector3.up*6,
                gate.FloorCenter+Vector3.up*6.2f);
            var mistRenderers=gate.GetComponentsInChildren<MeshRenderer>(true);
            foreach(var renderer in mistRenderers)renderer.enabled=false;
            Capture("presentation-00-fog-off-reference",gate.FloorCenter-gate.Inward*15+Vector3.up*6,
                gate.FloorCenter+Vector3.up*6.2f);
            foreach(var renderer in mistRenderers)renderer.enabled=true;
            VerifyVisibleMist(report);
            Require(report.fogChangedPixelFraction>.2f && report.fogMeanBrightnessGain>.025f,
                "the mist visibly covers the doorway in a same-camera rendered comparison, beyond particle-count flags");
            for(int step=0;step<100 && !lab.EncounterStarted;step++) {
                lab.Walk(gate.Inward,.02f);Tick();
                if(!lab.EncounterStarted && DoorDepth()>=gate.EntryClearance+.12f)
                    throw new InvalidOperationException("Hunter crossed the entry threshold without starting combat at step "+step);
            }
            report.entryPosition=hunter.transform.position;
            Require(lab.EncounterStarted && gate.Sealed && DoorDepth()>=gate.EntryClearance-.025f && DoorDepth()<gate.EntryClearance+.2f,
                "walking through the mist starts combat only after the capsule clears the gate plane");
            report.walkedThroughGate=true;
            Physics.SyncTransforms();
            Require(lab.ShadowState.WalkBounds==hall.ExplorationArea,"combat keeps exploration movement bounds instead of the former z90 air wall");
            for(int step=0;step<150 && hunter.transform.position.z<92;step++){lab.Walk(gate.Inward,.02f);Tick();}
            Require(hunter.transform.position.z>=91.9f,"the hunter can walk through the old z90 boundary into the room");
            Capture("presentation-01-hall-lamps",new Vector3(0,73,86.5f),new Vector3(0,72.5f,119),68);
            Capture("presentation-02-fog-inside",gate.FloorCenter+gate.Inward*13+Vector3.up*5.5f,
                gate.FloorCenter+Vector3.up*6);

            var views=UnityEngine.Object.FindObjectsByType<ShadowDemonView>(FindObjectsSortMode.None)
                .Where(view=>view.GetComponentsInChildren<SkinnedMeshRenderer>().Any(renderer=>renderer.enabled && renderer.gameObject.activeInHierarchy)).ToArray();
            report.visibleBossModels=views.Length;
            Require(views.Length==1 && views[0].transform.IsChildOf(lab.transform),"only one visible classic ShadowDemon model exists in the loaded scene");
            var spectacle=lab.GetComponentInChildren<BossSpectacle>();
            Require(spectacle && !spectacle.ApparitionVisible && spectacle.ApparitionRoot==null,
                "the classic encounter creates no giant duplicate apparition");
            Require(lab.transform.Find("Caster").GetComponentsInChildren<Renderer>().All(renderer=>!renderer.enabled),
                "legacy placeholder body renderers remain hidden");
            report.shadowLinearScale=lab.ShadowState.Witch.Body.LinearScale;
            Require(Mathf.Abs(report.shadowLinearScale-2)<.001f && Mathf.Abs(lab.DemonShadowScale-2)<.001f && lab.ShadowCount==1,
                "the single gameplay shadow uses the requested two-times linear scale");
            var enemy=lab.ShadowState.Enemy;
            Capture("presentation-03-classic-boss-shadow",enemy+new Vector3(7,7,-9),enemy+Vector3.up*.5f,65);
            var closeLamp=lamps[0];var wick=closeLamp.Wick.position;
            Capture("presentation-04-large-lamp-lit",wick+new Vector3(-Mathf.Sign(wick.x)*5,1,-4),wick+Vector3.up*.1f,55);

            for(int step=0;step<180;step++){lab.Walk(-gate.Inward,.02f);Tick();}
            report.blockedReturn=hunter.transform.position;float blockedDepth=DoorDepth();
            Require(gate.Sealed && blockedDepth>.45f && blockedDepth<1.05f && hunter.transform.position.z<89,
                "walking back passes the old air-wall location and stops at the physical mist barrier");
            Require(gate.Barrier.Raycast(new Ray(hunter.transform.position+Vector3.up,-gate.Inward),out var hit,2) && hit.distance<1,
                "the return obstruction is the gate collider directly in front of the hunter");
            report.physicalReturnBlocked=true;

            lab.SendMessage("AgeLamps",24.1f);lab.RefreshView();
            Require(lab.LitLamps==0 && lab.ShadowCount==0 && hall.GalleryLights.All(light=>!light.enabled),
                "all six expired lamps disable gameplay lights and remove the enemy shadow");
            var properties=new MaterialPropertyBlock();
            for(int slot=0;slot<6;slot++) {
                Require(lamps[slot].Strength==0 && !hall.GalleryLights[slot].enabled,"lamp "+slot+" reaches its fully extinguished state");
                var flame=targets[slot].GetComponentInChildren<LanternFire>();
                var stream=flame.GetComponentsInChildren<ParticleSystemRenderer>().First();stream.GetPropertyBlock(properties);
                Require(properties.GetFloat("_Dormant")==1 && properties.GetFloat("_Power")>0 &&
                    flame.GetComponentsInChildren<Light>(true).Length==0,"lamp "+slot+" displays the purple flame without shadow-affecting illumination");
                foreach(var surface in lamps[slot].GlassSurfaces) {
                    var materials=surface.sharedMaterials;
                    for(int material=0;material<materials.Length;material++) {
                        if(materials[material]==null || !materials[material].name.Contains("Glass"))continue;
                        surface.GetPropertyBlock(properties,material);Color color=properties.GetColor("_EmissionColor");
                        Require(color.b>color.r && color.r>color.g,"lamp "+slot+" has a purple glass state marker");
                    }
                }
            }
            Capture("presentation-05-large-lamp-purple",wick+new Vector3(-Mathf.Sign(wick.x)*5,1,-4),wick+Vector3.up*.1f,55);

            // Explicitly a presentation completion test, not a fabricated boss kill.
            lab.Developer.SetFrozen(true);gate.SetEncounterState(true,true,true);
            Require(!gate.Sealed,"direct victory presentation state immediately opens the exit collider");
            gate.AdvancePresentation(.6f,lab.GameplayPaused);
            Require(gate.MistOpacity>.45f && gate.MistOpacity<.55f,"completion mist fades halfway while gameplay is paused");
            gate.AdvancePresentation(.65f,lab.GameplayPaused);
            Require(!gate.MistVisible && gate.MistOpacity==0,"completion mist finishes fading after 1.2 seconds");
            Capture("presentation-06-gate-after-completion",gate.FloorCenter-gate.Inward*15+Vector3.up*6,
                gate.FloorCenter+Vector3.up*6.2f);
            lab.RestartEncounter();Physics.SyncTransforms();
            Require(!lab.EncounterStarted && !gate.Sealed && gate.MistVisible && gate.MistOpacity==1,
                "restarting restores the open visible mist gate and dormant boss");
            TeleportForSetup(.2f);
            for(int step=0;step<20;step++){lab.Walk(-gate.Inward,.02f);Tick();}
            Require(!lab.EncounterStarted && !gate.Sealed && DoorDepth()<-.8f,
                "after restart the player can walk outward through the restored open gate before starting combat");
            report.restartRestored=true;
            TeleportForSetup(-.45f);lab.Jump();Tick();
            Require(lab.ShadowState.JumpHeight>.05f && !hunter.isGrounded,"the entry regression starts with the actual hunter airborne outside the gate");
            for(int step=0;step<35 && !lab.EncounterStarted;step++){lab.Walk(gate.Inward,.02f);Tick();}
            report.jumpEntryBase=lab.ShadowState.Player;report.jumpHeightAtEntry=lab.ShadowState.JumpHeight;
            Require(lab.EncounterStarted && gate.Sealed && report.jumpHeightAtEntry>.1f && !hunter.isGrounded &&
                gate.IsInside(report.jumpEntryBase) && Mathf.Abs(report.jumpEntryBase.y-hall.SurfaceHeight(report.jumpEntryBase.x,report.jumpEntryBase.z))<.2f,
                "jumping through the mist starts combat before landing using the hunter's ground-base position");
            report.jumpEntryStartsBeforeLanding=true;
            lab.RestartEncounter();
        }
        static void Save(Report report) {
            Directory.CreateDirectory(report.evidenceFolder);
            File.WriteAllText(Path.Combine(report.evidenceFolder,"presentation-check.json"),JsonUtility.ToJson(report,true));
        }
        static void VerifyVisibleMist(Report report) {
            var mist=new Texture2D(2,2);var clear=new Texture2D(2,2);
            try {
                mist.LoadImage(File.ReadAllBytes(Path.Combine(report.evidenceFolder,"presentation-00-fog-outside.png")));
                clear.LoadImage(File.ReadAllBytes(Path.Combine(report.evidenceFolder,"presentation-00-fog-off-reference.png")));
                var a=mist.GetPixels32();var b=clear.GetPixels32();int changed=0,total=0;float gain=0;
                for(int y=(int)(mist.height*.16f);y<mist.height*.82f;y++)
                    for(int x=(int)(mist.width*.38f);x<mist.width*.62f;x++) {
                        int i=y*mist.width+x;
                        float delta=((int)a[i].r+a[i].g+a[i].b-(int)b[i].r-b[i].g-b[i].b)/(3f*255);
                        if(Mathf.Abs(delta)>.04f)changed++;
                        gain+=Mathf.Max(0,delta);total++;
                    }
                report.fogChangedPixelFraction=(float)changed/total;report.fogMeanBrightnessGain=gain/total;
            } finally {UnityEngine.Object.Destroy(mist);UnityEngine.Object.Destroy(clear);}
        }
        static string Argument(string name) {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);
            return index>=0 && index+1<args.Length ? args[index+1] : null;
        }
        static string Hierarchy(Transform node) => node.parent ? Hierarchy(node.parent)+"/"+node.name : node.name;
    }
}
