using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The armour reuses the original F01 and F02 assets. Effects are imported prefabs;
// the simple lines display the real combat geometry for authoring and verification.
internal sealed class FixedGuardianView {
    readonly FixedLanternGuardian owner;
    readonly Transform root,body,torso,leftArm,rightArm,chest;
    readonly LineRenderer boundary,tether;
    readonly WallInk shadow;
    readonly GuardianShadowCaster shadowCaster;
    internal BellShadowProjection ShadowProjection => shadow.Body;
    readonly Material armour,trim,heart,edge,ink;
    readonly List<Effect> effects=new List<Effect>();
    Effect travellingWave,ritualEffect;
    readonly ParticleSystem.Particle[] waveSample=new ParticleSystem.Particle[1];
    float leftAngle,rightAngle;
    readonly FixedGuardianArmour importedArmour;
    internal bool HasImportedArmour => importedArmour!=null && importedArmour.Ready;
    internal Vector3 ChestPosition => chest.position;
    internal Vector3 SweepOrigin(int side) => importedArmour!=null ? importedArmour.SweepOrigin(side) : (side<0 ? leftArm:rightArm).position;
    sealed class Effect {
        public GameObject Root;
        public ParticleSystem[] Particles;
        public Animator[] Animators;
        public float Remaining;
        public Vector3 Scale;
        public bool Frozen;
        public float ReferenceRadius=1;
        public ParticleSystem Ring;
    }

