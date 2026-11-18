using UnityEngine;

public sealed class HunterCrossbow
{
    public enum Motion { Ready, Cycle, Lower, Load, Raise }
    public const int Capacity=4;
    public Bolt Loaded { get; private set; } = Bolt.Holy;
    public Bolt Target { get; private set; } = Bolt.Holy;
    public int Rounds { get; private set; } = Capacity;
    public Motion Phase { get; private set; }
    Bolt? queued;
    public Bolt? Queued => queued;
    float remaining, duration;
    bool switching;

    public float PhaseProgress => duration>0 ? Mathf.Clamp01(1-remaining/duration) : 0;

    public float Lowered {
        get {
            switch(Phase) {
                case Motion.Lower: return 1-remaining/duration;
                case Motion.Load: return 1;
                case Motion.Raise: return remaining/duration;
                default: return 0;
            }
        }
    }
    void Enter(Motion phase,float time) {
        Phase=phase; remaining=duration=time;
    }
    public void Request(Bolt type) {
        if(Phase!=Motion.Ready) {
            queued=type;
            return;
        }
        if(type==Loaded && Rounds==Capacity) return;
        Target=type;
        switching=Target!=Loaded;
        Enter(Motion.Lower,switching ? .24f : .18f);
    }
    public bool Fire() {
        if(Phase!=Motion.Ready) return false;
        if(Rounds==0) { Request(Loaded); return false; }
        Rounds--;
        Enter(Motion.Cycle,.32f);
        return true;
    }
    public void Step(float dt) {
        while(Phase!=Motion.Ready && dt>=remaining) {
            dt-=remaining;
            switch(Phase) {
                case Motion.Lower:
                    Enter(Motion.Load,switching ? .90f : .78f);
                    break;
                case Motion.Load:
                    Loaded=Target;
                    Rounds=Capacity;
                    Enter(Motion.Raise,switching ? .30f : .24f);
                    break;
                default:
                    Enter(Motion.Ready,0);
                    if(queued.HasValue) {
                        var next=queued.Value; queued=null; Request(next);
                    }
                    break;
            }
        }
        if(Phase!=Motion.Ready) remaining-=dt;
    }
}
