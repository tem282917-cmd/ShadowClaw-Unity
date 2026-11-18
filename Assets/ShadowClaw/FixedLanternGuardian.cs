using System.Collections.Generic;
using UnityEngine;

/// <summary>Author-placed fixed boss. This component never creates encounter locations or lamps.</summary>
[DisallowMultipleComponent]
public sealed class FixedLanternGuardian : ShadowEncounterActor {
    [Header("Activation and authored battle area")]
    public bool StartActive=true;
    public bool StartOnArenaEntry=true;
    [Tooltip("Leaving the authored arena for this long resets the guardian. Zero disables disengagement.")]
    [Min(0)] public float ExitGraceSeconds=3f;
    public Transform ArenaCenter;
    [Tooltip("Authored exit barrier, released as soon as the guardian dies.")]
    public GameObject ExitSeal;
    public Vector2 ArenaSize=new Vector2(24,24);
    [Tooltip("Optional placed lamps. If set, these replace the fallback numerical indices.")]
    public EncounterLantern[] LinkedLanterns;
    [Tooltip("When no explicit lamp objects are assigned, bind the nearest ordinary lamps once per attempt. Never uses the final Boss's six lamps.")]
    public bool AutoLinkNearbyOrdinaryLamps=true;
    [Min(1)] public float LampSearchRadius=24f;
    [Range(1,6)] public int NearbyLampLimit=3;
    public int[] LampIndices={0,1,2};
    [Header("Guardian")]
    [Range(3,16)] public float BodyHeight=7.8f;
    [Range(.25f,3)] public float ShadowScale=1.35f;
    public FixedGuardianTuning Tuning=new FixedGuardianTuning();
    [Header("Original Lamplighter armour and chest lamp")]
    public GameObject BodyPrefab;
    [Tooltip("Optional helmet for a custom body. F01 includes its own helmet.")]
    public GameObject HelmetPrefab;
    public GameObject ChestLanternPrefab;
    [Header("Imported effect prefabs")]
    public GameObject SlamImpactPrefab;
    public GameObject SweepPrefab;
    public GameObject WavePrefab;
    public GameObject RitualPrefab;
    public GameObject DeathPrefab;
    [Min(.1f)] public float SlamEffectScale=2.8f, SweepEffectScale=2.2f, RitualEffectScale=1.5f, DeathEffectScale=3f;
    [Tooltip("Zero measures the imported wave at its frozen pose; otherwise specify its radius at scale one.")]
    [Min(0)] public float WavePrefabReferenceRadius;
    [Range(.02f,.5f)] public float WavePrefabSampleTime=.3f;
    [Tooltip("Swirl01's brightest annulus lies at 0.365 of the particle quad width from its centre.")]
    [Range(.05f,.5f)] public float WaveTextureRingRadius=.365f;
    [Tooltip("Exact hit boundaries for prototype verification. Replace visual presentation with imported effects.")]
    public bool ShowAttackBoundaries=true;

    public FixedGuardianState State {get;private set;}
    public override bool Alive => State==null || State.Alive;
    public float HealthFraction => State==null ? 1 : (float)State.Health/Mathf.Max(1,Tuning.Health);
    public string Tell => State?.Tell ?? "锁灯巨像";
    public int LitLinkedLamps {get;private set;}
    public BossRoomReadability Readability {get;private set;}
    public Vector3 ShadowLight {get;private set;}
    public float WaveVisualReferenceRadius => view?.WaveReferenceRadius ?? 0;
    public float WaveVisualRadius => view?.WaveVisualRadius ?? 0;
    public bool HasImportedArmour => view?.HasImportedArmour ?? false;
    public int LinkedLampCount => links.Count;
    public bool ShadowBurning => shadowFire?.Burning ?? false;
    FixedGuardianShadowFire shadowFire;
    readonly List<int> autoLinks=new List<int>();
    readonly List<int> nearbyCandidates=new List<int>();
    bool autoLinksLocked;
    readonly List<int> links=new List<int>();
    FixedGuardianView view;
    FixedGuardianAudio sounds;
    FixedGuardianState.Move lastAction;
    float visualClock;
    float outsideTime;
    bool configured,encounterEnded;

