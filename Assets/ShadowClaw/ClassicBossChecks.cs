using System;
using System.Collections.Generic;
using UnityEngine;

public static class ClassicBossChecks {
    static void Check(bool condition,string label,List<string> rows) {
        if(!condition)throw new InvalidOperationException("FAIL classic boss: "+label);
        rows.Add("PASS classic boss: "+label);
    }
    static int CountCasts(float damage) {
        var boss=new HuntBoss {HunterHealthLocked=true};
        var world=new ClawState(new WallInk(true),new WallInk(true),new WallInk(true),new WallInk(true),true);
        var shadows=new[]{world};
        boss.SetDemonDamage(damage);
        int casts=0;
        for(int i=0;i<3000;i++) {
            var last=boss.Action;
            world.Player=world.Enemy+Vector3.right*1.5f;
            world.Step(.04f);boss.StepWithLights(.04f,world,shadows,world,0);
            if(last==HuntBoss.Move.Stalk && boss.Action!=last)casts++;
        }
        return casts;
    }
    public static string[] Run() {
        var rows=new List<string>();
        var boss=new HuntBoss();
        Check(!boss.GrandAttacks && HuntBoss.DemonHealth==120,
            "the shipped encounter uses the original attack set and 120 health",rows);
        Check(boss.BeginRitual() && Mathf.Abs(boss.ActionRemaining-1.15f)<.0001f,
            "the normal lamp ritual has its original timing",rows);
        boss.BreakRitual();
        Check(boss.Action==HuntBoss.Move.Recover && Mathf.Abs(boss.ActionRemaining-.85f)<.0001f,
            "guarding a lamp returns the original brief recovery",rows);
        boss=new HuntBoss();boss.SetLitLamps(2);
        Check(boss.BeginRitual() && Mathf.Abs(boss.ActionRemaining-1.45f)<.0001f,
            "the two-lamp pressure ritual retains its original timing",rows);
        boss.SetLitLamps(0);
        Check(boss.Action==HuntBoss.Move.Pursue && Mathf.Abs(boss.ActionRemaining-1.15f)<.0001f,
            "blackout cancels the ritual into the original dark pursuit",rows);
        boss.SetLitLamps(1);
        Check(boss.Action==HuntBoss.Move.LightStagger && Mathf.Abs(boss.ActionRemaining-2f)<.0001f,
            "relighting grants the original two-second counter window",rows);
        boss.HurtUnscaled(20);boss.StepRecovery(5.25f);
        Check(boss.HunterHealth==82,"the boss rollback preserves breathing recovery",rows);

        var world=new ClawState(new WallInk(true),new WallInk(true),new WallInk(true),new WallInk(true),true);
        boss=new HuntBoss {HunterHealthLocked=true};
        var actions=new HashSet<HuntBoss.Move>();bool experimental=false;
        for(int i=0;i<4000;i++) {
            // Keep a reachable player near the enemy across a complete cycle.
            world.Player=world.Enemy+Vector3.right*1.5f;
            world.Step(.02f);boss.StepWithLights(.02f,world,new[]{world},world,0);
            actions.Add(boss.Action);
            experimental|=boss.Action==HuntBoss.Move.SleeveTell || boss.Action==HuntBoss.Move.SleeveSweep ||
                boss.Action==HuntBoss.Move.BellTell || boss.Action==HuntBoss.Move.BellWaves;
        }
        Check(!experimental && actions.Contains(HuntBoss.Move.Claw) && actions.Contains(HuntBoss.Move.MarkFloor) &&
            actions.Contains(HuntBoss.Move.LungeTell) && actions.Contains(HuntBoss.Move.Rift) && actions.Contains(HuntBoss.Move.Burst),
            "a full restored encounter cycles through original spells without grand attacks",rows);
        boss=new HuntBoss();
        boss.SetDemonDamage(59.99f);
        Check(boss.WantsRitual,"just above half health the normal lamp decision remains available",rows);
        boss.SetDemonDamage(60f);
        Check(!boss.WantsRitual && !boss.BeginRitual(),"at exactly half health the next attack has priority over a new ritual",rows);
        boss.StepWithLights(1.01f,world,new[]{world},world,0);
        Check(boss.Action==HuntBoss.Move.BellTell && boss.BellWaveCount==2 && boss.ActionRemaining>=1.19f,
            "half health opens with two warned waves on the original boss",rows);
        int rings=0;
        boss.StrikeReleased+=(move,a,b)=>{if(move==HuntBoss.Move.BellWaves)rings++;};
        world.Player=boss.BellCenter+Vector3.right*6;
        for(int i=0;i<260 && boss.Action!=HuntBoss.Move.Recover;i++)boss.StepWithLights(.02f,world,new[]{world},world,0);
        Check(rings==2 && boss.HunterHealth==56,"both gold rings release and damage a grounded body exactly once each",rows);
        boss.StepWithLights(boss.ActionRemaining+.01f,world,new[]{world},world,0);
        Check(boss.Action==HuntBoss.Move.Stalk && Mathf.Abs(boss.ActionRemaining-.14f)<.001f,"half-health stalk duration falls to 35 percent",rows);
        boss.SetLitLamps(0);
        Check(boss.Action==HuntBoss.Move.Pursue && boss.ActionRemaining<1.15f && boss.BellWaveCount==0,
            "blackout cancels the rings and accelerates the dark pursuit",rows);
        boss.SetLitLamps(1);
        Check(boss.ActionRemaining==HuntBoss.CounterWindow,"relighting retains its full counter window",rows);
        int first=CountCasts(0),second=CountCasts(60);
        Check(second>first,"two minutes at the same range: "+first+" full-health casts, "+second+" half-health casts (a double bell counts as one)",rows);
        return rows.ToArray();
    }
}
