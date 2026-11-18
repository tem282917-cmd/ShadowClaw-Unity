using UnityEngine;

// The controller supplies real horizontal displacement, never key-down time.
public sealed class HunterMotionState
{
    public float BreathPhase {get;private set;}
    public float GaitPhase {get;private set;}
    public float MovementBlend {get;private set;}
    public float RunBlend {get;private set;}
    public int Footsteps {get;private set;}
    float stepDistance;
    public void Reset() {BreathPhase=GaitPhase=MovementBlend=RunBlend=stepDistance=0;Footsteps=0;}
    public bool Step(float distance,float dt,bool grounded,bool sprinting) {
        if(dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return false;
        distance=Mathf.Max(0,distance);
        bool moving=grounded && distance>.0001f;
        BreathPhase=Mathf.Repeat(BreathPhase+dt*1.7f,Mathf.PI*2);
        MovementBlend=Mathf.MoveTowards(MovementBlend,moving ? 1 : 0,dt*6);
        RunBlend=Mathf.MoveTowards(RunBlend,moving && sprinting ? 1 : 0,dt*5);
        if(!moving) {stepDistance=0;return false;}
        float stride=sprinting ? 2.5f : 2.1f;
        GaitPhase=Mathf.Repeat(GaitPhase+distance/stride*Mathf.PI,Mathf.PI*2);
        stepDistance+=distance;
        if(stepDistance<stride)return false;
        stepDistance=Mathf.Repeat(stepDistance,stride);Footsteps++;return true;
    }
    public Vector3 PositionOffset=>new Vector3(
        Mathf.Sin(GaitPhase)*Mathf.Lerp(.007f,.018f,RunBlend)*MovementBlend,
        Mathf.Sin(BreathPhase)*.0022f+Mathf.Abs(Mathf.Cos(GaitPhase))*Mathf.Lerp(.006f,.014f,RunBlend)*MovementBlend-RunBlend*.018f,
        Mathf.Sin(BreathPhase*.5f)*.001f-MovementBlend*.004f);
    public Vector3 RotationOffset=>new Vector3(
        Mathf.Sin(BreathPhase)*.25f+Mathf.Cos(GaitPhase*2)*.65f*MovementBlend+RunBlend*2,
        Mathf.Sin(GaitPhase)*.55f*MovementBlend,
        -Mathf.Sin(GaitPhase)*Mathf.Lerp(.55f,1.3f,RunBlend)*MovementBlend);
}
