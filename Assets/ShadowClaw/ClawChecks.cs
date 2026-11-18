using System;
using System.Collections.Generic;
using UnityEngine;

public static class ClawChecks
{
    static void Check(bool condition,string name,List<string> rows) {
        if(!condition) throw new Exception("FAIL "+name);
        rows.Add("PASS "+name);
    }
    public static ClawState NewState() {
        return new ClawState(new WallInk(),new WallInk(),new WallInk(),new WallInk());
    }
    static ClawState GroundState() => new ClawState(new WallInk(true),new WallInk(true),new WallInk(true),new WallInk(true),true);
    static void SetBossValue(HuntBoss boss,string property,object value) {
        typeof(HuntBoss).GetProperty(property).SetValue(boss,value);
    }
    static void Attack(HuntBoss boss,HuntBoss.Move move,Vector3 destination,float duration=.01f) {
        SetBossValue(boss,"Destination",destination);
        typeof(HuntBoss).GetMethod("Enter",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
            .Invoke(boss,new object[] {move,duration});
    }
    static void CheckBodyHits(List<string> log) {
        foreach(var move in new[] {HuntBoss.Move.Rift,HuntBoss.Move.Burst,HuntBoss.Move.Lunge,HuntBoss.Move.Pounce}) {
            foreach(bool bodyHit in new[] {false,true}) {
                var world=GroundState();
                world.Enemy=Vector3.zero;
                float radius=move==HuntBoss.Move.Burst ? 4.3f : move==HuntBoss.Move.Rift ? 1.16f : 1.56f;
                world.Player=Vector3.forward*(radius+(bodyHit ? -.01f : .01f));
                // Deliberately invert the body's and shadow's positions: the old fallback hits the safe body.
                world.Hunter.Clear();
                if(!bodyHit)world.Hunter.Oval(Vector2.zero,2,2);
                var boss=new HuntBoss();
                Attack(boss,move,move==HuntBoss.Move.Burst ? Vector3.zero : Vector3.right*4);
                boss.StepWithLights(.02f,world,new[] {world},world,0);
                int damage=move==HuntBoss.Move.Rift ? 25 : move==HuntBoss.Move.Burst ? 30 : move==HuntBoss.Move.Lunge ? 34 : 28;
                Check(boss.HunterHealth==100-(bodyHit ? damage : 0),
                    move+(bodyHit ? " hits the body without a hunter shadow" : " ignores a shadow inside the attack"),log);
            }
        }
        foreach(var move in new[] {HuntBoss.Move.Lunge,HuntBoss.Move.Pounce}) {
            var world=GroundState();world.Enemy=Vector3.zero;world.Player=Vector3.right*1.8f;
            var boss=new HuntBoss();Attack(boss,move,Vector3.right*4,1);
            boss.StepWithLights(.2f,world,new[] {world},world,0,(desired,dt)=>Vector3.zero);
            Check(boss.HunterHealth==100,"collision-blocked "+move+" cannot hit beyond its actual travel",log);
            boss.StepWithLights(.02f,world,new[] {world},world,0,(desired,dt)=>Vector3.right*.28f);
            int after=boss.HunterHealth;
            Check(after==100-(move==HuntBoss.Move.Lunge ? 34 : 28),move+" reaches the body at the enlarged strike boundary",log);
            boss.StepWithLights(.02f,world,new[] {world},world,0,(desired,dt)=>Vector3.right*.28f);
            Check(boss.HunterHealth==after,move+" does not damage twice during one contact",log);
        }
        foreach(bool bodyHit in new[] {false,true}) {
            var world=GroundState();world.Player=Vector3.right*(bodyHit ? .75f : .77f);
            world.Hunter.Clear();if(!bodyHit)world.Hunter.Oval(Vector2.zero,1,1);
            var boss=new HuntBoss();
            Attack(boss,HuntBoss.Move.Stalk,Vector3.zero,10);
            SetBossValue(boss,"Scar",Vector2.zero);SetBossValue(boss,"ScarLife",1f);
            boss.StepWithLights(.02f,world,new[] {world},world,0);
            Check(boss.HunterHealth==(bodyHit ? 88 : 100) && (boss.Slow>0)==bodyHit,
                bodyHit ? "claw scar damages and slows only the body" : "shadow crossing a scar causes no damage or slow",log);
        }
        var claw=GroundState();claw.Player=Vector3.zero;claw.Enemy=Vector3.forward*2;
        Check(claw.StartCast() && claw.LockPoint==Vector2.zero,"claw telegraph locks the hunter's feet",log);
        claw.Player=new Vector3(1,0,1);
        float source=4.3f/1.2f;
        claw.SetLamp(claw.LightId,new Vector3(source,4.3f,source));
        claw.Step(ClawState.CloseTime+.01f);
        Check(claw.Hunter.Contains(claw.LockPoint) && claw.Hits==0 && claw.Misses==1 && claw.Pin==0,
            "claw misses a moved body even when its shadow still covers the locked point",log);
        claw=GroundState();claw.Player=Vector3.zero;claw.Enemy=Vector3.forward*2;
        claw.StartCast();claw.Step(ClawState.CloseTime-.2f);claw.Jump();claw.Step(.21f);
        Check(claw.Hits==0 && claw.Misses==1 && claw.JumpHeight>ClawState.GroundAttackHeight,
            "jumping above the closing ground claw avoids pin and damage",log);
        foreach(bool scar in new[] {false,true}) {
            var world=GroundState();world.Player=Vector3.zero;world.Enemy=Vector3.right;
            world.Jump();world.StepMovement(.2f);
            world.Hunter.Clear();world.Hunter.Oval(Vector2.zero,3,3);
            var boss=new HuntBoss();
            Attack(boss,scar ? HuntBoss.Move.Stalk : HuntBoss.Move.Burst,Vector3.zero,scar ? 10 : .01f);
            if(scar) {SetBossValue(boss,"Scar",Vector2.zero);SetBossValue(boss,"ScarLife",1f);}
            boss.StepWithLights(.02f,world,new[] {world},world,0);
            Check(boss.HunterHealth==100 && boss.Slow==0,
                "airborne body avoids "+(scar ? "ground scar" : "burst")+" even with shadow inside",log);
        }
    }
    static void CheckRecovery(List<string> log) {
        var boss=new HuntBoss();boss.Hurt(40);
        boss.StepRecovery(HuntBoss.RecoveryDelay-.1f);
        Check(boss.HunterHealth==60 && !boss.Recovering,"recovery waits for the full undamaged delay",log);
        boss.StepRecovery(.6f);
        Check(boss.HunterHealth==64 && boss.Recovering,"recovery heals only the part of a frame beyond the delay",log);
        boss.Hurt(4);boss.StepRecovery(4);
        Check(boss.HunterHealth==60 && Mathf.Approximately(boss.RecoveryRemaining,1),"new damage restarts the recovery delay",log);
        boss.StepRecovery(0);boss.StepRecovery(-1);
        Check(boss.HunterHealth==60 && Mathf.Approximately(boss.RecoveryRemaining,1),"paused or invalid time cannot advance recovery",log);
        boss.HunterHealthLocked=true;boss.Hurt(20);boss.StepRecovery(20);
        Check(boss.HunterHealth==60 && !boss.Recovering,"health lock prevents both damage and regeneration",log);
        boss.HunterHealthLocked=false;boss.StepRecovery(100);
        Check(boss.HunterHealth==100 && !boss.Recovering,"recovery stops at maximum health",log);
        boss.Hurt(100);boss.StepRecovery(100);
        Check(boss.HunterHealth==0 && !boss.Recovering,"recovery never revives a defeated hunter",log);
        var coarse=new HuntBoss();var fine=new HuntBoss();coarse.Hurt(40);fine.Hurt(40);
        coarse.StepRecovery(7);
        for(int i=0;i<700;i++)fine.StepRecovery(.01f);
        Check(coarse.HunterHealth==76 && fine.HunterHealth==coarse.HunterHealth,"recovery amount is stable across frame rates",log);
    }
    public static string[] Run()
    {
        var log=new List<string>(); var s=NewState();
        Check(s.StartCast(),"claw can lock the hunter's body",log);
        s.Step(ClawState.CloseTime-.06f);
        Check(s.Hits==0 && s.Misses==0,"no early hit on the extending arm",log);
        s.Step(.07f);
        Check(s.Hits==1 && s.Pin>0 && s.Arm.Contains(s.LockPoint),"stationary: claw reaches mark and pins exactly on close",log);
        float x=s.Player.x; s.MovePlayer(.5f);
        Check(s.Player.x==x,"pin blocks movement",log);
        s.Step(.14f);
        Check(s.Hits==1 && s.EnemyAt(s.Palm),"shootable arm in recovery, no double damage",log);
        s.Reset(); s.StartCast(); Vector2 locked=s.LockPoint;
        s.Step(.45f); s.MovePlayer(1.04f); s.Step(.62f);
        Check(s.LockPoint==locked && s.Misses==1 && s.Pin==0 && !s.Hunter.Contains(locked),"sidestep: cast misses the fixed mark",log);
        s.Reset(); s.StartCast(); s.Step(.52f); s.SelectLight(1); s.Step(.75f);
        Check(!s.Active && s.Cancels==1 && s.Hits==0 && s.Misses==0,"replace the light: old cast cannot resolve",log);
        Check(s.StartCast(),"new light permits a fresh cast",log);
        s.Reset(); s.StartCast(); locked=s.LockPoint; Vector2 shoulder=s.Shoulder;
        s.Enemy.x+=.38f; s.Step(.54f);
        Check(s.Shoulder.x>shoulder.x+.35f && s.LockPoint==locked,"moving caster moves shoulder but not the mark",log);
        s.Reset(); s.Player=s.Enemy+Vector3.right*11.39f;
        Check(s.StartCast(),"the claw can acquire a body 11.39 metres away",log);
        s.Interrupt();s.Player=s.Enemy+Vector3.right*11.41f;
        Check(!s.StartCast(),"out-of-reach attack is rejected",log);
        s.Reset();
        var combat=new WallCombat();
        Vector2 center=s.Project(s.Enemy+Vector3.up*1.2f);
        Check(s.EnemyAt(center),"test point is in the enemy silhouette",log);
        combat.Shoot(Bolt.Holy,center,s.EnemyAt(center));
        combat.Shoot(Bolt.Holy,locked,false);
        Check(combat.Damage==10,"holy damage only on hit",log);
        combat.Shoot(Bolt.Fire,center,false);
        combat.Step(.5f,s,null);
        s.Enemy.x+=1.5f; s.Draw(); combat.Step(.5f,s,null);
        Check(Mathf.Approximately(combat.Damage,12),"fire area damages only while shadow overlaps",log);
        s.Reset(); combat=new WallCombat();
        combat.Shoot(Bolt.Fire,center,true);
        s.Enemy.x+=1.5f; s.Draw(); combat.Step(4,s,null);
        Check(Mathf.Approximately(combat.Damage,12),"direct fire follows moving demon for three seconds",log);
        s.Reset(); combat=new WallCombat(); s.StartCast();
        for(int i=0;i<2;i++) combat.Shoot(Bolt.Storm,center,true);
        combat.Step(0,s,null);
        Check(combat.Stun==0 && s.Active,"two storm bolts do not stun",log);
        combat.Shoot(Bolt.Storm,center,true); combat.Step(0,s,null);
        Check(combat.Stun>0 && !s.Active && combat.Marks.Count==0 && combat.Damage==0,"three storm bolts interrupt and are consumed",log);
        combat.Step(2.1f,s,null);
        Check(combat.Stun==0,"stun wears off",log);
        s.Reset(); s.StartCast(); s.SetLamp(2,new Vector3(0,1,ClawState.WallZ));
        Check(!s.Active && s.Cancels==1 && !s.EnemyAt(center),"receiver light extinguishes old silhouette and cast",log);
        s.Reset();
        var second=NewState(); second.SetLamp(2,new Vector3(1,.2f,-3));
        var alternate=second.Project(second.Enemy+Vector3.up*1.2f);
        Check(second.EnemyAt(alternate) && !s.EnemyAt(alternate),"floor light creates a separately hittable shadow",log);
        var floor=new ClawState(new WallInk(true),new WallInk(true),new WallInk(true),new WallInk(true),true);
        Vector2 tile=floor.Project(floor.Enemy+Vector3.up*1.2f);
        combat=new WallCombat();
        combat.Shoot(Bolt.Fire,tile,false,true);
        combat.Step(.5f,s,null,floor);
        Check(combat.Damage==2,"floor fire recognizes the floor silhouette",log);
        combat.Shoot(Bolt.Storm,tile,true,true); combat.Shoot(Bolt.Storm,tile,true,true);
        combat.Shoot(Bolt.Storm,tile,false);
        combat.Step(0,s,null,floor);
        Check(combat.Stun==0,"wall and floor anchors do not combine",log);
        combat.Shoot(Bolt.Storm,tile,true,true);
        combat.Step(0,s,null,floor);
        Check(combat.Stun>0,"three ground anchors paralyze",log);
        s.Reset();
        var ai=new HuntBoss {GrandAttacks=false}; bool claw=false,slow=false,moved=false;
        Vector3 spawn=s.Enemy;
        for(int frame=0;frame<1500 && !(claw && slow && moved && ai.HunterHealth<100);frame++) {
            floor.Enemy=s.Enemy; floor.Player=s.Player;
            floor.Step(.02f); s.Step(.02f);
            ai.Step(.02f,s,null,floor,0);
            claw|=ai.Action==HuntBoss.Move.Claw;
            slow|=ai.Slow>0;
            moved|=(s.Enemy-spawn).sqrMagnitude>.05f;
        }
        Check(moved && claw && slow && ai.HunterHealth<100,"autonomous stalk="+moved+" claw="+claw+" slow="+slow+" hp="+ai.HunterHealth,log);
        foreach(var attack in new[] { HuntBoss.Move.Rift,HuntBoss.Move.Burst }) {
            foreach(bool dodge in new[] { false,true }) {
                var world=NewState();
                var hunter=new HuntBoss {GrandAttacks=false};
                bool warned=false, resolved=false;
                int before=0;
                for(int i=0;i<1600 && !resolved;i++) {
                    // Hold close for the pulse; back off for the ranged line.
                    if(!warned) world.Player=world.Enemy+Vector3.right*(attack==HuntBoss.Move.Burst ? 1.2f : 6);
                    if(!warned && (hunter.Action==HuntBoss.Move.Lunge || hunter.Action==HuntBoss.Move.Rift))
                        world.Player=world.Enemy+Vector3.forward*3;
                    else if(warned && dodge) {
                        Vector3 across=attack==HuntBoss.Move.Burst ? Vector3.forward :
                            Vector3.Cross(Vector3.up,(hunter.Destination-world.Enemy).normalized);
                        world.Player=world.Enemy+across*(attack==HuntBoss.Move.Burst ? HuntBoss.BurstRadius+.2f : HuntBoss.RiftWidth+.2f);
                    }
                    world.Step(.02f);
                    hunter.StepWithLights(.02f,world,new[] {world},null,0);
                    if(hunter.Action==attack && !warned) { warned=true; before=hunter.HunterHealth; }
                    else if(warned && hunter.Action!=attack) resolved=true;
                }
                int expected=dodge ? 0 : attack==HuntBoss.Move.Burst ? 30 : 25;
                Check(resolved && before-hunter.HunterHealth==expected,attack+" warning and "+(dodge ? "evasion" : "single impact"),log);
            }
        }
        combat=new WallCombat(); combat.Shoot(Bolt.Fire,Vector2.zero,true,true);
        combat.Step(1,new ClawState[0]);
        Check(combat.Damage==0,"ongoing burn cannot bypass darkness immunity",log);
        var power=new HuntBoss();
        power.Hurt(16); power.SetLitLamps(1); power.Hurt(16); power.SetLitLamps(0); power.Hurt(16);
        Check(power.Enraged && power.HunterHealth==37,"light level increases attack damage and enters rage",log);
        var bow=new HunterCrossbow();
        for(int i=0;i<HunterCrossbow.Capacity;i++) {
            Check(bow.Fire() && !bow.Fire(),"shot consumes one round and cycles",log); bow.Step(.33f);
        }
        Check(!bow.Fire() && bow.Phase==HunterCrossbow.Motion.Lower,"empty trigger starts reload",log);
        bow.Step(.20f); Check(!bow.Fire() && bow.Rounds==0,"no shot during the load",log);
        bow.Step(.80f); Check(bow.Rounds==4 && !bow.Fire(),"raising a loaded bow still locks fire",log);
        bow.Step(.30f); bow.Request(Bolt.Storm); bow.Step(.5f);
        Check(!bow.Fire() && bow.Loaded==Bolt.Holy,"switch wind-up retains the current arrow",log);
        bow.Step(1); Check(bow.Loaded==Bolt.Storm && bow.Fire(),"new arrow fires only after recovery",log);
        s.Reset(); s.Jump(); s.Step(.20f);
        float height=s.JumpHeight; Vector2 airborne=s.At(s.PlayerBody,0,1.2f);
        s.Jump(); s.Step(.60f);
        Check(height>.5f && s.JumpHeight==0 && (airborne-s.At(s.PlayerBody,0,1.2f)).sqrMagnitude>.01f,
            "jump displaces shadow then lands without an air jump",log);
        foreach(int count in new[] {6,4,2,1}) {
            s.Reset(); var ritualBoss=new HuntBoss(); ritualBoss.SetLitLamps(count);
            ritualBoss.BeginRitual(); Vector3 origin=s.Enemy;
            ritualBoss.StepWithLights(.40f,s,new[] {s},null,0);
            Check((s.Enemy!=origin)==(count<=2),"ritual movement at "+count+" lights",log);
            ritualBoss.BreakRitual();
            Check(ritualBoss.Action==HuntBoss.Move.Recover,"protecting lamp stops ritual approach",log);
        }
        CheckBodyHits(log);
        CheckRecovery(log);
        return log.ToArray();
    }
}
