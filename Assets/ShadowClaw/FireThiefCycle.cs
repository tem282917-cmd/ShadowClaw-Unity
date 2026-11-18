using UnityEngine;

// Connection is a persistent shield, distinct from the timed fire-stealing prayer.
public sealed class FireThiefCycle
{
    public enum Action { Waiting, Windup, Prayer, Stagger, Dead }
    [System.Flags] public enum Signal { None=0, Volley=1, PrayerStarted=2, Extinguish=4, ConnectionStarted=8 }
    public const int AttacksBeforeConnection=3;
    public Action Current {get;private set;}
    public bool Connected {get;private set;}
    public int AttacksReleased=>volleys;
    public float Remaining {get;private set;}
    public float Progress=>1-Mathf.Clamp01(Remaining/Mathf.Max(.01f,duration));
    public float AttackInterval=2.4f,Windup=1.1f,PrayerTime=4.2f,StaggerTime=2.2f;
    // Serialized historical callers may still set this; the accepted rule is exactly three.
    public int VolleysBeforePrayer=3;
    int volleys;
    float duration;
    bool linkedDark;
    void Enter(Action action,float seconds) {Current=action;Remaining=duration=Mathf.Max(.01f,seconds);}
    public void Reset() {volleys=0;Connected=linkedDark=false;Enter(Action.Waiting,.8f);}
    public void Kill() {Connected=false;Enter(Action.Dead,1);}
    public bool LampShot() {
        if(!Connected || Current==Action.Dead)return false;
        Connected=linkedDark=false;volleys=0;Enter(Action.Stagger,StaggerTime);return true;
    }
    public void LoseLamp() {
        if(!Connected)return;
        Connected=linkedDark=false;volleys=0;
        if(Current!=Action.Dead)Enter(Action.Waiting,.35f);
    }
    Signal Connect(bool lit) {
        Connected=true;linkedDark=!lit;
        Enter(lit ? Action.Prayer : Action.Waiting,lit ? PrayerTime : AttackInterval);
        return Signal.ConnectionStarted | (lit ? Signal.PrayerStarted : Signal.None);
    }
    public Signal BeginEngagement(bool lit,bool available) {
        // Seeing the hunter opens with fire theft. Re-entering range must not
        // restart an existing prayer or erase a just-earned lamp-shot stagger.
        if(!available || Connected || Current==Action.Dead || Current==Action.Stagger)return Signal.None;
        return Connect(lit);
    }
    public Signal Step(float dt,bool lit,bool available=true) {
        if(dt<=0 || Current==Action.Dead)return Signal.None;
        if(!available) LoseLamp();
        if(Connected && linkedDark && lit) {LampShot();return Signal.None;}
        if(Connected && !lit) {
            linkedDark=true;
            if(Current==Action.Prayer)Enter(Action.Waiting,.35f);
        }
        // Outside a fresh engagement, regaining a lamp keeps the three-attack
        // recovery window after the hunter interrupted its connection.
        if(!Connected && available && volleys>=AttacksBeforeConnection && Current==Action.Waiting)return Connect(lit);
        Remaining=Mathf.Max(0,Remaining-dt);
        if(Remaining>0)return Signal.None;
        switch(Current) {
            case Action.Waiting: Enter(Action.Windup,Windup);return Signal.None;
            case Action.Windup:
                volleys=Mathf.Min(AttacksBeforeConnection,volleys+1);
                if(!Connected && available && volleys>=AttacksBeforeConnection)
                    return Signal.Volley | Connect(lit);
                Enter(Action.Waiting,AttackInterval);return Signal.Volley;
            case Action.Prayer:
                linkedDark=true;Enter(Action.Waiting,.5f);return Signal.Extinguish;
            case Action.Stagger: Enter(Action.Waiting,AttackInterval);return Signal.None;
            default:return Signal.None;
        }
    }
}
