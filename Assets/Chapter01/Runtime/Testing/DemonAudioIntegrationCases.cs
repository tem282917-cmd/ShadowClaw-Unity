using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Opt-in acceptance helper for a disposable play-test encounter. Resets the hunt;
// never saves a scene, creates authored actors, or starts itself automatically.
public static class DemonAudioIntegrationCases
{
    const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Read(object owner,string name) => owner.GetType().GetField(name,Instance).GetValue(owner);
    static object Call(object owner,string name,params object[] args) => owner.GetType().GetMethod(name,Instance).Invoke(owner,args);

    public static string[] Run(ShadowLab lab) {
        if(lab==null || !lab.GameplayReady)throw new ArgumentException("Demon audio checks need an initialized disposable encounter.");
        var results=new List<string>();
        void Check(bool condition,string text) {
            if(!condition)throw new InvalidOperationException("Demon audio: "+text);
            results.Add("PASS demon audio: "+text);
        }
        var audio=ShiyinAudio.Ensure(lab.Eye);var ui=lab.UI;
        string oldView=ui!=null ? ui.CurrentView : "battle";
        float oldEffects=(float)Read(audio,"effects"),oldMusic=(float)Read(audio,"music");
        bool oldFrozen=lab.Developer.Frozen;
        HuntBoss boss=null;WallCombat combat=null;
        void Observe(float dt=0) => Call(lab,"TickBossAudio",dt);
        void Enter(HuntBoss.Move move,float duration=1) => Call(boss,"Enter",move,duration);
        void Prepare() {
            lab.RestartEncounter();
            if(ui!=null)Call(ui,"SetView","battle",null);
            lab.Developer.SetFrozen(false);audio.SetPaused(false);
            typeof(ShadowLab).GetProperty("EncounterStarted",Instance).SetValue(lab,true);
            boss=(HuntBoss)Read(lab,"boss");combat=(WallCombat)Read(lab,"combat");
            boss.HunterHealthLocked=true;
            // Authored halls reset into exploration, which intentionally clears
            // boss shadows. Build the same lit shadow set as actual room entry.
            Call(lab,"ApplyLighting");
            Check(lab.LitLamps>0 && ((List<ClawState>)Read(lab,"shadows")).Contains(lab.ShadowState),
                "prepared encounter has the real lit boss shadow registered after exploration reset");
            Call(lab,"ResetBossAudio");
        }
        try {
            Prepare();
            Check(audio.DemonSoundsAvailable,"all sixteen recorded cue families load from Resources/DemonSfx");
            int arrival=audio.DemonSoundCount(DemonSound.Arrival);Observe();Observe(.02f);Observe(.02f);
            Check(audio.DemonSoundCount(DemonSound.Arrival)==arrival+1,"entering a hunt plays one arrival; repeated ticks do not replay it");

            int tell=audio.DemonSoundCount(DemonSound.RiftTell),sweep=audio.DemonSoundCount(DemonSound.Sweep);
            Enter(HuntBoss.Move.Rift,.1f);Observe();Observe(.02f);
            Check(audio.DemonSoundCount(DemonSound.RiftTell)==tell+1,"rift anticipation plays once during the telegraph");
            var shadows=(List<ClawState>)Read(lab,"shadows");
            boss.StepWithLights(.2f,lab.ShadowState,shadows,lab.ShadowState,0);
            Observe();Observe();
            Check(audio.DemonSoundCount(DemonSound.Sweep)==sweep+1 && boss.Action==HuntBoss.Move.Recover,
                "actual Rift StrikeReleased plays its sweep even when the same step enters recovery");
            int impact=audio.DemonSoundCount(DemonSound.Impact);
            Enter(HuntBoss.Move.Burst,.1f);Observe();boss.StepWithLights(.2f,lab.ShadowState,shadows,lab.ShadowState,0);Observe();
            Check(audio.DemonSoundCount(DemonSound.Impact)==impact+1,"burst release produces one positioned impact");

            var state=lab.ShadowState;Vector3 oldEnemy=state.Enemy,oldPlayer=state.Player,oldLamp=state.Lamp;
            state.Interrupt();state.Enemy=state.Player+Vector3.right;
            if(state.Ground)state.SetLamp(state.LightId,state.Player+new Vector3(2,6,2));
            Enter(HuntBoss.Move.Claw,ClawState.EndTime);Observe();
            Check(state.StartCast(),"the real claw cast can lock the test player's body");
            impact=audio.DemonSoundCount(DemonSound.Impact);
            state.Player+=Vector3.back*2;state.Step(ClawState.CloseTime+.01f);Observe();Observe();
            Check(state.Resolved && audio.DemonSoundCount(DemonSound.Impact)==impact+1 &&
                  Vector3.Distance(audio.LastDemonSoundPosition,state.BodyLockPoint)<.001f,
                "a dodged claw still sounds once at the locked ground position, not at the moving listener");
            state.Interrupt();Observe();state.Enemy=oldEnemy;state.Player=oldPlayer;state.SetLamp(state.LightId,oldLamp);

            Enter(HuntBoss.Move.Stalk);Observe();
            int ritual=audio.DemonSoundCount(DemonSound.Ritual),stagger=audio.DemonSoundCount(DemonSound.Stagger);
            Check(boss.BeginRitual(),"ritual starts through its production state transition");Observe();
            boss.BreakRitual();Observe();Observe();
            Check(audio.DemonSoundCount(DemonSound.Ritual)==ritual+1 && audio.DemonSoundCount(DemonSound.Stagger)==stagger+1,
                "breaking a ritual plays exactly one distinct stagger cue");
            Enter(HuntBoss.Move.Stalk);Observe();Check(boss.BeginRitual(),"a second ritual can begin");Observe();
            stagger=audio.DemonSoundCount(DemonSound.Stagger);
            boss.StepWithLights(2,lab.ShadowState,shadows,lab.ShadowState,0);Observe();
            Check(boss.RitualComplete && audio.DemonSoundCount(DemonSound.Stagger)==stagger,
                "a successfully completed ritual is not incorrectly voiced as an interruption");

            Enter(HuntBoss.Move.Stalk);Observe();Check(boss.BeginRitual(),"a ritual can be active when its last lamp expires");
            boss.StepWithLights(.01f,lab.ShadowState,shadows,lab.ShadowState,0);Observe();
            int dark=audio.DemonSoundCount(DemonSound.DarkHunt),extinguish=audio.DemonSoundCount(DemonSound.Extinguish);
            stagger=audio.DemonSoundCount(DemonSound.Stagger);
            for(int i=0;i<HuntBoss.LampCapacity;i++)lab.TryExtinguishLamp(i);
            Observe();Observe();
            Check(lab.LitLamps==0 && audio.DemonSoundCount(DemonSound.DarkHunt)==dark+1 &&
                  audio.DemonSoundCount(DemonSound.Extinguish)==extinguish+1,
                "all lights going out gives one extinguish transient and one dark-hunt entrance, not six stacked voices");
            Check(audio.DemonSoundCount(DemonSound.Stagger)==stagger,
                "a ritual abandoned because the last lamp goes dark does not falsely sound like a player interrupt");
            Enter(HuntBoss.Move.Pounce);Observe();Enter(HuntBoss.Move.Pursue);Observe();
            Check(audio.DemonSoundCount(DemonSound.DarkHunt)==dark+1,"repeated dark pursuit cycles do not restart the entrance roar");
            stagger=audio.DemonSoundCount(DemonSound.Stagger);
            var lamps=(Array)Read(lab,"lamps");Call(lab,"ShootLamp",lamps.GetValue(0));Observe();Observe();
            Check(lab.LitLamps>0 && audio.DemonSoundCount(DemonSound.Stagger)==stagger+1,
                "relighting a real boss lamp produces one light-stagger sound");

            int hurt=audio.DemonSoundCount(DemonSound.Hurt);
            combat.Shoot(Bolt.Holy,Vector2.zero,false,true);Observe();
            Check(audio.DemonSoundCount(DemonSound.Hurt)==hurt,"a missed holy arrow does not produce boss pain");
            combat.Shoot(Bolt.Holy,Vector2.zero,true,true);Observe();Observe();
            combat.Shoot(Bolt.Fire,Vector2.zero,true,true);
            for(int i=0;i<6;i++) {combat.Step(.02f,shadows);Observe(.02f);}
            Check(audio.DemonSoundCount(DemonSound.Hurt)==hurt+1,"real damage gives pain while six rapid burn ticks are throttled");
            Observe(.5f);combat.Shoot(Bolt.Holy,Vector2.zero,true,true);Observe();
            Check(audio.DemonSoundCount(DemonSound.Hurt)==hurt+2,"a later actual hit can sound again after the pain cooldown");

            audio.SetLevels(.27f,oldMusic);
            bool volumes=true,spatial=true,roomCoverage=true;
            var hall=lab.GetComponent<IronHallEncounter>();float diagonal=hall!=null ? hall.PlayArea.size.magnitude : 20;
            foreach(var voice in lab.GetComponentsInChildren<AudioSource>(true))if(voice.name.StartsWith("Shadow demon voice",StringComparison.Ordinal)) {
                volumes &= Mathf.Abs(voice.volume-.27f)<.001f;
                spatial &= voice.spatialBlend==1 && voice.dopplerLevel==0 && voice.transform.parent!=lab.Eye.transform;
                roomCoverage &= voice.maxDistance>=Mathf.Max(65,diagonal*2.5f)-.001f;
            }
            Check(volumes && spatial && roomCoverage,"boss voices follow Effects gain, remain in world space, and cover the authored room diagonal");
            int pausedTell=audio.DemonSoundCount(DemonSound.LungeTell);
            if(ui!=null)Call(ui,"SetView","pause",null);else {lab.Developer.SetFrozen(true);audio.SetPaused(true);}
            Enter(HuntBoss.Move.LungeTell);Observe(.2f);
            Check((bool)Read(audio,"paused") && !audio.PlayDemonSound(DemonSound.LungeTell,Vector3.zero) &&
                  audio.DemonSoundCount(DemonSound.LungeTell)==pausedTell,"pausing blocks gameplay and direct boss sound triggers");
            if(ui!=null)Call(ui,"SetView","battle",null);else {lab.Developer.SetFrozen(false);audio.SetPaused(false);}
            Observe();Check(audio.DemonSoundCount(DemonSound.LungeTell)==pausedTell+1,"resuming observes the pending phase once");

            int death=audio.DemonSoundCount(DemonSound.Death);
            while(combat.Damage<HuntBoss.DemonHealth)combat.Shoot(Bolt.Holy,Vector2.zero,true,true);
            Call(lab,"CheckEnd");Call(lab,"CheckEnd");Observe();
            Check(lab.Finished && audio.DemonSoundCount(DemonSound.Death)==death+1 && audio.DemonDeathTailReserved,
                "real lethal damage reserves one death tail before the ending switches views");
            if(ui!=null)Check(ui.CurrentView=="ending" && (bool)Read(audio,"paused"),
                "the ending pauses battle playback while retaining its dedicated death source");
            // Source reservation is observable without an audio device. Listening and
            // actual sample playback remain separate manual mix acceptance checks.
            audio.ResetCombatEffects();
            Check(!audio.DemonDeathTailReserved,"menu/restart combat cleanup also releases the dedicated death tail");
            Prepare();arrival=audio.DemonSoundCount(DemonSound.Arrival);Observe();Observe();
            Check(audio.DemonSoundCount(DemonSound.Arrival)==arrival+1 && !audio.DemonDeathTailReserved,
                "a fresh hunt clears old playback and can play its arrival exactly once again");
            boss.HunterHealthLocked=false;boss.HurtUnscaled(HuntBoss.HunterMaxHealth);
            death=audio.DemonSoundCount(DemonSound.Death);Call(lab,"CheckEnd");
            Check(lab.Finished && audio.DemonSoundCount(DemonSound.Death)==death && !audio.DemonDeathTailReserved,
                "hunter defeat stops boss voices without pretending that the demon died");
            return results.ToArray();
        } finally {
            lab.RestartEncounter();audio.SetLevels(oldEffects,oldMusic);audio.ResetCombatEffects();
            if(ui!=null)Call(ui,"SetView",oldView,null);
            lab.Developer.SetFrozen(oldFrozen);
        }
    }
}
