using UnityEngine;

public sealed partial class ShadowLab
{
    readonly HunterMotionState hunterMotion=new HunterMotionState();
    float pendingMovement;
    public int FootstepCount=>hunterMotion.Footsteps;
    public HunterMotionState MotionFeedback=>hunterMotion;
    void RecordPlayerMotion(Vector3 before,Vector3 after) {
        pendingMovement+=Vector3.ProjectOnPlane(after-before,Vector3.up).magnitude;
    }
    void ResetPlayerMotion() {pendingMovement=0;hunterMotion.Reset();weaponView?.SetLocomotion(Vector3.zero,Vector3.zero);}
    void TickPlayerMotion(float dt) {
        bool grounded=state.JumpHeight<=.001f && (playerBody==null || playerBody.isGrounded);
        bool step=hunterMotion.Step(pendingMovement,dt,grounded,Sprinting);
        pendingMovement=0;
        if(step)eye.GetComponent<ShiyinAudio>()?.PlayFootstep(Sprinting,hunterMotion.Footsteps);
        bool reduced=UI!=null && UI.Settings.ReducedMotion;
        weaponView?.SetLocomotion(reduced ? Vector3.zero : hunterMotion.PositionOffset,reduced ? Vector3.zero : hunterMotion.RotationOffset);
    }
}
