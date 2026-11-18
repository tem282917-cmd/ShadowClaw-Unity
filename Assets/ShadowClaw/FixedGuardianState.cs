using System;
using UnityEngine;

// The fixed guardian's clock is owned by ShadowLab, including telegraphs and wave travel.
[Serializable]
public sealed class FixedGuardianTuning {
    [Min(1)] public int Health=240;
    [Min(1)] public int SlamDamage=22, SweepDamage=20, WaveDamage=15;
    [Min(.1f)] public float SlamTrackTime=.55f, SlamLockTime=1.2f, SlamRadius=4.2f;
    [Min(.1f)] public float SweepTellTime=1.75f, WaveTellTime=1.2f, WaveSpeed=6.5f;
    [Min(.05f)] public float WaveHalfWidth=.3f;
    [Min(1f)] public float RitualTime=2f, CounterTime=3.5f;
}

public struct FixedGuardianFrame {
    public Vector3 Hunter, Origin, ArenaCenter, ArenaRight, ArenaForward;
    public Vector2 ArenaSize;
    public float JumpHeight;
    public int LitCount, LampCount, NextLitLamp;
    public bool InsideArena {
        get {
            Vector3 delta=Hunter-ArenaCenter;
            return Mathf.Abs(Vector3.Dot(delta,ArenaRight))<=ArenaSize.x*.5f &&
                   Mathf.Abs(Vector3.Dot(delta,ArenaForward))<=ArenaSize.y*.5f;
        }
    }
}

public sealed class FixedGuardianState {
    public enum Move { Dormant, Recover, SlamTrack, SlamLocked, SlamImpact, SweepTell, SweepImpact, WaveTell, WaveTravel, Ritual, Dead }
    public readonly FixedGuardianTuning Tuning;
    public Move Action {get;private set;}
    public int Health {get;private set;}
    public bool PhaseTwo => Health<=Tuning.Health*.5f;
    public bool Dark {get;private set;}
    public bool Alive => Health>0;
    public float Remaining {get;private set;}
    public float Progress => duration<=0 ? 0 : 1-Mathf.Clamp01(Remaining/duration);
    public Vector3 LockedPoint {get;private set;}
    public Vector3 WaveOrigin {get;private set;}
    public float WaveRadius {get;private set;}
    public float WaveRange {get;private set;}
    public int SweepSide {get;private set;}
    public int RitualLamp {get;private set;}=-1;
    public int DamageEvents {get;private set;}
    public int ReleasedWaves {get;private set;}
    public Action<int> DamageHunter, ExtinguishLamp;
    public string Tell => Action==Move.Dormant ? "锁灯巨像 · 静候猎人" :
        Action==Move.SlamTrack ? "巨臂落槌 · 落点正在跟随" :
        Action==Move.SlamLocked ? "落点已锁定 · 离开落槌圈" :
        Action==Move.SweepTell ? (SweepSide<0 ? "钟摆横扫 · 离开左半场" : "钟摆横扫 · 离开右半场") :
        Action==Move.WaveTell ? "胸灯鸣震 · 波环到达时跳跃" :
        Action==Move.WaveTravel ? "低位波环正在扩散 · 跳过波前" :
        Action==Move.Ritual ? "收灯祷告 · 射击黑线连接的灯" :
        Action==Move.Recover ? "守灯者失衡 · 瞄准地影反击" :
        Action==Move.Dead ? "守灯者已倒下" : "巨像出招";

    float duration;
    int turn, wavesLeft, sweepsLeft;
    bool waveHit, sweepThenRitual;
    Vector3 previousHunter;
    float previousJump;

