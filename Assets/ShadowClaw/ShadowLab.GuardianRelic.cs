using UnityEngine;

public sealed partial class ShadowLab {
    public const int GuardianRelicCapacity=3, GuardianRelicHealing=30;
    public bool HasGuardianRelic {get;private set;}
    public int GuardianRelicCharges {get;private set;}
    public int GuardianRelicRelights {get;private set;}
    public GameObject GuardianRelicDrop {get;private set;}
    public ParticleSystem GuardianHealingEffect {get;private set;}
    ParticleSystem dropBeacon;
    FixedLanternGuardian relicOwner;
    float healAge;
    float firstBossWoundHintRemaining;

    internal string FirstBossWoundHint {
        get {
            if(firstBossWoundHintRemaining<=0 || finished || HunterHealth<=0 || HunterHealth==HuntBoss.HunterMaxHealth)return null;
            if(!HasGuardianRelic)return "尚未获得守灯者灯心 · 避开伤害 5 秒后会自动回血";
            if(GuardianRelicCharges>0)return "按 Q 使用守灯者灯心，立即恢复 30 点生命";
            return GuardianRelicRelights==0 ? "灯心已耗尽 · 再重燃两盏灯可恢复 1 次充能" :
                "灯心已耗尽 · 再重燃一盏灯可恢复 1 次充能";
        }
    }
    void ShowFirstBossWoundHint() {
        if(!EncounterStarted || HunterHealth<=0)return;
        boss.HunterDamaged-=ShowFirstBossWoundHint;
        firstBossWoundHintRemaining=5f;
        RequestNarration("21_first_boss_wound");
    }

    internal void DropGuardianRelic(FixedLanternGuardian guardian) {
        if(!GameplayReady || finished || HasGuardianRelic || GuardianRelicDrop!=null)return;
        Vector3 foot=ReachableDropPosition(guardian.transform.position);
        GuardianRelicDrop=new GameObject("Keeper's lantern heart");
        GuardianRelicDrop.transform.SetParent(transform,true);
        GuardianRelicDrop.transform.position=foot+Vector3.up*.08f;
        relicOwner=guardian;
        var source=guardian.ChestLanternPrefab;
        if(source!=null) {
            var cage=Instantiate(source,GuardianRelicDrop.transform,false);
            FixedGuardianArmour.PrepareDecoration(cage,.55f,true);
        }
        dropBeacon=RelicParticles("vfx_LootDrop_01",GuardianRelicDrop.transform,.65f);
        UI?.ShowRelicFeedback("守灯者留下了灯心 · 靠近光柱拾取");
    }
    Vector3 ReachableDropPosition(Vector3 guardian) {
        // Trace a walkable route towards the plinth from the player's current
        // floor. The last clear spot is usable even if stairs or a rail intervene.
        Vector3 last=HunterGroundPosition, start=last;
        Vector3 delta=Vector3.ProjectOnPlane(guardian-start,Vector3.up);
        float length=Mathf.Max(0,delta.magnitude-3.2f);
        Vector3 forward=delta.normalized;
        for(float step=.4f;step<=length+.39f;step+=.4f) {
            Vector3 p=start+forward*Mathf.Min(step,length);
            p.y=last.y;
            if(!Physics.Raycast(p+Vector3.up*.55f,Vector3.down,out var floor,1.05f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) || floor.normal.y<.7f)break;
            p.y=floor.point.y;
            if(Mathf.Abs(p.y-last.y)>.35f)break;
            bool obstructed=false;
            foreach(var obstacle in Physics.OverlapCapsule(p+Vector3.up*.43f,p+Vector3.up*1.42f,.3f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) {
                if(obstacle!=playerBody){obstructed=true;break;}
            }
            if(obstructed)break;
            last=p;
        }
        return last;
    }
    internal void CancelGuardianDrop(FixedLanternGuardian guardian) {
        if(guardian==relicOwner)ClearDrop();
    }
    void ClearDrop() {
        if(GuardianRelicDrop!=null){GuardianRelicDrop.SetActive(false);Destroy(GuardianRelicDrop);}
        GuardianRelicDrop=null;dropBeacon=null;relicOwner=null;
    }
    void ResetGuardianRelic() {
        ClearDrop();ClearHealingEffect();
        firstBossWoundHintRemaining=0;
        HasGuardianRelic=false;GuardianRelicCharges=GuardianRelicRelights=0;
    }
    void OnPlayerRelitLamp() {
        GrantHolyKnifeFromLamp();
        if(!HasGuardianRelic || GuardianRelicCharges>=GuardianRelicCapacity)return;
        if(++GuardianRelicRelights<2)return;
        GuardianRelicRelights=0;GuardianRelicCharges++;
    }
    public bool UseGuardianRelic() {
        if(GameplayPaused || !HasGuardianRelic || GuardianRelicCharges<=0 || !boss.RestoreHunterHealth(GuardianRelicHealing))return false;
        GuardianRelicCharges--;healAge=0;firstBossWoundHintRemaining=0;
        if(GuardianHealingEffect==null)GuardianHealingEffect=RelicParticles("vfx_Heal_01",transform,.48f);
        if(GuardianHealingEffect!=null) {
            PositionHealingEffect();
            GuardianHealingEffect.Simulate(.04f,true,true,false);
        }
        return true;
    }
    static ParticleSystem RelicParticles(string name,Transform parent,float scale) {
        var source=Resources.Load<GameObject>("ImportedVFX/FreeQuickEffectsVol1/Prefabs/"+name);
        if(source==null)return null;
        var obj=Instantiate(source,parent,false);
        obj.transform.localScale*=scale;
        foreach(var light in obj.GetComponentsInChildren<Light>(true))light.enabled=false;
        foreach(var collider in obj.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        var root=obj.GetComponent<ParticleSystem>();
        root.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        root.Simulate(.02f,true,true,false);
        return root;
    }
    void PositionHealingEffect() {
        // Lift the imported burst into the lower part of the first-person view.
        GuardianHealingEffect.transform.position=HunterBodyPosition+Vector3.up*.94f+eye.transform.forward*.85f;
    }
    void ClearHealingEffect() {
        if(GuardianHealingEffect!=null){GuardianHealingEffect.gameObject.SetActive(false);Destroy(GuardianHealingEffect.gameObject);}
        GuardianHealingEffect=null;
    }
    void TickGuardianRelic(float dt) {
        firstBossWoundHintRemaining=Mathf.Max(0,firstBossWoundHintRemaining-dt);
        if(GuardianRelicDrop!=null) {
            dropBeacon?.Simulate(dt,true,false,false);
            Vector3 foot=GuardianRelicDrop.transform.position;
            if((HunterGroundPosition-foot).sqrMagnitude<=1.45f*1.45f &&
                !Physics.Linecast(HunterGroundPosition+Vector3.up*.65f,foot+Vector3.up*.45f,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) {
                ClearDrop();HasGuardianRelic=true;GuardianRelicCharges=GuardianRelicCapacity;
                UI?.ShowRelicFeedback("获得守灯者灯心 · Q 恢复生命，重燃两盏灯恢复一次充能");
                RequestNarration(FairyTaleNarrator.GuardianRelicPickupId);
            }
        }
        if(GuardianHealingEffect!=null) {
            PositionHealingEffect();
            GuardianHealingEffect.Simulate(dt,true,false,false);
            healAge+=dt;
            if(healAge>=2.5f)ClearHealingEffect();
        }
    }
}
