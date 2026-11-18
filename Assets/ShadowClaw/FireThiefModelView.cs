using System.Collections.Generic;
using UnityEngine;

// Skin and tint use the encounter clock, including when a single tick is advanced in a replay.
internal sealed class FireThiefModelView
{
    sealed class Surface {
        internal Renderer Renderer;
        internal int Slot,ColorProperty;
        internal Color Color;
        internal MaterialPropertyBlock Original,Working;
    }
    readonly Transform root;
    readonly Vector3 position,scale;
    readonly Quaternion rotation;
    readonly List<Surface> surfaces=new List<Surface>();
    readonly Animation animation;
    readonly Dictionary<string,AnimationClip> clips=new Dictionary<string,AnimationClip>();
    readonly Transform[] bones;
    readonly Quaternion[] previousRotations;
    readonly Vector3[] previousPositions;
    // Delivery is frame 17 of the authored 24 fps cast; the planted half-stride travels 0.52 m.
    const float CastReleasePhase=2f/3f, TransitionSeconds=.12f, StrideMetres=1.04f;
    float gait,transition,deathTime,channelTime;
    string currentClip;
    internal Transform CastingHand {get;}
    internal bool FinishingAction => release>0 || hit>0;
    float age,release,hit,tilt,roll,tint;

    internal FireThiefModelView(Transform model) {
        root=model;position=root.localPosition;rotation=root.localRotation;scale=root.localScale;
        animation=root.GetComponentInChildren<Animation>(true);
        if(animation!=null) {
            foreach(AnimationState state in animation) {
                string name=state.name;
                clips[name.Substring(name.LastIndexOf('|')+1)]=state.clip;
            }
            if(clips.ContainsKey("Idle")) {animation.Stop();animation.enabled=false;}
        }
        var skin=root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        bones=skin!=null ? skin.bones : System.Array.Empty<Transform>();
        previousPositions=new Vector3[bones.Length];previousRotations=new Quaternion[bones.Length];
        foreach(var bone in bones)if(bone!=null && bone.name=="RightHand")CastingHand=bone;
        int baseColor=Shader.PropertyToID("_BaseColor"),legacyColor=Shader.PropertyToID("_Color");
        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)) {
            // Imported particles, if a custom model supplies any, retain their own palette.
            if(!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))continue;
            var materials=renderer.sharedMaterials;
            for(int slot=0;slot<materials.Length;slot++) {
                var material=materials[slot];if(material==null)continue;
                int property=material.HasProperty(baseColor) ? baseColor : material.HasProperty(legacyColor) ? legacyColor : -1;
                if(property<0)continue;
                var original=new MaterialPropertyBlock();renderer.GetPropertyBlock(original,slot);
                var working=new MaterialPropertyBlock();renderer.GetPropertyBlock(working,slot);
                surfaces.Add(new Surface {Renderer=renderer,Slot=slot,ColorProperty=property,
                    Color=original.HasProperty(property) ? original.GetColor(property) : material.GetColor(property),
                    Original=original,Working=working});
            }
        }
    }
    internal void OnVolley() {
        release=.35f;hit=0;
        // Snap to the delivery key before the actor reads the actual casting hand for the projectile.
        Pose("Cast",CastReleasePhase,TransitionSeconds);
    }
    internal void OnInterrupted() {release=0;hit=0;channelTime=0;Pose("Stagger",0,0);}
    internal void OnHit() {hit=.32f;Pose("Stagger",.18f,.04f);}
    internal bool StartDeath() {
        release=hit=deathTime=0;
        if(!clips.ContainsKey("Death"))return false;
        Pose("Death",0,0);return true;
    }
    internal bool TickDeath(float dt) {
        if(!clips.TryGetValue("Death",out var clip))return false;
        deathTime+=dt;
        Pose("Death",Mathf.Clamp01(deathTime/clip.length),dt);
        return deathTime<clip.length+.12f;
    }
    // Preserve the pose at a state change for a short crossfade. SampleAnimation evaluates
    // imported bone curves synchronously, even with the regular Animation player disabled.
    void Pose(string name,float phase,float dt) {
        if(!clips.TryGetValue(name,out var clip))return;
        if(currentClip!=name) {
            for(int i=0;i<bones.Length;i++)if(bones[i]!=null) {
                previousPositions[i]=bones[i].localPosition;previousRotations[i]=bones[i].localRotation;
            }
            transition=currentClip==null ? 1 : 0;currentClip=name;
        }
        clip.SampleAnimation(animation.gameObject,Mathf.Clamp01(phase)*clip.length);
        transition=Mathf.Min(1,transition+dt/TransitionSeconds);
        if(transition>=1)return;
        float weight=Mathf.SmoothStep(0,1,transition);
        for(int i=0;i<bones.Length;i++)if(bones[i]!=null) {
            bones[i].localPosition=Vector3.Lerp(previousPositions[i],bones[i].localPosition,weight);
            bones[i].localRotation=Quaternion.Slerp(previousRotations[i],bones[i].localRotation,weight);
        }
    }
    internal void Tick(float dt,FireThiefCycle.Action action,float progress,bool engaged,bool protectedByLamp,bool empowered,float walked) {
        if(root==null || dt<=0)return;
        age+=dt;
        float breath=Mathf.Sin(age*2.25f),prayer=action==FireThiefCycle.Action.Prayer ? 1 : 0;
        float windup=action==FireThiefCycle.Action.Windup ? Mathf.SmoothStep(0,1,progress) : 0;
        float stagger=action==FireThiefCycle.Action.Stagger ? 1-Mathf.SmoothStep(0,1,progress) : 0;
        float recoil=release/.35f,wound=hit/.32f;
        float blend=1-Mathf.Exp(-dt*14f);
        if(clips.ContainsKey("Idle")) {
            if(action!=FireThiefCycle.Action.Prayer || !engaged)channelTime=0;
            if(hit>0)Pose("Stagger",Mathf.Lerp(.18f,1,1-wound),dt);
            else if(action==FireThiefCycle.Action.Stagger && engaged)Pose("Stagger",progress,dt);
            else if(release>0)Pose("Cast",Mathf.Lerp(CastReleasePhase,1,1-recoil),dt);
            else if(action==FireThiefCycle.Action.Windup && engaged)Pose("Cast",Mathf.Clamp01(progress)*CastReleasePhase,dt);
            else if(action==FireThiefCycle.Action.Prayer && engaged) {
                channelTime+=dt;Loop("Channel",channelTime,dt);
            } else if(Mathf.Abs(walked)>.00001f) {
                gait=Mathf.Repeat(gait+walked/StrideMetres,1);Pose("Walk",gait,dt);
            } else Loop("Idle",age,dt);
        } else {
            // Authored replacement meshes without animation still get a readable windup.
            tilt=Mathf.Lerp(tilt,prayer*(7f+Mathf.Sin(age*3.2f)*1.4f)-windup*7f+recoil*12f-stagger*10f-wound*4f,blend);
            roll=Mathf.Lerp(roll,stagger*5f+wound*2f+(engaged ? .3f : .65f)*Mathf.Sin(age*1.25f),blend);
            root.localRotation=rotation*Quaternion.Euler(tilt,0,roll);
            root.localScale=Vector3.Scale(scale,new Vector3(1-breath*.002f,1+breath*.006f,1-breath*.002f));
        }
        release=Mathf.Max(0,release-dt);hit=Mathf.Max(0,hit-dt);
        float target=empowered ? .25f : prayer>0 ? .2f : protectedByLamp ? .12f : windup*.12f;
        tint=Mathf.Lerp(tint,target,blend);
        foreach(var surface in surfaces) {
            if(surface.Renderer==null)continue;
            // A restrained violet cast on the existing texture, never emission.
            Color colour=Color.Lerp(surface.Color,new Color(.48f,.30f,.57f,surface.Color.a),tint);
            colour=Color.Lerp(colour,new Color(.64f,.44f,.45f,surface.Color.a),wound*.22f);
            surface.Working.SetColor(surface.ColorProperty,colour);
            surface.Renderer.SetPropertyBlock(surface.Working,surface.Slot);
        }
    }
    void Loop(string name,float seconds,float dt) {
        if(clips.TryGetValue(name,out var clip))Pose(name,Mathf.Repeat(seconds/clip.length,1),dt);
    }
    internal void Reset() {
        age=release=hit=tilt=roll=tint=0;
        gait=transition=deathTime=channelTime=0;currentClip=null;
        Pose("Idle",0,0);
        if(root!=null) {root.localPosition=position;root.localRotation=rotation;root.localScale=scale;}
        foreach(var surface in surfaces)if(surface.Renderer!=null)surface.Renderer.SetPropertyBlock(surface.Original,surface.Slot);
    }
}