    public FixedGuardianState(FixedGuardianTuning tuning) { Tuning=tuning; Reset(); }
    public void Reset() {
        Health=Mathf.Max(1,Tuning.Health);Action=Move.Dormant;Remaining=duration=0;
        Dark=false;RitualLamp=-1;WaveRadius=0;turn=wavesLeft=sweepsLeft=0;
        DamageEvents=ReleasedWaves=0;waveHit=sweepThenRitual=false;
    }
    void Enter(Move action,float time) {
        Action=action;Remaining=duration=Mathf.Max(.001f,time);
        if(action!=Move.Ritual) RitualLamp=-1;
    }
    public void Begin(FixedGuardianFrame frame) {
        if(!Alive || Action!=Move.Dormant)return;
        Dark=frame.LitCount==0;previousHunter=frame.Hunter;previousJump=frame.JumpHeight;
        Enter(Move.Recover,1.2f);
    }
    public void ReceiveDamage(int damage) {
        if(!Alive || Dark || Action==Move.Dormant || damage<=0)return;
        Health=Mathf.Max(0,Health-damage);
        if(!Alive){Enter(Move.Dead,1);WaveRadius=0;sweepThenRitual=false;}
    }
    public void LampShot(int lamp,bool belongsToGuardian) {
        if(!Alive || Action==Move.Dormant || !belongsToGuardian)return;
        if(Dark || (Action==Move.Ritual && RitualLamp==lamp)) {
            Dark=false;WaveRadius=0;sweepThenRitual=false;
            Enter(Move.Recover,Tuning.CounterTime);
        }
    }
    void Hit(int damage) { DamageEvents++;DamageHunter?.Invoke(damage); }
    void Slam(FixedGuardianFrame frame) {
        LockedPoint=frame.Hunter;
        Enter(Move.SlamTrack,Tuning.SlamTrackTime*.5f);
    }
    void Ritual(FixedGuardianFrame frame) {
        if(frame.NextLitLamp<0) {Slam(frame);return;}
        Enter(Move.Ritual,Tuning.RitualTime);RitualLamp=frame.NextLitLamp;
    }
    void Sweep(FixedGuardianFrame frame) {
        // Scale the former arena-width-adjusted windup at every arena size.
        Enter(Move.SweepTell,Mathf.Max(Tuning.SweepTellTime,frame.ArenaSize.x/16f+.25f)*.5f);
    }
    void Wave(FixedGuardianFrame frame) {
        WaveOrigin=frame.Origin;WaveRadius=0;waveHit=false;
        WaveRange=0;
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2) {
            Vector3 corner=frame.ArenaCenter+frame.ArenaRight*x*frame.ArenaSize.x*.5f+
                frame.ArenaForward*z*frame.ArenaSize.y*.5f;
            WaveRange=Mathf.Max(WaveRange,HorizontalDistance(corner,WaveOrigin)+1f);
        }
        Enter(Move.WaveTell,Tuning.WaveTellTime*.5f);
    }
    void Next(FixedGuardianFrame frame) {
        int choice=turn++%4;
        sweepThenRitual=false;
        if(Dark)choice=turn%2==0 ? 0 : 2;
        switch(choice) {
            case 0:Slam(frame);break;
            case 1:
                sweepsLeft=PhaseTwo?2:1;sweepThenRitual=PhaseTwo;
                SweepSide=Vector3.Dot(frame.Hunter-frame.ArenaCenter,frame.ArenaRight)<0 ? -1:1;
                Sweep(frame);break;
            case 2:wavesLeft=PhaseTwo?2:1;Wave(frame);break;
            default:Ritual(frame);break;
        }
    }
    public static float HorizontalDistance(Vector3 a,Vector3 b) {
        float x=a.x-b.x,z=a.z-b.z;return Mathf.Sqrt(x*x+z*z);
    }
    public void Tick(float dt,FixedGuardianFrame frame) {
        if(dt<=0 || !Alive || Action==Move.Dormant)return;
        bool nowDark=frame.LitCount==0;
        if(Dark && !nowDark) {
            Dark=false;WaveRadius=0;sweepThenRitual=false;
            Enter(Move.Recover,Tuning.CounterTime);
            previousHunter=frame.Hunter;previousJump=frame.JumpHeight;return;
        }
        if(!Dark && nowDark) {Dark=true;sweepThenRitual=false;Slam(frame);}
        Remaining-=dt;
        switch(Action) {
            case Move.Recover: if(Remaining<=0)Next(frame); break;
            case Move.SlamTrack:
                LockedPoint=frame.Hunter;
                if(Remaining<=0)Enter(Move.SlamLocked,Tuning.SlamLockTime*.5f);
                break;
            case Move.SlamLocked:
                if(Remaining<=0) {
                    if(HorizontalDistance(frame.Hunter,LockedPoint)<=Tuning.SlamRadius)
                        Hit(Tuning.SlamDamage);
                    Enter(Move.SlamImpact,.28f);
                }
                break;
            case Move.SlamImpact: if(Remaining<=0)Enter(Move.Recover,Tuning.CounterTime);break;
            case Move.SweepTell:
                if(Remaining<=0) {
                    float side=Vector3.Dot(frame.Hunter-frame.ArenaCenter,frame.ArenaRight)*SweepSide;
                    if(frame.InsideArena && side>=-.3f)Hit(Tuning.SweepDamage);
                    Enter(Move.SweepImpact,.35f);
                }
                break;
            case Move.SweepImpact:
                if(Remaining<=0) {
                    if(--sweepsLeft>0){SweepSide=-SweepSide;Sweep(frame);}
                    else if(sweepThenRitual){sweepThenRitual=false;Ritual(frame);}
                    else Enter(Move.Recover,Tuning.CounterTime);
                }
                break;
            case Move.WaveTell:
                if(Remaining<=0) {
                    ReleasedWaves++;waveHit=false;WaveRadius=0;
                    Enter(Move.WaveTravel,(WaveRange+Tuning.WaveHalfWidth)/Mathf.Max(.1f,Tuning.WaveSpeed));
                }
                break;
            case Move.WaveTravel:
                float oldRadius=WaveRadius;
                WaveRadius+=Tuning.WaveSpeed*dt;
                float before=HorizontalDistance(previousHunter,WaveOrigin)-oldRadius;
                float after=HorizontalDistance(frame.Hunter,WaveOrigin)-WaveRadius;
                float crossing=Mathf.Abs(before-after)<.0001f ? 1 : Mathf.Clamp01(before/(before-after));
                float jumpAtCrossing=Mathf.Lerp(previousJump,frame.JumpHeight,crossing);
                if(!waveHit && Mathf.Min(before,after)<=Tuning.WaveHalfWidth+.3f &&
                   Mathf.Max(before,after)>=-Tuning.WaveHalfWidth-.3f && jumpAtCrossing<=ClawState.GroundAttackHeight) {
                    waveHit=true;Hit(Tuning.WaveDamage);
                }
                if(Remaining<=0 || WaveRadius>WaveRange+Tuning.WaveHalfWidth) {
                    WaveRadius=0;
                    if(--wavesLeft>0)Wave(frame);
                    else Enter(Move.Recover,Tuning.CounterTime);
                }
                break;
            case Move.Ritual:
                if(Remaining<=0) {
                    int lamp=RitualLamp;
                    Enter(Move.Recover,Tuning.CounterTime);
                    ExtinguishLamp?.Invoke(lamp);
                }
                break;
        }
        previousHunter=frame.Hunter;previousJump=frame.JumpHeight;
    }
}
