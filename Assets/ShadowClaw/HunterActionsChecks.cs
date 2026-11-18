using System;
using System.Collections.Generic;
using UnityEngine;

public static class HunterActionsChecks
{
    public static string[] Run() {
        var rows=new List<string>();
        void Check(bool value,string label) {if(!value)throw new InvalidOperationException(label);rows.Add("PASS hunter actions: "+label);}
        var pouch=new HolyKnifePouch();pouch.Reset();
        Check(pouch.Count==2,"new hunt starts with the requested two knives");
        for(int i=0;i<10;i++)pouch.Grant();
        Check(pouch.Count==5 && !pouch.Grant(),"lamp rewards cap at five");
        for(int i=0;i<5;i++)Check(pouch.Spend(),"throw consumes stocked knife "+i);
        Check(pouch.Count==0 && !pouch.Spend(),"empty pouch cannot become negative");
        pouch.Reset(99);Check(pouch.Count==5,"configured starting stock is capped");
        pouch.Reset(-1);Check(pouch.Count==0,"invalid negative starting stock is clamped");
        var knife=new HolyKnifeThrowMotion();
        Check(knife.Begin() && !knife.Begin(),"one accepted knife preparation rejects a duplicate animation start");
        Check(!knife.Advance(.10f) && !knife.Released && knife.Active,"drawing the left-side blade does not release or hit early");
        float knifeAge=knife.Age;
        Check(!knife.Advance(0) && !knife.Advance(float.NaN) && knife.Age==knifeAge,"paused and invalid time cannot advance or release the knife");
        Check(knife.Advance(.13f) && knife.Released && knife.Active,"exactly one release occurs across the forward-throw boundary");
        Check(!knife.Advance(.5f) && !knife.Active,"recovery completes without releasing a second blade");
        knife.Reset();knife.Begin();
        Check(knife.Advance(1) && !knife.Active && knife.Released,"a long frame still releases exactly once before completing recovery");
        Check(HolyKnifeThrowMotion.Position(HolyKnifeThrowMotion.ReleaseTime).x<-.2f,"the dagger leaves the camera's left side rather than the crossbow rail");
        var motion=new HunterMotionState();
        for(int i=0;i<100;i++)motion.Step(0,.02f,true,false);
        Check(motion.Footsteps==0 && motion.MovementBlend==0 && motion.PositionOffset.sqrMagnitude>0,"idle breath never triggers walking sounds");
        for(int i=0;i<20;i++)motion.Step(.1f,.02f,true,false);
        Check(motion.Footsteps==0,"steps follow travelled distance, not held movement time");
        motion.Step(.2f,.04f,true,false);Check(motion.Footsteps==1,"walking a full stride triggers one step");
        int before=motion.Footsteps;motion.Step(10,.1f,false,true);
        Check(motion.Footsteps==before,"airborne horizontal movement cannot trigger a step");
        var beforePhase=motion.BreathPhase;motion.Step(10,0,true,true);
        Check(motion.BreathPhase==beforePhase && motion.Footsteps==before,"zero simulation time freezes motion and sound cadence");
        motion.Reset();for(int i=0;i<30;i++)motion.Step(.16f,.02f,true,true);
        Check(motion.RunBlend>.99f && motion.MovementBlend>.99f && motion.Footsteps==1,"running uses its longer stride and stronger hand motion");
        for(int i=0;i<50;i++)motion.Step(0,.02f,true,false);
        Check(motion.MovementBlend==0 && motion.RunBlend==0,"stopping eases back to breathing");
        return rows.ToArray();
    }
}
