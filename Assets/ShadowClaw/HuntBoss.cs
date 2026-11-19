using UnityEngine;
using System.Collections.Generic;

public sealed partial class HuntBoss
{
    public enum Move { Stalk, Claw, MarkFloor, LungeTell, Lunge, Rift, Burst, Ritual, Recover, Pursue, Pounce, LightStagger,
        SleeveTell, SleeveSweep, BellTell, BellWaves }
    public Move Action { get; private set; }
    public const int HunterMaxHealth=100;
    public const float RecoveryDelay=5f, RecoveryPerSecond=8f;
    public int HunterHealth { get; private set; } = HunterMaxHealth;
    internal event System.Action HunterDamaged;
    internal bool HunterHealthLocked {get;set;}
    double timeSinceDamage=RecoveryDelay, recoveryCarry;
    public float RecoveryRemaining => HunterHealth>0 && HunterHealth<HunterMaxHealth ? (float)System.Math.Max(0,RecoveryDelay-timeSinceDamage) : 0;
    public bool Recovering => !HunterHealthLocked && HunterHealth>0 && HunterHealth<HunterMaxHealth && timeSinceDamage>=RecoveryDelay;
    public float Slow { get; private set; }
    public float ScarLife { get; private set; }
    public Vector2 Scar { get; private set; }
    public Vector3 Destination { get; private set; }
    public const float BurstRadius=4.3f, RiftWidth=1.16f, RiftLength=30f, ScarRadius=.76f;
    public const float LungeWidth=1.56f, PounceWidth=1.56f;
    public event System.Action<Move,Vector3,Vector3> StrikeReleased;
    float ritualDuration;
    public float RitualProgress => Action==Move.Ritual ? 1-Mathf.Clamp01(timer/ritualDuration) : 0;
    public bool RitualComplete { get; private set; }
    public const int LampCapacity=6;
    public int LitLamps { get; private set; } = LampCapacity;
    public bool PressingLamp => Action==Move.Ritual && LitLamps<=2;
    internal void LeaveArena() {
        CancelGrandAttack();
        ScarLife=0;
        Enter(Move.Stalk,1);
    }
    public bool Pursuing => Action==Move.Pursue || Action==Move.Pounce;
    public const float CounterWindow=2f;

    public void SetLitLamps(int count) {
        bool wasDark=Enraged;
        LitLamps=Mathf.Clamp(count,0,LampCapacity);
        if(!wasDark && Enraged) {
            ScarLife=0;
            CancelGrandAttack();
            Enter(Move.Pursue,CurrentDarkPursuitDuration);
        } else if(wasDark && !Enraged) {
            CancelGrandAttack();
            Enter(Move.LightStagger,CurrentCounterWindow);
        }
    }
    public bool Enraged => LitLamps==0;
    public void Hurt(int amount) {
        if(HunterHealthLocked || HunterHealth<=0 || amount<=0)return;
        float multiplier=Enraged ? 1.55f : Mathf.Lerp(1.4f,1f,(LitLamps-1f)/(LampCapacity-1));
        HurtUnscaled(Mathf.RoundToInt(amount*multiplier));
    }
    public void HurtUnscaled(int amount) {
        if(HunterHealthLocked || HunterHealth<=0 || amount<=0)return;
        HunterHealth=Mathf.Max(0,HunterHealth-amount);
        timeSinceDamage=recoveryCarry=0;
        HunterDamaged?.Invoke();
    }
    public void StepExternalPlayerStatus(float dt) {
        if(dt<=0)return;
        Slow=Mathf.Max(0,Slow-dt);
    }
    internal bool RestoreHunterHealth(int amount) {
        if(HunterHealthLocked || HunterHealth<=0 || HunterHealth>=HunterMaxHealth || amount<=0)return false;
        HunterHealth=Mathf.Min(HunterMaxHealth,HunterHealth+amount);
        if(HunterHealth==HunterMaxHealth)recoveryCarry=0;
        return true;
    }
    // Called once by the encounter clock, before this frame's damage is resolved.
    public void StepRecovery(float dt) {
        if(dt<=0 || HunterHealthLocked || HunterHealth<=0 || HunterHealth>=HunterMaxHealth)return;
        double healingTime=System.Math.Max(0,dt-System.Math.Max(0,RecoveryDelay-timeSinceDamage));
        timeSinceDamage=System.Math.Min(RecoveryDelay,timeSinceDamage+dt);
        recoveryCarry+=healingTime*RecoveryPerSecond;
        int restored=(int)System.Math.Floor(recoveryCarry+.00001);
        if(restored<=0)return;
        recoveryCarry=System.Math.Max(0,recoveryCarry-restored);
        HunterHealth=Mathf.Min(HunterMaxHealth,HunterHealth+restored);
        if(HunterHealth==HunterMaxHealth)recoveryCarry=0;
    }
    public const int DemonHealth = 120;
    float timer=1.0f, moveDuration=1.0f;
    public float ActionRemaining => Mathf.Max(0,timer);
    public float MoveProgress => 1-Mathf.Clamp01(timer/moveDuration);
    int turn;
    bool struck;