    void EnsureReady() {
        if(configured || Encounter==null)return;
        configured=true;
        const string effects="ImportedVFX/FreeQuickEffectsVol1/Prefabs/";
        if(SlamImpactPrefab==null)SlamImpactPrefab=Resources.Load<GameObject>(effects+"vfx_Explosion_01");
        if(SweepPrefab==null)SweepPrefab=Resources.Load<GameObject>(effects+"vfx_Impact_01");
        if(WavePrefab==null)WavePrefab=Resources.Load<GameObject>(effects+"vfx_Shockwave_01");
        if(RitualPrefab==null)RitualPrefab=Resources.Load<GameObject>(effects+"vfx_Implosion_01");
        if(DeathPrefab==null)DeathPrefab=Resources.Load<GameObject>(effects+"vfx_Smoke_01");
        if(BodyPrefab==null || (BodyPrefab.name=="KnightCharacter" && BodyPrefab==Resources.Load<GameObject>("Guardian/KnightCharacter")))
            BodyPrefab=Resources.Load<GameObject>("Guardian/F01");
        if(ChestLanternPrefab==null || ((ChestLanternPrefab.name=="ChestLantern" || ChestLanternPrefab.name=="GrandCombatLantern") && ChestLanternPrefab==Resources.Load<GameObject>("Guardian/ChestLantern")))
            ChestLanternPrefab=Resources.Load<GameObject>("Guardian/F02");
        State=new FixedGuardianState(Tuning);
        State.DamageHunter=amount=>Encounter.DamageHunter(amount);
        State.ExtinguishLamp=index=>{
            if(Encounter.TryExtinguishLamp(index))sounds.LampExtinguished(index);
        };
        view=new FixedGuardianView(this);
        sounds=new FixedGuardianAudio(this,view);
        Readability=BossRoomReadability.CreateForGuardian(this);
        lastAction=State.Action;
    }
    void ResolveLinks() {
        links.Clear();
        if(Encounter==null)return;
        if(LinkedLanterns!=null && LinkedLanterns.Length>0) {
            foreach(var lamp in LinkedLanterns) {
                int index=lamp!=null && lamp.AvailableForClaim ? Encounter.GetLampIndex(lamp) : -1;
                if(index>=0 && !links.Contains(index))links.Add(index);
            }
        } else if(AutoLinkNearbyOrdinaryLamps) {
            bool lostLamp=autoLinks.RemoveAll(index=>!Encounter.IsOrdinaryLamp(index))>0;
            if(!autoLinksLocked || lostLamp || autoLinks.Count==0) {
                if(!autoLinksLocked)autoLinks.Clear();
                nearbyCandidates.Clear();
                float radius=Mathf.Max(1,LampSearchRadius);
                for(int index=HuntBoss.LampCapacity;index<Encounter.LampCount;index++) {
                    if(!Encounter.IsOrdinaryLamp(index) || autoLinks.Contains(index))continue;
                    Vector3 delta=Encounter.GetLampPosition(index)-transform.position;
                    if(Mathf.Abs(delta.y)>BodyHeight+3 || delta.x*delta.x+delta.z*delta.z>radius*radius)continue;
                    nearbyCandidates.Add(index);
                }
                nearbyCandidates.Sort((a,b)=>(Encounter.GetLampPosition(a)-transform.position).sqrMagnitude.CompareTo((Encounter.GetLampPosition(b)-transform.position).sqrMagnitude));
                int limit=Mathf.Clamp(NearbyLampLimit,1,6);
                foreach(int index in nearbyCandidates) {
                    if(autoLinks.Count>=limit)break;
                    autoLinks.Add(index);
                }
            }
            links.AddRange(autoLinks);
        } else if(LampIndices!=null) foreach(int index in LampIndices)
            if(index>=0 && index<Encounter.LampCount && !links.Contains(index))links.Add(index);
        LitLinkedLamps=0;ShadowLight=Vector3.zero;
        foreach(int index in links)if(Encounter.IsLampLit(index)) {
            LitLinkedLamps++;ShadowLight+=Encounter.GetLampPosition(index);
        }
        if(LitLinkedLamps>0)ShadowLight/=LitLinkedLamps;
    }
    public FixedGuardianFrame CurrentFrame() {
        ResolveLinks();
        Readability?.SetLampPower(links.Count>0 ? (float)LitLinkedLamps/links.Count : 0);
        Transform arena=ArenaCenter!=null ? ArenaCenter : transform;
        Quaternion yaw=Quaternion.Euler(0,arena.eulerAngles.y,0);
        int next=-1;
        foreach(int index in links)if(Encounter.IsLampLit(index)) {next=index;break;}
        return new FixedGuardianFrame {
            Hunter=Encounter.HunterGroundPosition,JumpHeight=Encounter.HunterJumpHeight,
            Origin=transform.position,ArenaCenter=arena.position,
            ArenaRight=yaw*Vector3.right,ArenaForward=yaw*Vector3.forward,
            ArenaSize=new Vector2(Mathf.Max(4,ArenaSize.x),Mathf.Max(4,ArenaSize.y)),
            LitCount=LitLinkedLamps,LampCount=links.Count,NextLitLamp=next
        };
    }
    [ContextMenu("Begin guardian encounter")]
    public void BeginEncounter() {
        EnsureReady();if(State==null)return;
        var frame=CurrentFrame();if(frame.LampCount==0)return;
        Activated=true;Wake(frame);
    }
    void Wake(FixedGuardianFrame frame) {
        if(State.Action!=FixedGuardianState.Move.Dormant)return;
        autoLinksLocked=true;State.Begin(frame);sounds.Wake();
        Encounter.RequestNarration("13_guardian_entrance");
    }
    public override void ResetEncounter() {
        EnsureReady();if(State==null)return;
        shadowFire?.Clear();sounds.Stop();
        Encounter.CancelGuardianDrop(this);
        Activated=StartActive;encounterEnded=false;autoLinksLocked=false;autoLinks.Clear();visualClock=outsideTime=0;State.Reset();lastAction=State.Action;
        if(ExitSeal!=null)ExitSeal.SetActive(true);
        view.Reset();view.Draw(CurrentFrame(),0);
    }
    public override void Tick(float dt) {
        EnsureReady();if(State==null || !Activated || encounterEnded)return;
        var frame=CurrentFrame();
        if(frame.LampCount==0 && State.Alive && State.Action!=FixedGuardianState.Move.Dormant) {
            // Disabled/deleted authored lights must not strand an invulnerable
            // encounter. Retrying after replacement lights arrive is safe.
            State.Reset();autoLinksLocked=false;visualClock=outsideTime=0;
            lastAction=State.Action;view.Reset();sounds.Stop();
        }
        if(State.Action!=FixedGuardianState.Move.Dormant && State.Alive && ExitGraceSeconds>0) {
            outsideTime=frame.InsideArena ? 0 : outsideTime+dt;
            if(outsideTime>=ExitGraceSeconds){ResetEncounter();return;}
        }
        if(StartOnArenaEntry && frame.InsideArena && frame.LampCount>0 && State.Action==FixedGuardianState.Move.Dormant) {
            Wake(frame);
        }
        var previous=State.Action;
        bool wasDark=State.Dark;
        sounds.Tick(dt);
        State.Tick(dt,frame);
        // Lethal damage can synchronously end the encounter from inside State.Tick.
        if(encounterEnded)return;
        if(wasDark && !State.Dark && State.Action==FixedGuardianState.Move.Recover)sounds.Stagger();
        if(previous==FixedGuardianState.Move.Ritual && State.Action!=previous)frame=CurrentFrame();
        visualClock+=dt;
        if(lastAction!=State.Action) {
            view.Changed(lastAction,State.Action);
            sounds.MoveChanged(State.Action);
            if(State.Action==FixedGuardianState.Move.SlamImpact || State.Action==FixedGuardianState.Move.SweepImpact ||
               State.Action==FixedGuardianState.Move.WaveTravel)Encounter.ShakeOnImpact();
            if(State.Action==FixedGuardianState.Move.Ritual)Encounter.RequestNarration("14_guardian_trial");
            lastAction=State.Action;
        }
        view.TickEffects(dt);
        view.Draw(frame,visualClock);
        if(shadowFire!=null) {
            if(!CanHitShadow)shadowFire.Clear();
            else {
                int damage=shadowFire.Step(dt);
                if(damage>0)ApplyShadowDamage(damage);
            }
        }
    }
    bool CanHitShadow => !encounterEnded && Activated && State!=null && State.Alive && !State.Dark &&
        State.Action!=FixedGuardianState.Move.Dormant && LitLinkedLamps>0;
    public override bool TryHitShadow(Ray ray,float maxDistance,out float distance) {
        distance=float.PositiveInfinity;
        if(!CanHitShadow)return false;
        distance=view.HitShadow(ray);
        return distance<=maxDistance;
    }
    public override void ReceiveShadowHit(Bolt kind,Vector3 point) {
        if(!CanHitShadow)return;
        if(kind==Bolt.Fire){TryIgniteShadow(point);return;}
        // The whole visible silhouette is vulnerable. The chest lantern is never a damage entry.
        ApplyShadowDamage(kind==Bolt.Holy ? 12 : kind==Bolt.Light ? 8 : 10);
    }
    public bool TryIgniteShadow(Vector3 contact) {
        if(!CanHitShadow)return false;
        ResolveLinks();
        Vector2 hit=new Vector2(contact.x,contact.z);
        var projection=view.ShadowProjection;
        if(LitLinkedLamps==0 || projection==null || !projection.Contains(hit))return false;
        if(shadowFire==null)shadowFire=new FixedGuardianShadowFire(this,projection);
        shadowFire.Ignite(hit);
        Encounter.RequestNarration("19_guardian_shadow_fire");
        return true;
    }
    void ApplyShadowDamage(int damage) {
        int before=State.Health;
        State.ReceiveDamage(damage);
        if(State.Health==before)return;
        if(!State.Alive) {
            if(ExitSeal!=null)ExitSeal.SetActive(false);
            shadowFire?.Clear();
            sounds.MoveChanged(State.Action);
            if(State.Action==FixedGuardianState.Move.SlamImpact || State.Action==FixedGuardianState.Move.SweepImpact ||
               State.Action==FixedGuardianState.Move.WaveTravel)Encounter.ShakeOnImpact();
            Encounter.RequestNarration("15_guardian_defeated");
            Encounter.DropGuardianRelic(this);
            view.Changed(lastAction,State.Action);lastAction=State.Action;
            view.Draw(CurrentFrame(),visualClock);
        } else sounds.Hurt();
    }
    public override void OnLampShot(int index) {
        if(State==null)return;
        ResolveLinks();
        bool interrupt=State.Dark || (State.Action==FixedGuardianState.Move.Ritual && State.RitualLamp==index);
        bool linked=links.Contains(index);
        State.LampShot(index,linked);
        if(linked && interrupt && State.Action==FixedGuardianState.Move.Recover)sounds.Stagger();
    }
    public override void OnEncounterEnded() {encounterEnded=true;shadowFire?.Clear();view?.EndEncounter();sounds?.Stop();}
    protected override void OnActivationChanged(bool value) {if(!value){shadowFire?.Clear();sounds?.Stop();}}
    protected override void OnDisable() {shadowFire?.Clear();sounds?.Stop();base.OnDisable();}
    void OnDestroy() {view?.Dispose();sounds?.Dispose();}
    void OnDrawGizmosSelected() {
        Transform arena=ArenaCenter!=null ? ArenaCenter : transform;
        var previous=Gizmos.matrix;
        Gizmos.matrix=Matrix4x4.TRS(arena.position,Quaternion.Euler(0,arena.eulerAngles.y,0),Vector3.one);
        Gizmos.color=new Color(.8f,.6f,1,.7f);Gizmos.DrawWireCube(Vector3.zero,new Vector3(ArenaSize.x,.08f,ArenaSize.y));
        Gizmos.matrix=previous;
        Gizmos.color=new Color(.7f,.7f,.9f,.8f);Gizmos.DrawWireCube(transform.position+Vector3.up*BodyHeight*.5f,new Vector3(3,BodyHeight,2));
    }
}