    static Material Material(string shader,Color color) {
        Shader selected=Resources.Load<Shader>(shader);
        if(selected==null)selected=Shader.Find("Sprites/Default");
        var result=new Material(selected){color=color};
        if(result.HasProperty("_SelfLit"))result.SetFloat("_SelfLit",.5f);
        return result;
    }
    Transform Part(string name,PrimitiveType type,Transform parent,Vector3 local,Vector3 size,Material material) {
        GameObject obj=GameObject.CreatePrimitive(type);obj.name=name;
        obj.transform.SetParent(parent,false);obj.transform.localPosition=local;obj.transform.localScale=size;
        obj.GetComponent<Collider>().enabled=false;
        var renderer=obj.GetComponent<Renderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        return obj.transform;
    }
    Transform Pivot(string name,Transform parent,Vector3 local) {
        var obj=new GameObject(name).transform;obj.SetParent(parent,false);obj.localPosition=local;return obj;
    }
    LineRenderer Line(string name,int count) {
        var obj=new GameObject(name);obj.transform.SetParent(root,false);
        var line=obj.AddComponent<LineRenderer>();line.sharedMaterial=edge;
        line.useWorldSpace=true;line.positionCount=count;line.widthMultiplier=.08f;
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;return line;
    }
    internal FixedGuardianView(FixedLanternGuardian owner) {
        this.owner=owner;
        root=Pivot("Guardian development presentation",owner.transform,Vector3.zero);
        armour=Material("Ink",new Color(.19f,.19f,.25f));
        trim=Material("Ink",new Color(.56f,.43f,.25f));
        heart=Material("Ink",new Color(1,.58f,.19f));heart.SetFloat("_SelfLit",1);
        edge=Material("IncantationEdge",Color.white);
        ink=Material("Guardian shadow uses imported shader",new Color(.025f,.018f,.035f,.92f));
        body=Pivot("Fixed hollow armour",root,Vector3.zero);
        body.localScale=Vector3.one*(owner.BodyHeight/7.8f);
        torso=Pivot("Turning waist",body,new Vector3(0,3.2f,0));
        if(owner.BodyPrefab!=null) {
            importedArmour=new FixedGuardianArmour(body,owner.BodyPrefab,owner.ChestLanternPrefab,owner.HelmetPrefab);
            root.name="Guardian imported armour presentation";
            chest=Pivot("Chest lantern indicator",torso,Vector3.zero);
            leftArm=Pivot("Left sweep origin",torso,new Vector3(-1.5f,2.6f,0));
            rightArm=Pivot("Right sweep origin",torso,new Vector3(1.5f,2.6f,0));
        } else {
            Part("Rooted plinth",PrimitiveType.Cylinder,body,new Vector3(0,.4f,0),new Vector3(3.2f,.4f,2.6f),armour);
            Part("Hollow skirt left",PrimitiveType.Cube,body,new Vector3(-.75f,1.9f,0),new Vector3(.7f,2.6f,1.2f),armour);
            Part("Hollow skirt right",PrimitiveType.Cube,body,new Vector3(.75f,1.9f,0),new Vector3(.7f,2.6f,1.2f),armour);
            Part("Breastplate",PrimitiveType.Cube,torso,new Vector3(0,1.4f,-.3f),new Vector3(2.6f,2.8f,.65f),armour);
            Part("Left rib",PrimitiveType.Cube,torso,new Vector3(-1.1f,1.15f,.3f),new Vector3(.34f,2.5f,1.2f),trim);
            Part("Right rib",PrimitiveType.Cube,torso,new Vector3(1.1f,1.15f,.3f),new Vector3(.34f,2.5f,1.2f),trim);
            Part("Helm",PrimitiveType.Cube,torso,new Vector3(0,3.5f,0),new Vector3(1.35f,1.65f,1.2f),armour);
            Part("Visor",PrimitiveType.Cube,torso,new Vector3(0,3.55f,.62f),new Vector3(.9f,.12f,.08f),heart);
            chest=Part("Chest lantern indicator",PrimitiveType.Sphere,torso,new Vector3(0,1.5f,.65f),Vector3.one*.8f,heart);
            leftArm=Arm(-1);rightArm=Arm(1);
        }
        var collider=body.gameObject.AddComponent<CapsuleCollider>();
        collider.center=Vector3.up*3.7f;collider.height=7.4f;collider.radius=1.2f;
        boundary=Line("Guardian exact attack boundary",97);boundary.loop=true;
        tether=Line("Guardian ritual target guide",17);tether.loop=false;
        float span=Mathf.Max(50,owner.ArenaSize.magnitude*2);
        var p=owner.transform.position;
        shadow=new WallInk(root,"Guardian ground shadow",ink,-.032f,true,
            Rect.MinMaxRect(p.x-span,p.z-span,p.x+span,p.z+span),owner.Encounter.GroundHeightAt);
        shadowCaster=new GuardianShadowCaster(shadow.BindArmour(),importedArmour?.ShadowSkin,body,torso,leftArm,rightArm);
        Reset();
    }
    Transform Arm(int side) {
        Transform arm=Pivot(side<0 ? "Left giant arm" : "Right giant arm",torso,new Vector3(side*1.5f,2.6f,0));
        Part("Shoulder bell",PrimitiveType.Sphere,arm,Vector3.zero,new Vector3(1.7f,1.4f,1.7f),armour);
        Part("Upper arm",PrimitiveType.Cube,arm,new Vector3(side*.35f,-1.2f,0),new Vector3(.7f,2.1f,.8f),armour);
        Part("Forearm plate",PrimitiveType.Cube,arm,new Vector3(side*.65f,-2.7f,.2f),new Vector3(1.2f,1.6f,1.2f),trim);
        Part("Hammer fist",PrimitiveType.Cube,arm,new Vector3(side*.65f,-3.7f,.2f),new Vector3(1.35f,.9f,1.5f),armour);
        return arm;
    }
    Vector3 Ground(Vector3 position) {
        position.y=owner.Encounter.GroundHeightAt(position.x,position.z)+.065f;return position;
    }
    Effect Spawn(GameObject prefab,Vector3 point,Quaternion rotation,float life,float scale=1,bool freeze=false) {
        if(prefab==null)return null;
        var obj=Object.Instantiate(prefab,point,rotation,root);
        obj.SetActive(true);
        obj.transform.localScale*=scale;
        var effect=new Effect {Root=obj,Particles=obj.GetComponentsInChildren<ParticleSystem>(true),
            Animators=obj.GetComponentsInChildren<Animator>(true),Remaining=life,Scale=obj.transform.localScale,Frozen=freeze};
        foreach(var light in obj.GetComponentsInChildren<Light>(true))light.enabled=false;
        foreach(var collider in obj.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        foreach(var rigidbody in obj.GetComponentsInChildren<Rigidbody>(true)) {
            rigidbody.isKinematic=true;rigidbody.detectCollisions=false;
        }
        foreach(var particle in effect.Particles) {
            particle.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particle.main;main.useUnscaledTime=false;main.playOnAwake=false;
            if(freeze)main.simulationSpace=ParticleSystemSimulationSpace.Local;
            particle.Play(false);particle.Pause(false);
            if(freeze)particle.Simulate(owner.WavePrefabSampleTime,false,false,true);
        }
        if(freeze) {
            float radius=0;
            foreach(var particle in effect.Particles) {
                var renderer=particle.GetComponent<ParticleSystemRenderer>();
                if(renderer==null || renderer.renderMode!=ParticleSystemRenderMode.HorizontalBillboard)continue;
                float measured=RingRadius(particle);
                if(measured>radius){radius=measured;effect.Ring=particle;}
            }
            effect.ReferenceRadius=owner.WavePrefabReferenceRadius>0 ? owner.WavePrefabReferenceRadius : Mathf.Max(.1f,radius);
        }
        foreach(var animator in effect.Animators)animator.enabled=false;
        effects.Add(effect);return effect;
    }
    float RingRadius(ParticleSystem particle) {
        if(particle==null || particle.GetParticles(waveSample)==0)return 0;
        Vector3 scale=particle.transform.lossyScale;
        // Unity 6 horizontal billboards report the quad's diagonal scale. BakeMesh
        // measured a 13.85m UV edge for size*hierarchyScale=19.60m: edge=size/sqrt(2).
        const float quadEdgePerParticleSize=.70710678f;
        return waveSample[0].GetCurrentSize(particle)*quadEdgePerParticleSize*
            Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z))*owner.WaveTextureRingRadius;
    }
    internal float WaveReferenceRadius => travellingWave!=null ? travellingWave.ReferenceRadius : 0;
    internal float WaveVisualRadius => travellingWave!=null ? RingRadius(travellingWave.Ring) : 0;
    void Remove(Effect effect) {
        if(effect==null)return;
        if(effect.Root!=null) {effect.Root.SetActive(false);Object.Destroy(effect.Root);}
        effects.Remove(effect);
    }
    internal void EndEncounter() {
        for(int i=effects.Count-1;i>=0;i--)Remove(effects[i]);
        travellingWave=ritualEffect=null;
        if(boundary!=null)boundary.enabled=false;
        if(tether!=null)tether.enabled=false;
    }
    internal void Reset() {
        EndEncounter();
        if(body!=null)body.gameObject.SetActive(true);
    }
    internal void Changed(FixedGuardianState.Move previous,FixedGuardianState.Move current) {
        if(previous==FixedGuardianState.Move.WaveTravel){Remove(travellingWave);travellingWave=null;}
        if(previous==FixedGuardianState.Move.Ritual){Remove(ritualEffect);ritualEffect=null;}
        var state=owner.State;
        switch(current) {
            case FixedGuardianState.Move.SlamImpact:
                Spawn(owner.SlamImpactPrefab,Ground(state.LockedPoint),Quaternion.identity,2.5f,owner.SlamEffectScale);break;
            case FixedGuardianState.Move.SweepImpact:
                Spawn(owner.SweepPrefab,SweepOrigin(state.SweepSide),
                    owner.transform.rotation,2f,owner.SweepEffectScale);break;
            case FixedGuardianState.Move.WaveTravel:
                travellingWave=Spawn(owner.WavePrefab,Ground(state.WaveOrigin),Quaternion.identity,
                    state.WaveRange/Mathf.Max(.1f,state.Tuning.WaveSpeed)+1,1,true);break;
            case FixedGuardianState.Move.Ritual:
                ritualEffect=Spawn(owner.RitualPrefab,chest.position,Quaternion.identity,state.Tuning.RitualTime+.2f,owner.RitualEffectScale);break;
            case FixedGuardianState.Move.Dead:
                for(int i=effects.Count-1;i>=0;i--)Remove(effects[i]);
                Spawn(owner.DeathPrefab,chest.position,Quaternion.identity,4,owner.DeathEffectScale);
                break;
        }
    }
    internal void TickEffects(float dt) {
        importedArmour?.Tick(dt);
        for(int i=effects.Count-1;i>=0;i--) {
            var effect=effects[i];effect.Remaining-=dt;
            if(effect.Root==null || effect.Remaining<=0){Remove(effect);continue;}
            if(!effect.Frozen) {
                foreach(var particle in effect.Particles)if(particle!=null)particle.Simulate(dt,false,false,true);
                foreach(var animator in effect.Animators)if(animator!=null)animator.Update(dt);
            }
        }
    }
    internal float HitShadow(Ray ray)=>shadow.HitDistance(ray);
    internal void Draw(FixedGuardianFrame frame,float clock) {
        var state=owner.State;
        body.gameObject.SetActive(state.Alive);
        boundary.enabled=false;tether.enabled=false;
        if(!state.Alive){shadow.Clear();shadow.Publish();shadow.Show(false);return;}
        Vector3 direction=frame.Hunter-owner.transform.position;direction.y=0;
        float yaw=direction.sqrMagnitude>.01f ? Vector3.SignedAngle(owner.transform.forward,direction,Vector3.up) : 0;
        torso.localRotation=Quaternion.Euler(0,Mathf.Clamp(yaw,-45,45),0);
        leftAngle=rightAngle=0;
        if(state.Action==FixedGuardianState.Move.SlamTrack || state.Action==FixedGuardianState.Move.SlamLocked)
            rightAngle=-140*Mathf.SmoothStep(0,1,state.Action==FixedGuardianState.Move.SlamTrack ? state.Progress : 1);
        if(state.Action==FixedGuardianState.Move.SlamImpact)rightAngle=-140*(1-state.Progress);
        if(state.Action==FixedGuardianState.Move.SweepTell || state.Action==FixedGuardianState.Move.SweepImpact) {
            float reach=state.Action==FixedGuardianState.Move.SweepTell ? 80*Mathf.SmoothStep(0,1,state.Progress) : 80*(1-state.Progress);
            if(state.SweepSide<0)leftAngle=-reach;else rightAngle=reach;
        }
        leftArm.localRotation=Quaternion.Euler(0,0,leftAngle);
        rightArm.localRotation=Quaternion.Euler(rightAngle<0 ? rightAngle : 0,0,Mathf.Max(0,rightAngle));
        float pulse=state.Action==FixedGuardianState.Move.Ritual || state.Action==FixedGuardianState.Move.WaveTell ?
            1+.2f*Mathf.Sin(clock*9):1;
        chest.localScale=Vector3.one*(.8f*pulse);
        heart.color=state.Dark ? new Color(.62f,.2f,1) : new Color(1,.58f,.19f);
        if(importedArmour!=null) {
            importedArmour.Draw(state,clock,yaw);
            chest.position=importedArmour.ChestPosition;
        }
        DrawShadow(frame);
        if(state.Action==FixedGuardianState.Move.Ritual && state.RitualLamp>=0) {
            tether.enabled=true;tether.widthMultiplier=.045f+.09f*state.Progress;
            tether.startColor=tether.endColor=new Color(.3f,.12f,.38f,1);
            Vector3 target=owner.Encounter.GetLampPosition(state.RitualLamp);
            for(int i=0;i<tether.positionCount;i++) {
                float t=(float)i/(tether.positionCount-1);
                tether.SetPosition(i,Vector3.Lerp(chest.position,target,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*.8f);
            }
            if(ritualEffect!=null && ritualEffect.Root!=null)ritualEffect.Root.transform.position=chest.position;
        }
        if(travellingWave!=null && travellingWave.Root!=null) {
            float scale=Mathf.Max(.05f,state.WaveRadius)/travellingWave.ReferenceRadius;
            // Keep all three axes uniform so the calibrated billboard radius is preserved.
            travellingWave.Root.transform.localScale=travellingWave.Scale*scale;
        }
        if(!owner.ShowAttackBoundaries)return;
        if(state.Action==FixedGuardianState.Move.SlamTrack || state.Action==FixedGuardianState.Move.SlamLocked || state.Action==FixedGuardianState.Move.SlamImpact)
            Circle(state.LockedPoint,state.Tuning.SlamRadius,state.Action==FixedGuardianState.Move.SlamTrack ? new Color(1,.8f,.28f):new Color(1,.34f,.18f));
        else if(state.Action==FixedGuardianState.Move.SweepTell || state.Action==FixedGuardianState.Move.SweepImpact) {
            boundary.enabled=true;boundary.positionCount=5;boundary.loop=false;
            boundary.startColor=boundary.endColor=new Color(1,.42f,.2f);
            float outer=state.SweepSide*frame.ArenaSize.x*.5f,front=frame.ArenaSize.y*.5f;
            Vector3[] corners={frame.ArenaCenter-frame.ArenaForward*front,
                frame.ArenaCenter+frame.ArenaRight*outer-frame.ArenaForward*front,
                frame.ArenaCenter+frame.ArenaRight*outer+frame.ArenaForward*front,
                frame.ArenaCenter+frame.ArenaForward*front,frame.ArenaCenter-frame.ArenaForward*front};
            for(int i=0;i<corners.Length;i++)boundary.SetPosition(i,Ground(corners[i]));
        } else if(state.Action==FixedGuardianState.Move.WaveTell)
            Circle(frame.Origin,1+state.Progress,new Color(.7f,.4f,1));
        else if(state.Action==FixedGuardianState.Move.WaveTravel)
            Circle(state.WaveOrigin,state.WaveRadius,new Color(1,.7f,.35f));
    }
    void Circle(Vector3 center,float radius,Color color) {
        boundary.enabled=true;boundary.positionCount=96;boundary.loop=true;
        boundary.widthMultiplier=owner.State.Action==FixedGuardianState.Move.WaveTravel && travellingWave==null ? owner.Tuning.WaveHalfWidth*2 : .08f;
        boundary.startColor=boundary.endColor=color;
        for(int i=0;i<boundary.positionCount;i++) {
            float angle=i*Mathf.PI*2/boundary.positionCount;
            boundary.SetPosition(i,Ground(center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius));
        }
    }
    void DrawShadow(FixedGuardianFrame frame) {
        bool visible=frame.LitCount>0;
        shadow.Show(visible);
        if(visible)shadowCaster.Draw(frame,owner.ShadowLight,owner.ShadowScale);
    }
    internal void Dispose() {
        Reset();
        Transform shape=root!=null ? root.Find("Guardian ground shadow") : null;
        ShadowProjection.Dispose();
        if(shape!=null)Object.Destroy(shape.GetComponent<MeshFilter>().sharedMesh);
        foreach(var material in new[]{armour,trim,heart,edge,ink})if(material!=null)Object.Destroy(material);
        importedArmour?.Dispose();
        if(root!=null)Object.Destroy(root.gameObject);
    }
}
