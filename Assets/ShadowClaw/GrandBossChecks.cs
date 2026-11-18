using System;
using System.Collections.Generic;
using UnityEngine;

public static class GrandBossChecks {
    static void Check(bool condition,string label,List<string> log) {
        if(!condition)throw new InvalidOperationException("FAIL "+label);
        log.Add("PASS "+label);
    }
    static ClawState World() {
        var world=new ClawState(new WallInk(true),new WallInk(true),new WallInk(true),new WallInk(true),true);
        world.Enemy=Vector3.zero;world.Player=new Vector3(2,0,2);
        return world;
    }
    static void Step(HuntBoss boss,ClawState world,float dt) => boss.StepWithLights(dt,world,new[]{world},world,0);
    static void Until(HuntBoss boss,ClawState world,HuntBoss.Move target) {
        for(int i=0;i<6000 && boss.Action!=target;i++) {
            if(boss.Action==HuntBoss.Move.SleeveTell)
                world.Player=new Vector3(boss.MoveBounds.center.x+(boss.SweepRight?-1:1),0,boss.MoveBounds.center.y);
            world.Step(.02f);Step(boss,world,.02f);
        }
        if(boss.Action!=target)throw new InvalidOperationException("Could not reach "+target);
    }
    static HuntBoss Bell(ClawState world,int phase) {
        var boss=new HuntBoss {GrandAttacks=true,MoveBounds=Rect.MinMaxRect(-10,-10,10,10),HunterHealthLocked=true};
        boss.SetDemonDamage(phase==3 ? HuntBoss.DemonHealth*.7f : phase==2 ? HuntBoss.DemonHealth*.4f : 0);
        Until(boss,world,HuntBoss.Move.BellTell);
        world.Player=boss.BellCenter+Vector3.right*6;
        Step(boss,world,boss.ActionRemaining+.01f);
        boss.HunterHealthLocked=false;
        return boss;
    }
    public static string[] Run() {
        var log=new List<string>();
        var tuning=new HuntBoss {GrandAttacks=true,MoveBounds=Rect.MinMaxRect(-48,-30,48,30),HunterSprintSpeed=8};
        Check(tuning.SweepTellTime>=48f/8+1,"large hall sweep includes sprint travel and observation time",log);
        tuning.SetDemonDamage(0);Check(tuning.Phase==1,"full health starts the teaching phase",log);
        tuning.SetDemonDamage(HuntBoss.DemonHealth*.31f);Check(tuning.Phase==2,"seventy percent health introduces sequences",log);
        tuning.SetDemonDamage(HuntBoss.DemonHealth*.66f);Check(tuning.Phase==3,"thirty-five percent health introduces the final set",log);

        foreach(bool bodyInside in new[]{false,true}) {
            var world=World();var boss=new HuntBoss {GrandAttacks=true};
            Until(boss,world,HuntBoss.Move.SleeveTell);
            var area=boss.SweepBounds;
            world.Player=new Vector3(area.center.x,0,area.center.y);
            if(!bodyInside)world.Player.x=boss.MoveBounds.center.x+(boss.SweepRight?-1:1);
            world.Hunter.Clear();if(!bodyInside)world.Hunter.Oval(area.center,4,4);
            Step(boss,world,boss.ActionRemaining+.01f);
            int health=boss.HunterHealth;
            Check(health==(bodyInside?68:100),bodyInside ? "sleeve hits a body in its displayed half" : "sleeve ignores a shadow in its displayed half",log);
            Step(boss,world,.1f);
            Check(boss.HunterHealth==health,"sleeve releases damage once",log);
        }
        {
            var world=World();var boss=new HuntBoss {GrandAttacks=true,HunterHealthLocked=true};boss.SetDemonDamage(120);
            Until(boss,world,HuntBoss.Move.SleeveTell);bool first=boss.SweepRight;
            float remaining=boss.ActionRemaining;Step(boss,world,0);
            Check(boss.ActionRemaining==remaining,"paused sweep does not advance",log);
            Step(boss,world,boss.ActionRemaining+.01f);Step(boss,world,.56f);
            Check(boss.Action==HuntBoss.Move.SleeveTell && boss.SweepRight!=first && boss.ActionRemaining>=3.25f,
                "second sleeve changes side with a fresh full warning",log);
            Step(boss,world,boss.ActionRemaining+.01f);Step(boss,world,.56f);
            Check(boss.Action==HuntBoss.Move.Recover && boss.ActionRemaining>=3.49f,"completed sleeve set grants a stable counter window",log);
        }
        {
            var world=World();var boss=Bell(world,1);
            world.Hunter.Clear();world.Hunter.Oval(new Vector2(boss.BellCenter.x,boss.BellCenter.z),20,20);
            Step(boss,world,.2f);
            Check(boss.HunterHealth==100 && Mathf.Abs(boss.GetBellRadius(0)-1.6f)<.01f,
                "wave advances physically and does not hit a distant body early",log);
            Step(boss,world,.6f);
            Check(boss.HunterHealth==78,"a moving wave sweeps across the body without tunnelling",log);
            Step(boss,world,.02f);Check(boss.HunterHealth==78,"one wave cannot damage twice",log);
        }
        {
            var world=World();var boss=Bell(world,1);
            Step(boss,world,.6f);world.Jump();world.StepMovement(.2f);Step(boss,world,.2f);
            Check(boss.HunterHealth==100,"jump clears the wave at the actual crossing time",log);
            world.StepMovement(.6f);Step(boss,world,.6f);
            Check(boss.HunterHealth==100,"landing behind a passed wave does not cause delayed damage",log);
        }
        foreach(int phase in new[]{1,2,3}) {
            var world=World();var boss=Bell(world,phase);
            Check(boss.BellWaveCount==phase,"phase "+phase+" releases the expected wave count",log);
            for(int i=0;i<600 && boss.Action==HuntBoss.Move.BellWaves;i++)Step(boss,world,.02f);
            Check(boss.HunterHealth==100-22*phase,"phase "+phase+" waves each hit once at separate crossings",log);
        }
        {
            var world=World();var boss=Bell(world,3);boss.SetLitLamps(0);
            Check(boss.Action==HuntBoss.Move.Pursue && boss.BellWaveCount==0,"blackout cancels all pending waves and starts exclusive dark hunt",log);
            boss.SetLitLamps(1);
            Check(boss.Action==HuntBoss.Move.LightStagger && boss.ActionRemaining==HuntBoss.GrandCounterWindow,
                "relighting any lamp cancels dark hunt for the full counter window",log);
            int health=boss.HunterHealth;Step(boss,world,1);
            Check(boss.HunterHealth==health,"cancelled waves cannot deal delayed damage",log);
        }
        {
            var world=World();var boss=new HuntBoss {GrandAttacks=true};
            Until(boss,world,HuntBoss.Move.SleeveTell);
            boss.StepWithLights(.1f,world,new[]{world},world,2);
            Check(boss.Action==HuntBoss.Move.Recover && boss.ActionRemaining==HuntBoss.GrandCounterWindow,"stun cancels a sleeve before release",log);
            var ritual=new HuntBoss {GrandAttacks=true};Check(ritual.BeginRitual() && ritual.ActionRemaining>=3.8f,"lamp ritual allows reload, turn and aim",log);
            ritual.BreakRitual();Check(ritual.Action==HuntBoss.Move.Recover && ritual.ActionRemaining==HuntBoss.GrandCounterWindow,"guarding a lamp grants the full counter window",log);
            var shared=new HuntBoss {GrandAttacks=true};shared.SetLitLamps(0);shared.HurtUnscaled(10);
            Check(shared.HunterHealth==90,"minor enemies share health without the final boss light multiplier",log);
            shared.StepRecovery(5.25f);Check(shared.HunterHealth==92,"shared enemy damage resets breathing recovery",log);
        }
        return log.ToArray();
    }
}
