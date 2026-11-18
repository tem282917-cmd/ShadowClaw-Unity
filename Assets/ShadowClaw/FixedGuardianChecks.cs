using System;
using System.Collections.Generic;
using UnityEngine;

public static class FixedGuardianChecks {
    static void Check(bool condition,string text,List<string> rows) {
        if(!condition)throw new Exception("FAIL guardian: "+text);
        rows.Add("PASS guardian: "+text);
    }
    static FixedGuardianFrame Frame() => new FixedGuardianFrame {
        Hunter=Vector3.right*8,Origin=Vector3.zero,ArenaCenter=Vector3.zero,
        ArenaRight=Vector3.right,ArenaForward=Vector3.forward,ArenaSize=new Vector2(24,24),
        LampCount=3,LitCount=3,NextLitLamp=1
    };
    static void Until(FixedGuardianState state,FixedGuardianFrame frame,FixedGuardianState.Move action) {
        for(int i=0;i<4000 && state.Action!=action;i++)state.Tick(.05f,frame);
        if(state.Action!=action)throw new Exception("Guardian never reached "+action);
    }
    public static string[] Run() {
        var rows=new List<string>();
        var frame=Frame();var tuning=new FixedGuardianTuning();
        var state=new FixedGuardianState(tuning);state.Begin(frame);
        Until(state,frame,FixedGuardianState.Move.SlamTrack);
        Check(Mathf.Approximately(state.Remaining,.275f),"slam tracks for 0.275 seconds",rows);
        Until(state,frame,FixedGuardianState.Move.SlamLocked);
        Check(Mathf.Approximately(state.Remaining,.6f),"locked slam gives 0.6 seconds to leave",rows);
        Vector3 target=state.LockedPoint;
        frame.Hunter=Vector3.left*8;int previous=state.DamageEvents;
        state.Tick(.1f,frame);
        Check(state.LockedPoint==target,"slam freezes its target after tracking",rows);
        Until(state,frame,FixedGuardianState.Move.Recover);
        Check(state.DamageEvents==previous,"moving the body clear of the locked slam avoids damage",rows);
        foreach(float offset in new[]{4.19f,4.21f}) {
            state=new FixedGuardianState(tuning);frame=Frame();state.Begin(frame);
            Until(state,frame,FixedGuardianState.Move.SlamLocked);
            frame.Hunter=state.LockedPoint+Vector3.forward*offset;
            state.Tick(state.Remaining+.01f,frame);
            Check(state.DamageEvents==(offset<4.2f?1:0),"slam collision agrees with the 4.2m circle at "+offset+"m",rows);
        }

        state=new FixedGuardianState(tuning);frame=Frame();state.Begin(frame);
        Until(state,frame,FixedGuardianState.Move.WaveTravel);previous=state.DamageEvents;
        while(state.WaveRadius<6 && state.Action==FixedGuardianState.Move.WaveTravel)state.Tick(.05f,frame);
        Check(state.DamageEvents==previous,"wave cannot hurt before its visible front arrives",rows);
        Until(state,frame,FixedGuardianState.Move.Recover);
        Check(state.DamageEvents==previous+1,"travelling wave damages the body once at crossing",rows);

        state=new FixedGuardianState(tuning);frame=Frame();state.Begin(frame);
        Until(state,frame,FixedGuardianState.Move.WaveTell);
        Check(Mathf.Approximately(state.Remaining,.6f),"low wave warns for 0.6 seconds",rows);
        frame.JumpHeight=1.2f;
        Until(state,frame,FixedGuardianState.Move.WaveTravel);previous=state.DamageEvents;
        Until(state,frame,FixedGuardianState.Move.Recover);
        Check(state.DamageEvents==previous,"jumping above the low wave avoids its body hit",rows);

        state=new FixedGuardianState(tuning);frame=Frame();state.Begin(frame);
        Until(state,frame,FixedGuardianState.Move.Ritual);
        Check(Mathf.Approximately(state.Remaining,2f),"lamp prayer warns for two seconds",rows);
        state.LampShot(2,true);
        Check(state.Action==FixedGuardianState.Move.Ritual,"shooting another lamp does not cancel the linked ritual",rows);
        state.LampShot(1,true);
        Check(state.Action==FixedGuardianState.Move.Recover && state.Remaining>=3,
            "shooting the linked lamp cancels the ritual and grants a counter window",rows);

        state=new FixedGuardianState(tuning);frame=Frame();state.Begin(frame);
        frame.LitCount=0;state.Tick(.05f,frame);
        Check(state.Dark && state.Action==FixedGuardianState.Move.SlamTrack,"all dark starts a fixed remote slam",rows);
        int health=state.Health;state.ReceiveDamage(100);
        Check(state.Health==health,"no effective light means the guardian cannot be damaged",rows);
        frame.LitCount=1;state.Tick(.05f,frame);
        Check(!state.Dark && state.Action==FixedGuardianState.Move.Recover,
            "relighting any linked lamp immediately cancels a dark attack",rows);

        state=new FixedGuardianState(tuning);frame=Frame();state.Begin(frame);
        state.ReceiveDamage(130);
        Until(state,frame,FixedGuardianState.Move.SweepTell);
        int side=state.SweepSide;
        Until(state,frame,FixedGuardianState.Move.SweepImpact);
        Until(state,frame,FixedGuardianState.Move.SweepTell);
        Check(state.SweepSide==-side && Mathf.Approximately(state.Remaining,.875f),
            "second phase changes sweep sides with a complete second telegraph",rows);
        Until(state,frame,FixedGuardianState.Move.SweepImpact);
        Until(state,frame,FixedGuardianState.Move.Ritual);
        Check(state.RitualLamp==1,"late sweep and lamp prayer form an ordered combination",rows);
        state.Reset();
        Check(state.Action==FixedGuardianState.Move.Dormant && state.Health==tuning.Health && !state.Dark,
            "reset clears attacks, darkness and damage",rows);
        return rows.ToArray();
    }
}
