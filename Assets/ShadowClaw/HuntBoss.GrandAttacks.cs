using UnityEngine;
using System.Collections.Generic;

public sealed partial class HuntBoss {
    // The original single-caster encounter is the shipped design. The later
    // grand-attack experiment remains explicit opt-in for isolated comparisons.
    public bool GrandAttacks=false;
    public const float GrandCounterWindow=3.5f;
    public float CurrentCounterWindow => GrandAttacks ? GrandCounterWindow : CounterWindow;
    public float HunterSprintSpeed=8f;
    public const float BellWaveSpeed=8f, BellWaveHalfWidth=.24f, BellWaveInterval=1.25f;
    float WaveSpeed => GrandAttacks ? BellWaveSpeed : 11f;
    float WaveInterval => GrandAttacks ? BellWaveInterval : 1f;
    public const float DarkPursuitDuration=1.75f, DarkLockDuration=.75f;
    public float CurrentDarkPursuitDuration => GrandAttacks ? DarkPursuitDuration : HalfHealthAssault ? .92f : 1.15f;
    public float CurrentDarkLockDuration => GrandAttacks ? DarkLockDuration : .45f;
    public bool DarkHuntLocked => Action==Move.Pounce || Action==Move.Pursue && timer<=CurrentDarkLockDuration;
    public int Phase {get;private set;}=1;
    public bool WantsRitual => !Enraged && Action==Move.Stalk && (GrandAttacks ? ritualRequested : !DoubleBellDue);
    bool HalfHealthAssault;
    int attacksBeforeDoubleBell;
    bool DoubleBellDue => HalfHealthAssault && attacksBeforeDoubleBell==0;
    public bool SweepRight {get;private set;}
    public Rect SweepBounds => Rect.MinMaxRect(SweepRight ? MoveBounds.center.x : MoveBounds.xMin,
        MoveBounds.yMin,SweepRight ? MoveBounds.xMax : MoveBounds.center.x,MoveBounds.yMax);
    public float SweepTellTime => Mathf.Max(3.25f,(MoveBounds.width*.5f+.6f)/Mathf.Max(1,HunterSprintSpeed)+1.1f);
    public Vector3 BellCenter {get;private set;}
    public int BellWaveCount {get;private set;}
    public float BellMaxRadius {get;private set;}
    public float BellElapsed {get;private set;}
    readonly bool[] bellHits=new bool[3];
    int grandTurn,sleevesRemaining;
    bool ritualRequested,ritualAfterSweep;
    Vector3 previousBellHunter;
    float previousBellJump;

