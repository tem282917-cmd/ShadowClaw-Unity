using UnityEngine;

// Lifecycle adapter for imported effect assets. Particle and animation clocks
// follow the game clock, including pause, cancelled casts and encounter resets.
public sealed class BossImportedEffects {
    sealed class Instance {
        public GameObject Root;
        public ParticleSystem[] Particles;
        public Animator[] Animators;
        public bool Visible;
        public Vector3 BaseScale;
        public float ReferenceRadius=1;
    }
    readonly Transform parent;
    readonly BossEffectLibrary library;
    readonly Instance[] instances=new Instance[8];
    readonly GameObject[] fallbackPrefabs=new GameObject[8];
    readonly bool[] fallbackResolved=new bool[8];
    HuntBoss.Move previousAction;
    bool previousSweep;

    public BossImportedEffects(Transform parent,BossEffectLibrary library) {
        this.parent=parent;this.library=library;
    }
    GameObject Prefab(int slot) {
        GameObject configured=null;
        if(library) {
            switch(slot) {
                case 0:configured=library.SleeveTellPrefab;break;
                case 1:configured=library.SleeveStrikePrefab;break;
                case 2:configured=library.BellChargePrefab;break;
                case 3:case 4:case 5:configured=library.BellWavePrefab;break;
                case 6:configured=library.RitualPrefab;break;
                case 7:configured=library.DarkHuntPrefab;break;
            }
        }
        if(configured)return configured;
        if(!fallbackResolved[slot]) {
            string[] names={"vfx_Smoke_01","vfx_Impact_01","vfx_Implosion_01","vfx_Shockwave_01",
                "vfx_Shockwave_01","vfx_Shockwave_01","vfx_Portal_01","vfx_Projectile_02"};
            fallbackPrefabs[slot]=Resources.Load<GameObject>("ImportedVFX/FreeQuickEffectsVol1/Prefabs/"+names[slot]);
            fallbackResolved[slot]=true;
        }
        return fallbackPrefabs[slot];
    }
    void Set(int slot,bool visible,Vector3 position,Quaternion rotation,Vector3 scale,float dt,bool restart=false,float sampleTime=-1,float waveRadius=-1) {
        var effect=instances[slot];
        if(!visible) {
            if(effect!=null && effect.Root) {effect.Root.SetActive(false);effect.Visible=false;}
            return;
        }
        if(effect==null || !effect.Root) {
            var prefab=Prefab(slot);
            if(!prefab)return;
            var root=Object.Instantiate(prefab,parent);
            root.name="Imported boss effect / "+prefab.name;
            // Effects are never attack colliders or sources for the light puzzle.
            foreach(var collider in root.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var light in root.GetComponentsInChildren<Light>(true))light.enabled=false;
            effect=new Instance {Root=root,Particles=root.GetComponentsInChildren<ParticleSystem>(true),
                Animators=root.GetComponentsInChildren<Animator>(true),BaseScale=root.transform.localScale};
            foreach(var animator in effect.Animators)animator.enabled=false;
            if(sampleTime>=0)foreach(var ps in effect.Particles) {var main=ps.main;main.simulationSpace=ParticleSystemSimulationSpace.Local;}
            instances[slot]=effect;
        }
        effect.Root.transform.SetPositionAndRotation(position,rotation);
        effect.Root.transform.localScale=Vector3.Scale(effect.BaseScale,scale);
        if(!effect.Visible || restart) {
            effect.Root.SetActive(true);
            foreach(var ps in effect.Particles)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach(var animator in effect.Animators) {animator.Rebind();animator.Update(0);}
            if(sampleTime>=0) {
                foreach(var ps in effect.Particles)ps.Simulate(sampleTime,false,true,false);
                float reference=library ? library.BellWaveReferenceRadius : 0;
                if(reference<=0) {
                    float textureRadius=library ? library.BellWaveTextureRadius : .365f;
                    foreach(var ps in effect.Particles) {
                        var renderer=ps.GetComponent<ParticleSystemRenderer>();
                        if(renderer==null || renderer.renderMode!=ParticleSystemRenderMode.HorizontalBillboard)continue;
                        var particles=new ParticleSystem.Particle[ps.particleCount];
                        int count=ps.GetParticles(particles);
                        float horizontalScale=Mathf.Max(Mathf.Abs(ps.transform.lossyScale.x),Mathf.Abs(ps.transform.lossyScale.z));
                        for(int i=0;i<count;i++)
                            // BakeMesh confirms HorizontalBillboard's size is
                            // its quad diagonal; texture UVs span a side.
                            reference=Mathf.Max(reference,particles[i].GetCurrentSize(ps)*.70710678f*textureRadius*horizontalScale);
                    }
                }
                effect.ReferenceRadius=Mathf.Max(.01f,reference);
            }
        }
        if(dt>0 && sampleTime<0) {
            foreach(var ps in effect.Particles)ps.Simulate(dt,false,!effect.Visible || restart,false);
            foreach(var animator in effect.Animators)animator.Update(dt);
        }
        if(waveRadius>=0) {
            float ratio=Mathf.Max(.01f,waveRadius)/effect.ReferenceRadius;
            // Keep billboard geometry uniformly scaled so its calibrated
            // texture radius remains valid as the ground wave expands.
            effect.Root.transform.localScale=effect.BaseScale*ratio;
        }
        effect.Visible=true;
    }
    public float WaveReferenceRadius(int index) => index>=0 && index<3 && instances[index+3]!=null ? instances[index+3].ReferenceRadius : 0;
    public float WaveVisualRadius(int index) {
        var effect=index>=0 && index<3 ? instances[index+3] : null;
        return effect!=null && effect.Visible && effect.Root ? effect.ReferenceRadius*Mathf.Abs(effect.Root.transform.localScale.x/effect.BaseScale.x) : -1;
    }
    public void Tick(HuntBoss boss,ClawState world,Transform ritual,float dt) {
        var action=boss.Action;
        bool changed=action!=previousAction || boss.SweepRight!=previousSweep;
        Rect area=boss.SweepBounds;
        Vector3 sweep=new Vector3(area.center.x,world.Enemy.y+.08f,area.center.y);
        float width=Mathf.Max(area.width,area.height)/(library ? Mathf.Max(.01f,library.SleeveReferenceWidth) : 8);
        Quaternion direction=Quaternion.Euler(0,boss.SweepRight ? 90 : -90,0);
        Set(0,action==HuntBoss.Move.SleeveTell,sweep,direction,Vector3.one*width,dt,changed);
        Set(1,action==HuntBoss.Move.SleeveSweep,sweep,direction,Vector3.one*width,dt,changed);
        Set(2,action==HuntBoss.Move.BellTell,boss.BellCenter+Vector3.up*.1f,Quaternion.identity,Vector3.one,dt,changed);
        for(int i=0;i<3;i++) {
            float radius=boss.GetBellRadius(i);
            Set(3+i,radius>=0,boss.BellCenter+Vector3.up*.08f,Quaternion.identity,Vector3.one,dt,
                false,library ? library.BellWaveSampleTime : .3f,radius);
        }
        Set(6,action==HuntBoss.Move.Ritual && ritual,ritual ? ritual.position : world.Enemy,Quaternion.identity,Vector3.one,dt,changed);
        Set(7,boss.Pursuing,world.Enemy+Vector3.up*.15f,Quaternion.LookRotation(
            (boss.Destination-world.Enemy).sqrMagnitude>.001f ? boss.Destination-world.Enemy : Vector3.forward),Vector3.one,dt,changed);
        previousAction=action;previousSweep=boss.SweepRight;
    }
    public void Reset() {
        foreach(var effect in instances)if(effect!=null && effect.Root) {
            foreach(var ps in effect.Particles)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.Root.SetActive(false);effect.Visible=false;
        }
        previousAction=HuntBoss.Move.Stalk;
    }
    public void Dispose() {
        foreach(var effect in instances)if(effect!=null && effect.Root)Object.Destroy(effect.Root);
    }
}
