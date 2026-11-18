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
    // Opt-in checks against the placed monsters and real HUD, with a controlled hunter position and combat clock.
    public sealed class CombatGuidanceCheck : MonoBehaviour
    {
        const string GhostLine="攻击它们的影子";
        const string AcolyteLines="当侍僧窃取灯光时，它是无敌的\n熄灭紫色的灯火";
        const string HeartLine="按q使用灯心回血";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] sealed class Report {
            public string result,failure;
            public List<string> passed=new List<string>();
        }
        readonly Report report=new Report();
        ShadowLab lab;
        GroundShadowSwarm ghost;
        FireThiefAcolyte thief;
        Text caption;
        string folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            var args=Environment.GetCommandLineArgs();
            if(!args.Contains("-check-combat-guidance"))return;
            var test=new GameObject("Combat guidance verification").AddComponent<CombatGuidanceCheck>();
            int i=Array.IndexOf(args,"-chapter01-evidence");
            test.folder=i>=0 && i+1<args.Length ? args[i+1] : Application.persistentDataPath;
            test.StartCoroutine(test.Run());
        }
        IEnumerator Run() {
            float deadline=Time.realtimeSinceStartup+25;
            do {
                yield return null;
                lab=FindFirstObjectByType<ShadowLab>();
            } while((lab==null || !lab.GameplayReady || lab.UI==null || !UnityEngine.Rendering.SplashScreen.isFinished) &&
                Time.realtimeSinceStartup<deadline);
            float firstY=0;
            try {
                lab.enabled=false;
                lab.RestartEncounter();
                lab.Developer.ClearPauseReasons();
                lab.UI.Settings.ReducedMotion=false;
                caption=lab.UI.UiCanvas.transform.Find("Battle HUD/Combat guidance").GetComponent<Text>();
                var pair=(from g in FindObjectsByType<GroundShadowSwarm>(FindObjectsSortMode.None)
                          from t in FindObjectsByType<FireThiefAcolyte>(FindObjectsSortMode.None)
                          where g.Activated && t.Activated && g.Encounter==lab && t.Encounter==lab
                          orderby (g.transform.position-t.FootPosition).sqrMagnitude
                          select new {g,t}).First();
                ghost=pair.g;thief=pair.t;
                CheckCaption("","no instructions before first contact");
                Face(ghost.transform.position,Vector3.back);
                ghost.Tick(.02f);CombatStep(.02f);
                Check(ghost.Engaged,"the authored ghost swarm engages");
                CheckCaption(GhostLine,"the first ghost instruction appears");
                Round3FrameCapture.SaveCurrentFrame(lab,folder,"guidance-wisp");
                firstY=caption.rectTransform.anchoredPosition.y;
            } catch(Exception error){Finish(error);}
            if(report.result=="FAIL")yield break;
            deadline=Time.realtimeSinceStartup+2;
            while(Mathf.Abs(caption.rectTransform.anchoredPosition.y-firstY)<.15f && Time.realtimeSinceStartup<deadline)
                yield return null;
            try {
                Check(Mathf.Abs(caption.rectTransform.anchoredPosition.y-firstY)>=.15f,"the visible caption floats vertically");
                MinorFights();
                FinalFight();
                Finish(null);
            } catch(Exception error){Finish(error);}
        }
        void MinorFights() {
            lab.UI.Dispatch("settings");
            CheckCaption("","pausing hides the guidance");
            lab.UI.Dispatch("back");
            CheckCaption(GhostLine,"resuming restores the ongoing first encounter");
            Face(thief.FootPosition,ghost.transform.position-thief.FootPosition);
            thief.Tick(.02f);CombatStep(.02f);
            Check(thief.Engaged && ghost.Engaged,"both real actors have entered combat");
            CheckCaption(GhostLine+"\n"+AcolyteLines,"overlapping first encounters retain both instructions and the explicit line break");
            Round3FrameCapture.SaveCurrentFrame(lab,folder,"guidance-acolyte");
            CombatStep(12);
            CheckCaption(GhostLine+"\n"+AcolyteLines,"a long combat interval does not expire either caption");
            lab.ShadowState.Player=thief.FootPosition+Vector3.right*500;
            ghost.Tick(.02f);thief.Tick(.02f);CombatStep(.02f);
            Check(!ghost.Engaged && !thief.Engaged,"both actors disengage by distance");
            if(lab.MinorCombatExitDelay>.02f)CheckCaption(GhostLine+"\n"+AcolyteLines,"captions persist during combat's exit delay");
            CombatStep(lab.MinorCombatExitDelay+1);
            CheckCaption("","actual combat exit clears the first-encounter captions");
            Face(ghost.transform.position,Vector3.back);ghost.Tick(.02f);
            Face(thief.FootPosition,Vector3.left);thief.Tick(.02f);CombatStep(.02f);
            Check(ghost.Engaged && thief.Engaged,"both enemies re-engage");
            CheckCaption("","later encounters in the same hunt do not repeat the instructions");
            lab.RestartEncounter();lab.Developer.ClearPauseReasons();
            Face(thief.FootPosition,Vector3.left);thief.Tick(.02f);CombatStep(.02f);
            CheckCaption(AcolyteLines,"a new hunt can introduce the acolyte before the ghosts");
            Round3FrameCapture.SaveCurrentFrame(lab,folder,"guidance-acolyte-alone");
        }
        void FinalFight() {
            lab.RestartEncounter();lab.Developer.ClearPauseReasons();
            if(lab.Developer.HealthLocked)lab.Developer.HandleKey(KeyCode.F2);
            var hall=lab.GetComponent<IronHallEncounter>();
            var gate=hall.FogGate;
            var guardian=FindFirstObjectByType<FixedLanternGuardian>();
            lab.ShadowState.Player=guardian.CurrentFrame().ArenaCenter;
            guardian.Tick(.02f);lab.DamageHunter(10);
            Check(lab.ActiveGuardian==guardian && !lab.EncounterStarted,"the mid-boss fight is active");
            CheckCaption("","guardian damage does not request the final-boss healing caption");
            lab.RestartEncounter();lab.Developer.ClearPauseReasons();
            var boss=(HuntBoss)typeof(ShadowLab).GetField("boss",Private).GetValue(lab);
            lab.ShadowState.Player=gate.FloorCenter+gate.Inward*(gate.EntryClearance+1.8f);
            Call(lab,"TryBeginEncounter");
            Check(lab.EncounterStarted,"the hunter crosses the real fog gate");
            CheckCaption("","full health in the final fight has no healing prompt");
            lab.DamageHunter(35);lab.RefreshView();lab.Eye.transform.LookAt(hall.DemonStart+Vector3.up*2);
            CheckCaption(HeartLine,"any missing health shows the final-boss instruction, including without a relic");
            Round3FrameCapture.SaveCurrentFrame(lab,folder,"guidance-relic");
            Call(lab,"TickGuardianRelic",6f);
            CheckCaption(HeartLine,"the persistent prompt outlasts the first wound toast");
            boss.RestoreHunterHealth(30);
            CheckCaption(HeartLine,"partial healing keeps the instruction");
            boss.RestoreHunterHealth(5);
            CheckCaption("","reaching full health clears it");
            lab.DamageHunter(1);
            CheckCaption(HeartLine,"a later wound restores it");
            lab.DamageHunter(500);
            CheckCaption("","player death clears every instruction");
        }
        void Face(Vector3 target,Vector3 side) {
            side=Vector3.ProjectOnPlane(side,Vector3.up).normalized;
            if(side==Vector3.zero)side=Vector3.back;
            var feet=target+side*5;
            if(lab.TryMinorGround(feet,target.y,out var floor))feet=floor;
            lab.ShadowState.Player=feet;
            lab.RefreshView();lab.Eye.transform.LookAt(target+Vector3.up*1.35f);
        }
        void CombatStep(float seconds)=>Call(lab,"TickStamina",seconds);
        static void Call(object target,string method,params object[] args)=>
            target.GetType().GetMethod(method,Private).Invoke(target,args);
        void CheckCaption(string expected,string detail) {
            Call(lab.UI,"RefreshLiveHud");
            Check(expected.Length==0 ? !caption.gameObject.activeInHierarchy :
                caption.gameObject.activeInHierarchy && caption.text==expected,detail+" [actual "+caption.text+"]");
        }
        void Check(bool ok,string detail) {
            if(!ok)throw new InvalidOperationException(detail);
            report.passed.Add(detail);
        }
        void Finish(Exception error) {
            report.result=error==null?"PASS":"FAIL";report.failure=error?.ToString();
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"combat-guidance.json"),JsonUtility.ToJson(report,true));
            if(error!=null)Debug.LogException(error);
            Application.Quit(error==null?0:1);
        }
    }
}
