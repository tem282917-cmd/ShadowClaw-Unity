using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Explicit Windows acceptance only. Uses the author's scene and enemies;
    // camera/player setup and direct lethal holy damage are recorded, not a full playthrough.
    public static class Chapter01Round2Check
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] sealed class AcolyteReport {
            public string name,lamp;
            public int link,shadowLamp;
            public float distance,searchRadius;
            public Vector3 position;
        }
        [Serializable] sealed class Report {
            public string result,failure,scene,timestampUtc,evidenceFolder,shotLamp,standingGround;
            public int checks,ordinaryLamps,adaptedLamps,placedAcolytes,knivesAtStart,knivesAfterShot,knivesAfterDuplicate,
                knivesAfterDarkKnife,knivesAfterLitKnife;
            public Vector3 lampStandingPoint,deathSetup;
            public bool controlledPlayerSetup=true,controlledLethalDamage=true,fullBossPlaythrough=false;
            public string scope="Ordinary lamp shots use the actual player capsule, camera ray, crossbow and E-action method. " +
                "Ending uses controlled placement and 12 holy hits through WallCombat followed by CheckEnd; this is not a manual full boss fight.";
            public List<AcolyteReport> acolytes=new List<AcolyteReport>();
            public List<string> passed=new List<string>(),screenshots=new List<string>();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start() {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-check-gameplay-round2")<0 && Array.IndexOf(args,"-check-demon-death-mist")<0)return;
            var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
            var report=new Report {timestampUtc=DateTime.UtcNow.ToString("o"),evidenceFolder=Argument("-chapter01-evidence") ??
                Path.Combine(Application.persistentDataPath,"GameplayRound2Check")};
            Directory.CreateDirectory(report.evidenceFolder);
            if(lab==null) {report.result="FAIL";report.failure="No ShadowLab in the loaded scene.";Save(report);Application.Quit(1);return;}
            lab.StartCoroutine(Run(lab,report));
        }
        static IEnumerator Run(ShadowLab lab,Report report) {
            lab.enabled=false;
            for(int frame=0;frame<4;frame++)yield return null;
            try {
                Exercise(lab,report);report.result="PASS";
                Debug.Log("GAMEPLAY_ROUND2_CHECK_PASS checks="+report.checks);
            } catch(Exception error) {
                report.result="FAIL";report.failure=error.ToString();
                Debug.LogError("GAMEPLAY_ROUND2_CHECK_FAIL: "+error.Message);Debug.LogException(error);
            } finally {Save(report);}
            Application.Quit(report.result=="PASS" ? 0 : 1);
        }
        static void Exercise(ShadowLab lab,Report report) {
            void Require(bool ok,string message) {
                if(!ok)throw new InvalidOperationException(message);
                report.checks++;report.passed.Add(message);Debug.Log("PASS "+message);
            }
            var hall=lab.GetComponent<IronHallEncounter>();report.scene=lab.gameObject.scene.name;
            Require(hall!=null && hall.UseAuthoredLayout && lab.UI!=null,"the authored chapter scene and real gameplay HUD are loaded");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-demon-death-mist")>=0) {
                report.scope="Controlled final boss defeat in the authored hall, death mist, story, and restart.";
                CheckEnding(lab,hall,report,Require);
                return;
            }
            lab.RestartEncounter();lab.Developer.ClearPauseReasons();
            if(!lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            Invoke(lab.UI,"SetView","battle",null);lab.RefreshOrdinarySceneLamps();
            var lamps=UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None)
                .Where(lamp=>lamp.Encounter==lab && lamp.isActiveAndEnabled).ToArray();
            report.ordinaryLamps=lamps.Length;report.adaptedLamps=lamps.Count(lamp=>lamp.RuntimeSceneAdapter);
            Require(report.adaptedLamps>0,"ordinary visible scene fixtures automatically receive playable lamp adapters");
            Require(lamps.All(lamp=>lamp.EffectiveLight==null || !hall.GalleryLights.Contains(lamp.EffectiveLight)),
                "ordinary lamp registry excludes every boss-room combat light");
            Require(lamps.Where(lamp=>lamp.RuntimeSceneAdapter).All(lamp=>lamp.EffectiveLight!=null &&
                !lamp.EffectiveLight.name.StartsWith("WindowSpill",StringComparison.OrdinalIgnoreCase)),
                "window spill illumination remains outside the automatic fire-lamp registry");
            var actors=UnityEngine.Object.FindObjectsByType<FireThiefAcolyte>(FindObjectsSortMode.None)
                .Where(actor=>actor.Encounter==lab && actor.isActiveAndEnabled && actor.Activated && actor.Alive).ToArray();
            report.placedAcolytes=actors.Length;
            Require(actors.Length>0,"the author's placed acolytes are present; no acceptance enemies were spawned");
            foreach(var actor in actors)actor.Tick(.02f);
            var claimed=new HashSet<int>();
            foreach(var actor in actors) {
                int link=actor.Link;float distance=link>=0 ? Vector3.Distance(actor.transform.position,lab.GetLampPosition(link)) : -1;
                report.acolytes.Add(new AcolyteReport {name=PathOf(actor.transform),link=link,shadowLamp=actor.ShadowLampIndex,
                    lamp=link>=0 ? lamps.FirstOrDefault(lamp=>lab.GetLampIndex(lamp)==link)?.name : "none",
                    position=actor.transform.position,distance=distance,searchRadius=actor.LampSearchRadius});
                Require(link<0 || (link>=HuntBoss.LampCapacity && claimed.Add(link) && distance<=actor.LampSearchRadius+.01f),
                    "placed acolyte has a unique ordinary lamp within its radius, or remains unassigned: "+actor.name);
            }
            report.knivesAtStart=lab.HolyKnifeCount;
            Require(report.knivesAtStart==2 && lab.Weapon.Loaded==Bolt.Holy,"the actual scene starts with two knives and a holy-water crossbow");
            EncounterLantern selected=null;Vector3 stand=default;string groundPath="";
            foreach(var lamp in lamps.Where(x=>x.RuntimeSceneAdapter && x.Target!=null)
                .OrderBy(x=>(x.Position-lab.HunterGroundPosition).sqrMagnitude)) {
                if(FindLampStandingPoint(lab,hall,lamp,out stand,out groundPath)) {selected=lamp;break;}
            }
            Require(selected!=null,"at least one real ordinary lamp has a grounded capsule-clear firing position and unobstructed target");
            report.shotLamp=PathOf(selected.transform);report.lampStandingPoint=stand;report.standingGround=groundPath;
            PlaceHunter(lab,stand,selected.Position);
            Require(FirstSurface(lab,new Ray(lab.Eye.transform.position,(selected.Position-lab.Eye.transform.position).normalized),out var hit) &&
                hit.collider==selected.Target,"the actual hunter's camera ray first intersects the selected ordinary wick");
            selected.Extinguish();Capture(lab,report,"round2-00-ordinary-lamp-dark");
            lab.Shoot();report.knivesAfterShot=lab.HolyKnifeCount;
            Require(selected.Lit && report.knivesAfterShot==Mathf.Min(5,report.knivesAtStart+1),
                "a real holy-water crossbow shot relights an ordinary lamp and grants exactly one knife");
            lab.Weapon.Step(1);lab.Shoot();report.knivesAfterDuplicate=lab.HolyKnifeCount;
            Require(selected.Lit && report.knivesAfterDuplicate==report.knivesAfterShot,
                "a repeated real shot at the lit ordinary lamp grants no extra knife");
            selected.Extinguish();
            Require(lab.ThrowHolyKnife(),"the production E-action accepts a holy fire knife throw at the actual wick");
            for(int i=0;i<20;i++)Invoke(lab,"TickHolyKnives",.05f);
            report.knivesAfterDarkKnife=lab.HolyKnifeCount;
            Require(selected.Lit && report.knivesAfterDarkKnife==report.knivesAfterDuplicate,
                "a knife spent relighting a dark lamp is replenished once, with zero net inventory increase");
            Invoke(lab,"TickHolyKnives",lab.HolyKnifeCooldown+.01f);
            Require(lab.ThrowHolyKnife(),"a second E-action is allowed after the actual throw cooldown");
            for(int i=0;i<20;i++)Invoke(lab,"TickHolyKnives",.05f);
            report.knivesAfterLitKnife=lab.HolyKnifeCount;
            Require(report.knivesAfterLitKnife==report.knivesAfterDarkKnife-1,
                "throwing into an already lit lamp consumes one knife and cannot farm inventory");
            Capture(lab,report,"round2-01-ordinary-lamp-relit-and-knives");
            foreach(var actor in actors)actor.Tick(.02f);
            var nearby=actors.FirstOrDefault(actor=>actor.ShadowLampIndex>=0) ?? actors[0];
            if(FindStandingPoint(hall,nearby.transform.position,nearby.transform.position+Vector3.up*.9f,
                null,out Vector3 actorStand,out _)) {
                PlaceHunter(lab,actorStand,nearby.transform.position+Vector3.up*.6f);
                Capture(lab,report,"round2-02-placed-acolyte-local-shadow");
            }

            CheckEnding(lab,hall,report,Require);
            Require(UnityEngine.Object.FindObjectsByType<FireThiefAcolyte>(FindObjectsSortMode.None)
                .Count(actor=>actor.Encounter==lab && actor.isActiveAndEnabled && actor.Activated)==actors.Length,
                "acceptance preserved the author's placed enemy count");
        }
        static void CheckEnding(ShadowLab lab,IronHallEncounter hall,Report report,Action<bool,string> Require) {
            // Controlled lethal-damage integration. The scene, model and player remain real.
            lab.RestartEncounter();Invoke(lab.UI,"SetView","battle",null);lab.Developer.ClearPauseReasons();
            var state=lab.ShadowState;Vector3 deathStand=state.Enemy+Vector3.back*5;
            deathStand.y=hall.SurfaceHeight(deathStand.x,deathStand.z)+.03f;
            PlaceHunter(lab,deathStand,state.Enemy+Vector3.up*.85f);report.deathSetup=lab.HunterGroundPosition;
            Invoke(lab,"TryBeginEncounter");lab.RefreshView();
            Aim(lab,state.Enemy+Vector3.up*.9f);
            Require(lab.EncounterStarted && !lab.Finished,"controlled five-metre boss setup enters the actual encounter");
            Capture(lab,report,"round2-03-controlled-boss-setup");
            var combat=(WallCombat)typeof(ShadowLab).GetField("combat",Private).GetValue(lab);
            for(int index=0;index<12;index++)combat.Shoot(Bolt.Holy,lab.DemonShadowCenter,true,true);
            Invoke(lab,"CheckEnd");
            Require(lab.Finished && lab.VictoryPresentationActive && lab.UI.CurrentView=="ending",
                "120 holy damage through combat starts the death sequence instead of a result window");
            var narrator=lab.GetComponent<FairyTaleNarrator>();
            var voice=(AudioSource)typeof(FairyTaleNarrator).GetField("voice",Private).GetValue(narrator);
            void CheckEndingLine(string id) {
                var clip=Resources.Load<AudioClip>("Narration/en/"+id);
                Require(narrator.CurrentId==id && clip!=null && voice.clip==clip &&
                    narrator.CurrentSubtitle.Length>0 && narrator.CurrentSubtitle==lab.VictoryStoryText,
                    "ending shows the approved Chinese text alongside its recorded English cue "+id);
            }
            bool showSubtitles=lab.UI.Settings.NarrationSubtitles;
            lab.UI.Settings.NarrationSubtitles=true;
            CheckEndingLine(FairyTaleNarrator.DispersalId);
            Require(lab.VictoryStoryStart>=voice.clip.length,"the dispersal recording finishes before the first story page");
            var view=lab.GetComponentInChildren<ShadowDemonView>();
            Require(view!=null && view.DeathPlaying,"the original boss model performs its death animation");
            Invoke(lab,"TickVictoryPresentation",lab.VictoryDeathSeconds*.65f);
            var mist=lab.transform.Find("Shadow demon death mist");
            var chapelGuardian=UnityEngine.Object.FindFirstObjectByType<FixedLanternGuardian>();
            Require(mist!=null && chapelGuardian!=null && mist.localScale.x>=chapelGuardian.DeathEffectScale*3,
                "the death plume has already grown to over triple the chapel guardian's linear scale");
            var smoke=mist.GetComponent<ParticleSystem>();
            var surface=mist.GetComponent<ParticleSystemRenderer>();
            Require(smoke!=null && smoke.particleCount>0 && chapelGuardian.DeathPrefab!=null &&
                surface.sharedMaterial.mainTexture==chapelGuardian.DeathPrefab.GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture &&
                surface.sharedMaterial.shader.name=="ShadowClaw/DeathMist" && surface.sharedMaterial.shader.isSupported,
                "both bosses use the imported smoke artwork and the final plume contains live particles");
            var tint=new MaterialPropertyBlock();surface.GetPropertyBlock(tint);
            Color dark=tint.GetColor("_Color");
            Require(dark.maxColorComponent<=.21f && dark.a>.7f,"the original smoke material has a near-black instance tint");
            float sampledTime=smoke.time;
            Invoke(lab,"TickVictoryPresentation",0f);
            Require(smoke.time==sampledTime,"holding the ending clock holds the plume");
            Capture(lab,report,"round2-04-boss-death");
            Invoke(lab,"TickVictoryPresentation",lab.VictoryDeathSeconds*.26f);
            Require(view.GetComponentsInChildren<SkinnedMeshRenderer>().All(r=>!r.enabled) &&
                mist.localScale.x>=chapelGuardian.DeathEffectScale*3.9f,"armour has dispersed inside a plume at least 3.9 times the guardian's peak scale");
            Capture(lab,report,"demon-death-mist-peak");
            surface.enabled=false;
            Capture(lab,report,"demon-death-mist-hidden-control");
            surface.enabled=true;
            Invoke(lab,"TickVictoryPresentation",lab.VictoryDeathSeconds*.09f+lab.VictoryFadeOutSeconds*.5f);
            Require(lab.VictoryPhase==VictoryPresentationState.Stage.FadeOut && Mathf.Abs(lab.VictoryBlackOpacity-.5f)<.01f,
                "the fallen boss transitions through half-black rather than an instant panel");
            surface.GetPropertyBlock(tint);
            Require(tint.GetColor("_Color").a<dark.a*.6f,"the smoke opacity fades with the cinematic");
            Capture(lab,report,"round2-05-half-black");
            Invoke(lab,"TickVictoryPresentation",lab.VictoryStoryStart-lab.VictoryDeathSeconds-lab.VictoryFadeOutSeconds*.5f+.8f);
            Require(lab.VictoryPhase==VictoryPresentationState.Stage.Story && lab.VictoryBlackOpacity==1 &&
                !string.IsNullOrEmpty(lab.VictoryStoryText),"story text is displayed after complete blackout");
            Require(!mist.gameObject.activeSelf,"the plume is removed before the story subtitles");
            CheckEndingLine(FairyTaleNarrator.CastleEndingId);
            Require(lab.VictoryPageDuration(0)>=voice.clip.length,"the first black-screen page fits its entire recorded performance");
            Capture(lab,report,"round2-06-story");
            Invoke(lab,"TickVictoryPresentation",lab.VictoryPageDuration(0));
            CheckEndingLine(FairyTaleNarrator.EpilogueId);
            Require(lab.VictoryPageDuration(1)>=voice.clip.length,"the epilogue finishes before the menu reveal");
            Invoke(lab,"TickVictoryPresentation",lab.VictoryPageDuration(1)-.8f+.001f);
            Require(lab.UI.CurrentView=="menu" && lab.VictoryBlackOpacity>.99f,"the homepage is prepared underneath opaque black");
            Require(!narrator.Speaking && voice.clip==null,"no cinematic voice leaks into the homepage");
            lab.UI.Settings.NarrationSubtitles=showSubtitles;
            Invoke(lab,"TickVictoryPresentation",lab.VictoryMenuRevealSeconds*.5f);
            Capture(lab,report,"round2-07-home-reveal");
            Invoke(lab,"TickVictoryPresentation",lab.VictoryMenuRevealSeconds);
            Require(lab.UI.CurrentView=="menu" && !lab.VictoryPresentationActive &&
                !lab.UI.UiCanvas.transform.Find("Victory story transition").gameObject.activeSelf,
                "the transition ends on the interactive homepage without a victory result overlay");
            Require(Application.productName=="灯与影：ShadowClaw" &&
                lab.UI.UiCanvas.GetComponentsInChildren<UnityEngine.UI.Text>().Any(label=>label.text==Application.productName),
                "the interactive homepage displays the published game title");
            Capture(lab,report,"round2-08-home");
            lab.UI.HandleKey(KeyCode.F5);
            Require(!lab.Finished && !view.DeathPlaying && lab.UI.CurrentView=="battle" &&
                lab.HolyKnifeCount==2 && !lab.EncounterStarted,"F5 restores exploration, the living boss and the initial two knives");
            Require(view.GetComponentsInChildren<SkinnedMeshRenderer>().All(r=>r.enabled),"restarting restores the complete original model");
            Invoke(lab,"BeginVictoryPresentation");Invoke(lab,"TickVictoryPresentation",lab.VictoryDeathSeconds*.55f);
            var interrupted=lab.GetComponentsInChildren<ParticleSystem>().First(p=>p.name=="Shadow demon death mist");
            lab.RestartEncounter();
            Require(!interrupted.gameObject.activeSelf && interrupted.particleCount==0 &&
                !view.DeathPlaying,"restarting during a plume clears the particles and restores the living boss immediately");
        }
        static bool FindLampStandingPoint(ShadowLab lab,IronHallEncounter hall,EncounterLantern lamp,out Vector3 point,out string ground) =>
            FindStandingPoint(hall,lamp.Position,lamp.Position,ray=>FirstSurface(lab,ray,out var hit) && hit.collider==lamp.Target,out point,out ground);
        static bool FindStandingPoint(IronHallEncounter hall,Vector3 center,Vector3 target,Func<Ray,bool> sight,out Vector3 point,out string ground) {
            foreach(float radius in new[]{4f,6f,8f,3f})for(int i=0;i<16;i++) {
                float angle=i*Mathf.PI/8;float x=center.x+Mathf.Cos(angle)*radius,z=center.z+Mathf.Sin(angle)*radius;
                var hits=Physics.RaycastAll(new Vector3(x,center.y+8,z),Vector3.down,100,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(hit=>hit.distance);
                foreach(var floor in hits) {
                    if(floor.normal.y<.7f || !hall.IsGround(floor.collider))continue;
                    var foot=floor.point+Vector3.up*.04f;
                    if(Physics.CheckCapsule(foot+Vector3.up*.38f,foot+Vector3.up*1.42f,.34f,
                        Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
                    Vector3 eye=foot+Vector3.up*1.65f,direction=target-eye;
                    var ray=new Ray(eye,direction.normalized);
                    if(sight!=null ? !sight(ray) : Physics.Raycast(ray,direction.magnitude-.25f,
                        Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
                    point=foot;ground=PathOf(floor.transform);return true;
                }
            }
            point=default;ground="";return false;
        }
        static void PlaceHunter(ShadowLab lab,Vector3 foot,Vector3 target) {
            var body=(CharacterController)typeof(ShadowLab).GetField("playerBody",Private).GetValue(lab);
            body.enabled=false;body.transform.position=foot;body.enabled=true;body.Move(Vector3.down*.05f);
            lab.ShadowState.Player=body.transform.position;Physics.SyncTransforms();lab.RefreshView();Aim(lab,target);
        }
        static void Aim(ShadowLab lab,Vector3 target) {
            Quaternion rotation=Quaternion.LookRotation(target-lab.Eye.transform.position);
            Vector3 euler=rotation.eulerAngles;
            typeof(ShadowLab).GetField("yaw",Private).SetValue(lab,euler.y);
            typeof(ShadowLab).GetField("pitch",Private).SetValue(lab,euler.x>180 ? euler.x-360 : euler.x);
            lab.Eye.transform.rotation=rotation;
        }
        static bool FirstSurface(ShadowLab lab,Ray ray,out RaycastHit hit) {
            object[] values={ray,default(RaycastHit)};bool result=(bool)typeof(ShadowLab).GetMethod("FirstShotSurface",Private).Invoke(lab,values);
            hit=(RaycastHit)values[1];return result;
        }
        static void Capture(ShadowLab lab,Report report,string name) {
            Invoke(lab.UI,"RefreshLiveHud");lab.UI.RefreshVictorySequence();
            var canvas=lab.UI.UiCanvas;var camera=lab.Eye;
            var mode=canvas.renderMode;var oldCamera=canvas.worldCamera;float plane=canvas.planeDistance;
            var oldTarget=camera.targetTexture;float aspect=camera.aspect;var previous=RenderTexture.active;
            var target=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32);
            var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            try {
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.005f;
                camera.aspect=1920f/1080;camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(report.evidenceFolder,name+".png"),texture.EncodeToPNG());report.screenshots.Add(name+".png");
            } finally {
                RenderTexture.active=previous;camera.targetTexture=oldTarget;camera.aspect=aspect;
                canvas.renderMode=mode;canvas.worldCamera=oldCamera;canvas.planeDistance=plane;
                RenderTexture.ReleaseTemporary(target);UnityEngine.Object.Destroy(texture);
            }
        }
        static void Invoke(object obj,string method,params object[] args) => obj.GetType().GetMethod(method,Private).Invoke(obj,args);
        static string PathOf(Transform value) => value.parent==null ? value.name : PathOf(value.parent)+"/"+value.name;
        static string Argument(string name) {var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length ? args[index+1] : null;}
        static void Save(Report report) => File.WriteAllText(Path.Combine(report.evidenceFolder,"round2-check.json"),JsonUtility.ToJson(report,true));
    }
}