    public void SetDemonDamage(float damage) {
        HalfHealthAssault=!GrandAttacks && damage>=DemonHealth*.5f;
        Phase=damage>=DemonHealth*.65f ? 3 : damage>=DemonHealth*.30f ? 2 : 1;
    }
    void BeginBell(Vector3 center,float radius,int count,float warning) {
        BellCenter=center;
        BellMaxRadius=radius;
        BellWaveCount=count;
        BellElapsed=0;
        System.Array.Clear(bellHits,0,bellHits.Length);
        Enter(Move.BellTell,warning);
    }
    public float GetBellRadius(int index) {
        if(Action!=Move.BellWaves || index<0 || index>=BellWaveCount)return -1;
        float age=BellElapsed-index*WaveInterval;
        return age<0 || age*WaveSpeed>BellMaxRadius ? -1 : age*WaveSpeed;
    }
    void CancelGrandAttack() {
        ritualRequested=ritualAfterSweep=false;
        BellWaveCount=0;BellElapsed=0;sleevesRemaining=0;
    }
    bool BeginNextGrandAction(ClawState world,IReadOnlyList<ClawState> shadows,ClawState floor) {
        if(Enraged)return false;
        if(ritualAfterSweep) {
            ritualAfterSweep=false;ritualRequested=true;Enter(Move.Stalk,1);
            return true;
        }
        int choice=grandTurn++%6;
        if(choice==0 || choice==3) {
            SweepRight=(grandTurn/3)%2==0;
            sleevesRemaining=Phase>=2 ? 2 : 1;
            ritualAfterSweep=Phase>=2 && choice==3;
            Enter(Move.SleeveTell,SweepTellTime);
        } else if(choice==1 || choice==4) {
            float radius=0;
            foreach(var corner in new[]{new Vector2(MoveBounds.xMin,MoveBounds.yMin),
                new Vector2(MoveBounds.xMax,MoveBounds.yMin),new Vector2(MoveBounds.xMin,MoveBounds.yMax),
                new Vector2(MoveBounds.xMax,MoveBounds.yMax)})
                radius=Mathf.Max(radius,Vector2.Distance(corner,new Vector2(world.Enemy.x,world.Enemy.z)));
            BeginBell(world.Enemy,radius+.8f,Phase,3.25f);
        } else if(choice==2) {
            ritualRequested=true;Enter(Move.Stalk,1);
        } else {
            // A short familiar attack separates the long sets; no persistent floor
            // hazard is placed across the next half-arena escape route.
            if(Vector3.Distance(world.Player,world.Enemy)<=ClawState.MaxReach && Cast(shadows))
                Enter(Move.Claw,ClawState.EndTime);
            else StartLunge(world,world.Player-world.Enemy);
        }
        return true;
    }
    bool StepGrandAttack(float dt,ClawState world) {
        if(Action==Move.SleeveTell) {
            if(timer<=0) {
                Rect bounds=SweepBounds;
                Vector3 center=new Vector3(bounds.center.x,world.Enemy.y,bounds.center.y);
                StrikeReleased?.Invoke(Move.SleeveSweep,center,center+Vector3.right*(SweepRight?1:-1));
                if(bounds.Contains(new Vector2(world.Player.x,world.Player.z)))Hurt(32);
                Enter(Move.SleeveSweep,.55f);
            }
            return true;
        }
        if(Action==Move.SleeveSweep) {
            if(timer<=0) {
                if(--sleevesRemaining>0) {SweepRight=!SweepRight;Enter(Move.SleeveTell,SweepTellTime);}
                else Enter(Move.Recover,CurrentCounterWindow);
            }
            return true;
        }
        if(Action==Move.BellTell) {
            if(timer<=0) {
                BellElapsed=0;previousBellHunter=world.Player;previousBellJump=world.JumpHeight;
                StrikeReleased?.Invoke(Move.BellWaves,BellCenter,BellCenter);
                Enter(Move.BellWaves,BellMaxRadius/WaveSpeed+(BellWaveCount-1)*WaveInterval+.1f);
            }
            return true;
        }
        if(Action!=Move.BellWaves)return false;
        float previousTime=BellElapsed;
        BellElapsed+=dt;
        float oldDistance=Vector2.Distance(new Vector2(previousBellHunter.x,previousBellHunter.z),new Vector2(BellCenter.x,BellCenter.z));
        float distance=Vector2.Distance(new Vector2(world.Player.x,world.Player.z),new Vector2(BellCenter.x,BellCenter.z));
        for(int i=0;i<BellWaveCount;i++) {
            float start=i*WaveInterval;
            if(BellElapsed<start || bellHits[i])continue;
            if(previousTime<start && i>0)StrikeReleased?.Invoke(Move.BellWaves,BellCenter,BellCenter);
            float oldRadius=Mathf.Max(0,previousTime-start)*WaveSpeed;
            float radius=Mathf.Min(BellMaxRadius,(BellElapsed-start)*WaveSpeed);
            float before=oldDistance-oldRadius,after=distance-radius;
            float width=BellWaveHalfWidth+.25f;
            bool crossed=before*after<=0 || Mathf.Min(Mathf.Abs(before),Mathf.Abs(after))<=width;
            if(!crossed || oldRadius>BellMaxRadius)continue;
            // Once crossed, a jumping body has successfully evaded this wave;
            // it cannot be struck on landing by the same receding ring.
            bellHits[i]=true;
            float crossing=Mathf.Abs(before-after)>.00001f ? Mathf.Clamp01(before/(before-after)) : 1;
            float jump=Mathf.Lerp(previousBellJump,world.JumpHeight,crossing);
            if(jump<=ClawState.GroundAttackHeight)Hurt(22);
        }
        previousBellHunter=world.Player;previousBellJump=world.JumpHeight;
        if(timer<=0)Enter(Move.Recover,GrandAttacks ? CurrentCounterWindow : .75f);
        return true;
    }
}