    public string Tell => Action==Move.SleeveTell ? (SweepRight ? "双袖覆殿 · 右半场即将落袖，移向左侧" : "双袖覆殿 · 左半场即将落袖，移向右侧") :
        Action==Move.SleeveSweep ? "长袖扫落 · 观察下一侧预警" :
        Action==Move.BellTell ? (GrandAttacks ? "暮钟将响 · 波环经过身体时跳跃" : "暮钟双环 · 连跳两次，越过金色波环") :
        Action==Move.BellWaves ? "暮钟波环 · 跳过正在经过的光环" :
        Action==Move.Pursue ? (GrandAttacks ? (DarkHuntLocked ? "无月巡猎 · 扑杀路线已锁定，横移或射灯" : "无月巡猎 · 正在追踪，射亮任意灯") : "暗猎 · 影魔正在追你，射亮任意灯") :
        Action==Move.Pounce ? "暗猎扑杀 · 横移或射灯打断" :
        Action==Move.LightStagger ? "畏光硬直 · 射击地影" : Action==Move.Rift ? "裂影直斩 · 横移离开紫色斩线" :
        Action==Move.Burst ? "噬影震爆 · 退出影魔脚下的圆圈" :
        Action==Move.Ritual ? "影魔正在操纵灯火" : Action==Move.MarkFloor ? "留爪封路 · 离开脚下爪痕" :
        Action==Move.LungeTell ? "突进蓄势 · 横移避开冲锋线" :
        Action==Move.Claw ? "禁锢爪印 · 离开脚下锁定圈" :
        Action==Move.Recover ? "收招 · 反击机会" : Action==Move.Lunge ? "影魔扑击" : "影魔正在游猎";

    void Enter(Move move,float duration) {
        if(HalfHealthAssault && (move==Move.Stalk || move==Move.Recover))duration*=.35f;
        Action=move; timer=moveDuration=duration;
    }

    public bool BeginRitual() {
        if(Action!=Move.Stalk || Enraged || DoubleBellDue) return false;
        ritualRequested=false;
        ritualDuration=GrandAttacks ? (Phase==1 ? 4.2f : 3.8f) : LitLamps<=2 ? 1.45f : 1.15f;
        Enter(Move.Ritual,ritualDuration);
        return true;
    }
    public void BreakRitual() {
        if(Action==Move.Ritual) Enter(Move.Recover,GrandAttacks ? CurrentCounterWindow : .85f);
    }
    static float DistanceToSegment(Vector3 point,Vector3 a,Vector3 b) {
        Vector3 path=b-a;
        float t=Mathf.Clamp01(Vector3.Dot(point-a,path)/Mathf.Max(.00001f,path.sqrMagnitude));
        return Vector3.Distance(point,a+t*path);
    }
    public Rect MoveBounds = Rect.MinMaxRect(-6.8f,-5.8f,6.8f,2.6f);
    Vector3 Limit(Vector3 p) => new Vector3(Mathf.Clamp(p.x,MoveBounds.xMin,MoveBounds.xMax),p.y,Mathf.Clamp(p.z,MoveBounds.yMin,MoveBounds.yMax));

