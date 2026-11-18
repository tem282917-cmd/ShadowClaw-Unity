using System;
using UnityEngine;

// The authored F01 model already includes its helmet. Its companion F02 cage
// follows the sternum as the upper body turns, leaving the fixed feet in place.
internal sealed class FixedGuardianArmour : IDisposable {
    readonly Transform holder, torso, leftArm, rightArm, leftHand, rightHand, chest;
    readonly Quaternion torsoRest,leftRest,rightRest;
    readonly Renderer lampSurface;
    readonly MaterialPropertyBlock lampTint=new MaterialPropertyBlock();
    readonly LanternFire fire;
    readonly ParticleSystem[] flames=Array.Empty<ParticleSystem>();
    readonly bool hasBody;
    public bool Ready => hasBody;
    internal SkinnedMeshRenderer ShadowSkin {get;}
    public Vector3 ChestPosition => chest.position;
    public Vector3 SweepOrigin(int side) => (side<0?leftHand:rightHand)?.position ?? chest.position;
    public FixedGuardianArmour(Transform parent, GameObject source, GameObject lanternSource) : this(parent,source,lanternSource,null) {}
    public FixedGuardianArmour(Transform parent, GameObject source, GameObject lanternSource, GameObject customHelmet) {
        holder=new GameObject("Original F01 guardian armour").transform;
        holder.SetParent(parent,false);
        var actor=UnityEngine.Object.Instantiate(source,holder,false);
        bool originalBody=source==Resources.Load<GameObject>("Guardian/F01");
        if(originalBody)ShadowSkin=actor.GetComponentInChildren<SkinnedMeshRenderer>(true);
        actor.name=originalBody?"F01 Lamplighter":source.name;
        if(!originalBody) {
            foreach(var animator in actor.GetComponentsInChildren<Animator>(true))animator.enabled=false;
            foreach(var animation in actor.GetComponentsInChildren<Animation>(true))animation.enabled=false;
            PrepareDecoration(actor,7.8f,true);
        }
        hasBody=actor.GetComponentInChildren<Renderer>(true)!=null;
        var bones=actor.GetComponentsInChildren<Transform>(true);
        Transform Bone(string name) => Array.Find(bones,t=>t.name==name);
        var head=Bone("Head");
        if(customHelmet!=null && !originalBody && head!=null) {
            var helmet=UnityEngine.Object.Instantiate(customHelmet,holder,false);
            PrepareDecoration(helmet,1.65f,false);
            // Preserve the fitted world scale under imported skeletons with non-unit bind units.
            var mount=new GameObject("Custom helmet mount").transform;
            mount.SetParent(holder,false);mount.position=head.position+holder.up*(.48f*holder.lossyScale.y);
            helmet.transform.SetParent(mount,false);
            mount.SetParent(head,true);
        }
        torso=Bone("Torso");leftArm=Bone("UpperArm.L");rightArm=Bone("UpperArm.R");
        leftHand=Bone("Palm.L");rightHand=Bone("Palm.R");
        torsoRest=torso!=null?torso.localRotation:Quaternion.identity;
        leftRest=leftArm!=null?leftArm.localRotation:Quaternion.identity;
        rightRest=rightArm!=null?rightArm.localRotation:Quaternion.identity;
        chest=new GameObject("Guardian chest lantern mount").transform;
        chest.SetParent(holder,false);
        chest.localPosition=new Vector3(0,5.21f,.50f);
        if(torso!=null)chest.SetParent(torso,true);
        if(lanternSource!=null) {
            var lamp=UnityEngine.Object.Instantiate(lanternSource,chest,false);
            bool originalLamp=lanternSource==Resources.Load<GameObject>("Guardian/F02");
            lamp.name=originalLamp?"F02 chest lamp":lanternSource.name;
            if(!originalLamp)PrepareDecoration(lamp,1.38f,false);
            lampSurface=lamp.GetComponentInChildren<MeshRenderer>();
            var wick=new GameObject("F02 wick").transform;wick.SetParent(chest,false);
            wick.localPosition=new Vector3(0,-.24f,.13f);
            wick.localScale=Vector3.one*.63f;
            fire=LanternFire.Create(wick);
            fire.SetStrength(1);
            flames=fire.GetComponentsInChildren<ParticleSystem>();
            foreach(var particle in flames)particle.Pause(false);
        }
    }
    internal static void PrepareDecoration(GameObject actor,float height,bool ground) {
        foreach(var collider in actor.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        foreach(var light in actor.GetComponentsInChildren<Light>(true))light.enabled=false;
        var t=actor.transform;
        bool found=false;Bounds bounds=default;
        foreach(var renderer in actor.GetComponentsInChildren<Renderer>(true)) {
            if(!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))continue;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);
        }
        if(!found || bounds.size.y<.001f)return;
        Vector3 anchor=bounds.center;if(ground)anchor.y=bounds.min.y;
        float scale=height*Mathf.Abs(t.parent.lossyScale.y)/bounds.size.y;
        t.localScale*=scale;
        t.position=t.parent.position+(t.position-anchor)*scale;
    }
    public void Tick(float dt) {
        foreach(var particle in flames)particle.Simulate(dt,false,false,true);
    }
    static void AimArm(Transform upper,Transform hand,Vector3 direction,float amount) {
        if(upper==null || hand==null || amount<=0)return;
        Vector3 aim=hand.position-upper.position;
        if(aim.sqrMagnitude<.001f)return;
        Quaternion turn=Quaternion.FromToRotation(aim,direction)*upper.rotation;
        upper.rotation=Quaternion.Slerp(upper.rotation,turn,amount);
    }
    public void Draw(FixedGuardianState state,float clock,float yaw) {
        var move=state.Action;
        if(torso!=null)torso.localRotation=torsoRest*Quaternion.Euler(Mathf.Sin(clock*1.7f)*.8f,Mathf.Clamp(yaw,-35,35),0);
        if(leftArm!=null)leftArm.localRotation=leftRest;
        if(rightArm!=null)rightArm.localRotation=rightRest;
        float raise=move==FixedGuardianState.Move.SlamLocked ? 1 : move==FixedGuardianState.Move.SlamTrack ?
            Mathf.SmoothStep(0,1,state.Progress) : move==FixedGuardianState.Move.SlamImpact ? 1-state.Progress : 0;
        AimArm(rightArm,rightHand,holder.up+holder.forward*.23f,raise);
        if(move==FixedGuardianState.Move.SweepTell || move==FixedGuardianState.Move.SweepImpact) {
            float t=move==FixedGuardianState.Move.SweepTell ? Mathf.SmoothStep(0,1,state.Progress):1-state.Progress;
            bool left=state.SweepSide<0;
            AimArm(left?leftArm:rightArm,left?leftHand:rightHand,holder.right*state.SweepSide+holder.forward*.28f,t);
        }
        bool gathering=move==FixedGuardianState.Move.Ritual || move==FixedGuardianState.Move.WaveTell;
        if(gathering) {
            float amount=Mathf.SmoothStep(0,1,state.Progress)*.78f;
            AimArm(leftArm,leftHand,-holder.right*.55f+holder.forward,amount);
            AimArm(rightArm,rightHand,holder.right*.55f+holder.forward,amount);
        }
        float pulse=gathering ? 1+.13f*Mathf.Sin(clock*7) : 1;
        if(lampSurface!=null) {
            lampTint.SetColor("_EmissionColor",state.Dark?new Color(.27f,.10f,.43f):new Color(.55f,.38f,.19f)*pulse);
            lampSurface.SetPropertyBlock(lampTint);
        }
        fire?.SetStrength(state.Dark?0:1);
    }
    public void Dispose() {
        if(holder!=null)UnityEngine.Object.Destroy(holder.gameObject);
    }
}
