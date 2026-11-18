using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Opt-in actual-Player integration. Only the hunter is repositioned. All
    // requests must come from the production gameplay, including the landmark floors.
    public sealed class Chapter01NarrationCheck : MonoBehaviour
    {
        const BindingFlags Member = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        [Serializable] sealed class Report {
            public string result, failure, scene;
            public int checks;
            public List<string> passed = new List<string>(), events = new List<string>(), phases = new List<string>();
        }
        readonly Report report = new Report();
        ShadowLab lab;
        IronHallEncounter hall;
        FairyTaleNarrator narrator;
        string evidence;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            string[] args = Environment.GetCommandLineArgs();
            if (!args.Contains("-check-chapter01-narration")) return;
            var check = new GameObject("Chapter narration verification").AddComponent<Chapter01NarrationCheck>();
            int index = Array.IndexOf(args,"-chapter01-evidence");
            check.evidence = index >= 0 && index + 1 < args.Length ? args[index+1] : Application.persistentDataPath;
            check.StartCoroutine(check.Run());
        }
        IEnumerator Run() {
            // AfterSceneLoad follows Awake and precedes the first gameplay
            // Update. Do not leave the narrator advancing during the splash.
            for (int frame=0;frame<8;frame++) {
                lab = FindFirstObjectByType<ShadowLab>();
                if (lab != null && lab.GameplayReady && lab.UI != null) break;
                yield return null;
            }
            try {
                Check(lab != null && lab.GameplayReady,"chapter gameplay initialized");
                report.scene = lab.gameObject.scene.name;
                hall = lab.GetComponent<IronHallEncounter>();
                Check(hall != null && hall.UseAuthoredLayout,"authored main hall loaded");
                lab.enabled = false;
                narrator = lab.GetComponent<FairyTaleNarrator>();
                narrator.enabled = false;
                Phase("title");
                Check(lab.UI.CurrentView=="menu" && !narrator.Speaking,"the fresh session begins on the title screen");
            } catch (Exception failure) {Finish(failure);}
            if (report.result=="FAIL")yield break;
            // Use the real title and settings pages, which deliberately pause the hunt.
            var sound=lab.Eye.GetComponent<ShiyinAudio>();
            var title=sound.GetComponents<AudioSource>().SingleOrDefault(s=>s.clip!=null && s.clip.name=="Title_Veil");
            float priorMusic=lab.UI.Settings.Music;
            sound.SetLevels(lab.UI.Settings.Effects,.5f);
            float deadline=Time.realtimeSinceStartup+15;
            while(title!=null && (title.timeSamples<2000 || title.volume<.01f) && Time.realtimeSinceStartup<deadline)yield return null;
            try {
                Check(title!=null && title.loop && title.isPlaying && title.timeSamples>0 && title.volume>0 &&
                    sound.MusicDirector.IsPaused,"the looping title arrangement plays softly while the hunt is paused");
                lab.UI.Dispatch("settings");
                Check(title.isPlaying && title.timeSamples>0 && lab.UI.CurrentView=="settings",
                    "opening settings on the title screen keeps the same performance playing");
                sound.SetLevels(lab.UI.Settings.Effects,0);
                Check(title.volume==0,"the music fader also mutes title music");
                lab.UI.Dispatch("back");
                lab.UI.Dispatch("start");
            } catch (Exception failure) {Finish(failure);}
            finally {sound.SetLevels(lab.UI.Settings.Effects,priorMusic);}
            if (report.result=="FAIL")yield break;
            for(int frame=0;frame<30 && lab.UI.CurrentView=="loading";frame++)yield return null;
            deadline=Time.realtimeSinceStartup+8;
            while(title.isPlaying && Time.realtimeSinceStartup<deadline)yield return null;
            try {
                Check(!title.isPlaying && title.volume==0,"starting a hunt fades out and stops the title arrangement");
                if(lab.UI.CurrentView=="onboarding")lab.UI.Dispatch("skip-tutorial");
                Phase("new hunt");
                Check(lab.UI.CurrentView=="battle" && !lab.GameplayPaused,"Start and the optional tutorial open a playable hunt");
                Phase("recordings");
                NarrationResourceChecks.Run(Check);
                Phase("exploration");
                CheckPassages();
                Phase("small enemies and recovery");
                CheckMinorEncounters();
                Phase("guardian");
                CheckGuardian();
                Phase("final battle");
                CheckFinalBattle();
                Check(report.events.Count == 18,"all eighteen approved non-cinematic events came from normal gameplay");
                lab.UI.Dispatch("settings");
                Check(!title.isPlaying && lab.UI.CurrentView=="settings","in-game settings keep the title arrangement silent");
                lab.UI.Dispatch("back");
                lab.UI.Dispatch("menu");
                lab.UI.Dispatch("confirm");
            } catch (Exception failure) {Finish(failure);}
            if (report.result=="FAIL")yield break;
            deadline=Time.realtimeSinceStartup+8;
            while(!title.isPlaying && Time.realtimeSinceStartup<deadline)yield return null;
            try {
                Check(lab.UI.CurrentView=="menu" && title.isPlaying &&
                    sound.GetComponents<AudioSource>().Count(s=>s.clip==title.clip)==1,
                    "returning home starts one title performance again");
                Finish(null);
            } catch (Exception failure) {Finish(failure);}
        }
        void Finish(Exception failure) {
            report.result = failure==null ? "PASS" : "FAIL";
            if(failure!=null){report.failure=failure.ToString();Debug.LogError(report.failure);}
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence,"narration-check.json"),JsonUtility.ToJson(report,true));
            Application.Quit(report.result == "PASS" ? 0 : 1);
        }
        string State() => "view="+lab.UI.CurrentView+" paused="+lab.GameplayPaused+" finished="+lab.Finished+
            " current="+(narrator.CurrentId??"<none>")+" queued="+string.Join(",",
                narrator.ApprovedLines.Where(line=>narrator.IsPending(line.id)).Select(line=>line.id))+
            " hunter="+lab.HunterGroundPosition.ToString("F2")+" frame="+Time.frameCount;
        void Phase(string name) {
            string message=name+" | "+State();report.phases.Add(message);Debug.Log("NARRATION_PHASE "+message);
        }
        void Check(bool passed,string detail) {
            if (!passed) throw new InvalidOperationException(detail);
            report.checks++; report.passed.Add(detail);
        }
        void Cue(string id) {
            if(!narrator.IsPending(id) && narrator.CurrentId!=id)
                throw new InvalidOperationException("Expected "+id+" | "+State());
            Check(true,"gameplay requested " + id);
            if (!report.events.Contains(id)) report.events.Add(id);
        }
        static object Call(object target,string name,params object[] parameters) =>
            target.GetType().GetMethod(name,Member).Invoke(target,parameters);
        void NewHunt() {
            lab.RestartEncounter();lab.Developer.ClearPauseReasons();
            if (!lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            Physics.SyncTransforms();
        }
        void Place(Vector3 feet) {
            var capsule = (CharacterController)typeof(ShadowLab).GetField("playerBody",Member).GetValue(lab);
            capsule.enabled = false; capsule.transform.position = feet; capsule.enabled = true;
            capsule.Move(Vector3.down*.035f);
            lab.ShadowState.Player = capsule.transform.position;
            Physics.SyncTransforms();
        }
        void CheckPassages() {
            narrator.Advance(.01f); Cue("01_adventure");
            narrator.Advance(narrator.LineDuration("01_adventure") + .1f);
            foreach (var floor in new[]{hall.CourtyardNarrationFloor,hall.UpperBridgeNarrationFloor}) {
                string id = floor == hall.CourtyardNarrationFloor ? "22_courtyard" : "23_upper_bridge";
                Check(floor != null && hall.IsGround(floor),"a real authored ground collider owns " + id);
                Vector3 feet = FindWalkablePoint(floor);
                Check(hall.PassageNarrationAt(feet) == id && hall.PassageNarrationAt(feet + Vector3.down*3) == null &&
                    hall.PassageNarrationAt(feet + Vector3.up*3) == null,"only the actual floor elevation qualifies for " + id);
                Place(feet);Call(lab,"Tick",.02f);Cue(id);
                Check(!lab.EncounterStarted,"the passage is accessible while exploring");
            }
        }
        Vector3 FindWalkablePoint(Collider floor) {
            Bounds b = floor.bounds;
            foreach (float x in new[]{0f,-.25f,.25f,-.5f,.5f})
                foreach (float z in new[]{0f,-.25f,.25f,-.5f,.5f}) {
                    var origin = new Vector3(b.center.x + x*b.extents.x,b.max.y+1,b.center.z+z*b.extents.z);
                    if (!floor.Raycast(new Ray(origin,Vector3.down),out var hit,b.size.y+2) || hit.normal.y<.55f) continue;
                    Vector3 p = hit.point+Vector3.up*.04f;
                    if (!Physics.CheckCapsule(p+Vector3.up*.39f,p+Vector3.up*1.4f,.32f,
                        Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) return p;
                }
            throw new InvalidOperationException("No walkable point on " + floor.name);
        }
        void CheckMinorEncounters() {
            NewHunt();
            var swarm = FindObjectsByType<GroundShadowSwarm>(FindObjectsSortMode.None).First(a=>a.Encounter==lab && a.Activated);
            Place(swarm.transform.position);swarm.Tick(.05f);Cue("02_ground_shadows");
            var thief = FindObjectsByType<FireThiefAcolyte>(FindObjectsSortMode.None).First(a=>a.Encounter==lab && a.Activated && a.Link>=0);
            var lamp = FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None).First(l=>lab.GetLampIndex(l)==thief.Link);
            lamp.Ignite();
            Vector3 aim=Vector3.Lerp(thief.FootPosition+Vector3.up*1.25f,lamp.Position,.3f);
            Place(thief.FootPosition+Vector3.forward*1.4f);lab.RefreshView();
            lab.Eye.transform.rotation=Quaternion.LookRotation(lab.Eye.transform.position-aim);thief.Tick(.02f);
            Check(thief.Action==FireThiefCycle.Action.Prayer && thief.Connected,"the placed thief prays and establishes its tether on sight");
            Cue("03_fire_thief");
            // Try several real viewpoints beside this thief. All are in engagement
            // range; the cue itself still requires an on-screen, unoccluded line.
            for(int i=0;i<12 && !narrator.IsPending("11_acolyte_shadow");i++) {
                float angle=i*Mathf.PI/6;
                Place(thief.FootPosition+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*1.8f);
                lab.RefreshView();lab.Eye.transform.rotation=Quaternion.LookRotation(aim-lab.Eye.transform.position);
                thief.Tick(.21f);
            }
            Cue("11_acolyte_shadow");
            narrator.Advance(.01f);
            Check(narrator.CurrentId=="03_fire_thief" && narrator.IsPending("11_acolyte_shadow"),"the theft line starts before the black tether's line");
            Check(!narrator.IsPending("12_holy_knife"),"starting knives do not request the relight reward narration");
            thief.Tick(thief.PrayerDuration+.1f);
            Check(!lamp.Lit,"the actual prayer puts out its lamp");Check(!narrator.IsPending("04_purple_flame") && narrator.CurrentId!="04_purple_flame","extinguishing a lamp leaves the retired narration silent");
            int before=lab.HolyKnifeCount;
            Check(lab.TryIgniteLamp(lamp.Target) && lab.HolyKnifeCount==before+1,"a real relight grants one new knife");
            Cue("12_holy_knife");
            if (lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            lab.DamageHunter(10);
            Check(!narrator.IsPending("21_first_boss_wound"),"wounds outside the final battle do not request the first-room wound line");
            var boss=(HuntBoss)typeof(ShadowLab).GetField("boss",Member).GetValue(lab);
            boss.StepRecovery(HuntBoss.RecoveryDelay+.05f); Call(lab,"TickAuthoredContent",.02f);
            Check(lab.Recovering,"the hunter actually enters passive recovery");Cue("06_recovery");
        }
        void CheckGuardian() {
            NewHunt();
            var guardian = FindObjectsByType<FixedLanternGuardian>(FindObjectsSortMode.None).Single(g=>g.Encounter==lab);
            Place(guardian.CurrentFrame().ArenaCenter);guardian.Tick(.01f);Cue("13_guardian_entrance");
            for (int i=0;i<450 && guardian.State.Action!=FixedGuardianState.Move.Ritual;i++)guardian.Tick(.2f);
            Check(guardian.State.Action==FixedGuardianState.Move.Ritual,"the guardian's normal attack sequence enters its lamp prayer");
            Cue("14_guardian_trial");
            var view=(FixedGuardianView)typeof(FixedLanternGuardian).GetField("view",Member).GetValue(guardian);
            var aim=view.ShadowProjection.Aim;
            var ray=new Ray(new Vector3(aim.x,guardian.CurrentFrame().Origin.y+12,aim.y),Vector3.down);
            bool lit=guardian.TryHitShadow(ray,25,out float hit) && guardian.TryIgniteShadow(ray.GetPoint(hit));
            Check(lit && guardian.ShadowBurning,"holy fire ignites an actual visible part of the guardian silhouette");Cue("19_guardian_shadow_fire");
            for (int i=0;i<60 && guardian.Alive;i++)guardian.ReceiveShadowHit(Bolt.Holy,Vector3.zero);
            Check(!guardian.Alive && lab.GuardianRelicDrop!=null,"the guardian dies and its relic reaches the floor");Cue("15_guardian_defeated");
            Check(!narrator.IsPending(FairyTaleNarrator.GuardianRelicPickupId),"dropping the relic does not narrate its pickup");
            Place(lab.GuardianRelicDrop.transform.position); Call(lab,"TickGuardianRelic",.02f);
            Check(lab.HasGuardianRelic,"the hunter collected the dropped relic");Cue(FairyTaleNarrator.GuardianRelicPickupId);
        }
        void CheckFinalBattle() {
            NewHunt();
            var gate=hall.FogGate;
            Vector3 feet=gate.FloorCenter + gate.Inward*(gate.EntryClearance+1.8f);
            feet.y=hall.SurfaceHeight(feet.x,feet.z)+.05f;
            Place(feet);Call(lab,"TryBeginEncounter");
            Check(lab.EncounterStarted && gate.Sealed,"crossing the authored fog gate seals the final battle");Cue("16_final_fog_gate");
            if (lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            lab.DamageHunter(5);Check(lab.HunterHealth>0 && lab.HunterHealth<HuntBoss.HunterMaxHealth,"the actual hunter took nonlethal damage");
            Cue("21_first_boss_wound");
            for (int i=0;i<HuntBoss.LampCapacity;i++)lab.TryExtinguishLamp(i);
            Check(lab.LitLamps==0,"all six final-room lights are out");Cue("17_final_blackout");
            var lamps=(Array)typeof(ShadowLab).GetField("lamps",Member).GetValue(lab);
            for (int i=0;i<3;i++) {
                var slot=lamps.GetValue(i);
                var target=(Collider)slot.GetType().GetField("Target",Member).GetValue(slot);
                Check(lab.TryIgniteLamp(target),"the hunter relights final-room wick "+i);
                if(i==0) {
                    Check(lab.BossAction==HuntBoss.Move.LightStagger && !narrator.IsPending("17_final_blackout"),
                        "relighting ends the hunt and removes its unsaid dark-state warning");
                    Cue("18_final_relight");
                }
            }
            Cue("05_more_lamps");
        }
    }
}