    void StartLunge(ClawState world,Vector3 delta) {
        Destination=Limit(world.Enemy+delta.normalized*Mathf.Min(delta.magnitude+.65f,5.5f));
        struck=false;
        Enter(Move.LungeTell,.56f);
    }
    int nextSource;
    bool Cast(IReadOnlyList<ClawState> shadows)
    {
        for(int i=0;i<shadows.Count;i++) {
            int index=(nextSource+i)%shadows.Count;
            if(shadows[index]!=null && shadows[index].StartCast()) {
                nextSource=index+1;
                return true;
            }
        }
        return false;
    }
    public void Step(float dt,ClawState wall,ClawState second,ClawState floor,float stun)
    {
        StepWithLights(dt,wall,new[] { second,wall,floor },floor,stun);
    }
    public void StepWithLights(float dt,ClawState wall,IReadOnlyList<ClawState> shadows,ClawState floor,float stun,
                               System.Func<Vector3,float,Vector3> moveBody=null)
    {
        if(dt<=0)return;
        Vector3 start=wall.Enemy;
        Move startingMove=Action;
        StepAction(dt,wall,shadows,floor,stun);
        if(moveBody!=null) wall.Enemy=moveBody(wall.Enemy,dt);
        if((startingMove!=Move.Pounce && startingMove!=Move.Lunge) || Action!=startingMove) return;

        // Sweep the distance the body actually travelled after the hall resolves collisions.
        float width=startingMove==Move.Pounce ? PounceWidth : LungeWidth;
        if(!struck && DistanceToSegment(wall.PlayerBody,start,wall.Enemy)<=width) {
            struck=true;
            StrikeReleased?.Invoke(startingMove,start,wall.PlayerBody);
            Hurt(startingMove==Move.Pounce ? 28 : 34);
        }
        if(startingMove==Move.Pounce) {
            if(struck || timer<=0 || wall.Enemy==Destination) Enter(Move.Pursue,CurrentDarkPursuitDuration);
        } else if(wall.Enemy==Destination || timer<=0) {
            if(struck) Enter(Move.Recover,.9f);
            else {
                Destination=wall.Enemy+(wall.Player-wall.Enemy).normalized*RiftLength;
                Enter(Move.Rift,.65f);
            }
        }
    }
    void StepAction(float dt,ClawState wall,IReadOnlyList<ClawState> shadows,ClawState floor,float stun)
    {
        RitualComplete=false;

        Slow=Mathf.Max(0,Slow-dt);
        if(ScarLife>0) {
            ScarLife=Mathf.Max(0,ScarLife-dt);
            if(wall.JumpHeight<=ClawState.GroundAttackHeight &&
               Vector2.Distance(new Vector2(wall.Player.x,wall.Player.z),Scar)<=ScarRadius) {
                Slow=1.5f; Hurt(12); ScarLife=0;
            }
        }
        if(Action==Move.LightStagger) {
            timer-=dt;
            if(timer<=0) Enter(Move.Stalk,.3f);
            return;
        }
        if(stun>0 && !Enraged) { CancelGrandAttack(); Enter(Move.Recover,GrandAttacks ? CurrentCounterWindow : .9f); return; }
        float previousTimer=timer;
        timer-=dt*(!GrandAttacks && Enraged ? 1.35f : 1f);
        Vector3 delta=wall.Player-wall.Enemy;
        if(StepGrandAttack(dt,wall))return;
        switch(Action) {
            case Move.Pursue:
                // Re-establish a striking distance, then hold the last telegraphed line for a sidestep.
                if(previousTimer>CurrentDarkLockDuration) {
                    wall.Enemy=Vector3.MoveTowards(wall.Enemy,Limit(wall.Player-delta.normalized*1.25f),4.8f*dt);
                    Destination=Limit(wall.Player+delta.normalized*.85f);
                }
                if(timer<=0) {
                    struck=false;
                    Enter(Move.Pounce,.85f);
                }
                break;
            case Move.Pounce:
                wall.Enemy=Vector3.MoveTowards(wall.Enemy,Destination,14f*dt);
                break;
            case Move.Stalk:
                if(GrandAttacks && ritualRequested) {
                    // The scene selects an eligible lamp on its next update. If all are
                    // protected, continue the sequence instead of waiting indefinitely.
                    if(timer<=0) {ritualRequested=false;Enter(Move.Stalk,.2f);}
                    break;
                }
                if(timer>0) {
                    Vector3 side=Vector3.Cross(Vector3.up,delta.normalized)*(turn%2==0 ? 1 : -1);
                    float spacing=LitLamps>=5 ? 2.8f : LitLamps>=3 ? 2.1f : 1.5f;
                    Vector3 goal=Limit(wall.Player-delta.normalized*spacing+side*.85f);
                    wall.Enemy=Vector3.MoveTowards(wall.Enemy,goal,dt*(LitLamps<=2 ? 3.0f : 2.65f));
                    break;
                }
                if(GrandAttacks && BeginNextGrandAction(wall,shadows,floor))break;
                if(DoubleBellDue) {
                    BeginBell(wall.Enemy,12f,2,1.2f);
                    attacksBeforeDoubleBell=5;
                    break;
                }
                if(HalfHealthAssault)attacksBeforeDoubleBell--;
                int choice=turn++%5;
                if(delta.magnitude>(choice==0 ? ClawState.MaxReach : 5f)) choice=turn%2==0 ? 2 : 3;
                if(choice==4 && delta.magnitude>BurstRadius+.3f) choice=3;
                if(choice==0 && Cast(shadows)) {
                    Enter(Move.Claw,ClawState.EndTime);
                } else if(choice==1 && floor!=null) {
                    Scar=new Vector2(wall.Player.x,wall.Player.z);
                    Enter(Move.MarkFloor,.60f);
                } else if(choice==3) {
                    Destination=wall.Enemy+delta.normalized*RiftLength;
                    Enter(Move.Rift,.55f);
                } else if(choice==4) {
                    Destination=wall.Enemy;
                    Enter(Move.Burst,.78f);
                } else {
                    StartLunge(wall,delta);
                }
                break;
            case Move.Ritual:
                if(PressingLamp && delta.magnitude>1.05f)
                    wall.Enemy=Vector3.MoveTowards(wall.Enemy,Limit(wall.Player-delta.normalized*1.05f),dt*2.75f);
                if(timer<=0) {
                    RitualComplete=true;
                    if(GrandAttacks) Enter(Move.Recover,CurrentCounterWindow);
                    else if(PressingLamp && LitLamps==2) StartLunge(wall,delta);
                    else Enter(Move.Recover,.65f);
                }
                break;
            case Move.Rift:
            case Move.Burst:
                if(timer<=0) {
                    StrikeReleased?.Invoke(Action,wall.Enemy,Destination);
                    bool hit=Action==Move.Rift ? DistanceToSegment(wall.PlayerBody,wall.Enemy,Destination)<=RiftWidth :
                        Vector2.Distance(new Vector2(wall.Player.x,wall.Player.z),new Vector2(Destination.x,Destination.z))<=BurstRadius;
                    // Low ground attacks are avoided by lifting the hunter's body above them.
                    if(Action==Move.Burst && wall.JumpHeight>ClawState.GroundAttackHeight) hit=false;
                    if(hit) Hurt(Action==Move.Rift ? 25 : 30);
                    Enter(Move.Recover,.49f);
                }
                break;
            case Move.Claw:
                foreach(var shadow in shadows) if(shadow!=null && shadow.Active) return;
                Enter(Move.Recover,.5f);
                break;
            case Move.MarkFloor:
                if(timer<=0) {
                    ScarLife=3.5f;
                    if(LitLamps<=4) StartLunge(wall,delta);
                    else Enter(Move.Recover,.49f);
                }
                break;
            case Move.LungeTell:
                if(timer<=0) Enter(Move.Lunge,1);
                break;
            case Move.Lunge:
                wall.Enemy=Vector3.MoveTowards(wall.Enemy,Destination,dt*(Enraged ? 12.5f : 10f));
                break;
            case Move.Recover:
                if(timer<=0) Enter(Move.Stalk,.40f);
                break;
        }
    }
}
