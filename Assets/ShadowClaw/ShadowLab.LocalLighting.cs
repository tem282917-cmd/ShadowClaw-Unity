using System;
using System.Collections.Generic;
using UnityEngine;
using Chapter01.Rendering;

// Ordinary fire fixtures are adapted only at runtime; no authored scene placement is changed.
public sealed partial class ShadowLab
{
    readonly Dictionary<int,FireThiefAcolyte> ordinaryLampClaims=new Dictionary<int,FireThiefAcolyte>();
    readonly HashSet<int> inspectedSceneLights=new HashSet<int>();
    readonly Dictionary<int,EncounterLantern> ordinaryFixtureAdapters=new Dictionary<int,EncounterLantern>();
    float localLightDiscoveryClock;

    public void RefreshOrdinarySceneLamps()
    {
        if(!GameplayReady)return;
        var budgets=FindObjectsByType<Chapter01PracticalLightBudget>(FindObjectsSortMode.None);
        Renderer[] surfaces=null;Collider[] fixtureColliders=null;
        foreach(var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)) {
            if(light==null || light.gameObject.scene!=gameObject.scene ||
                (light.type!=LightType.Point && light.type!=LightType.Spot) || IsBossLight(light) ||
                light.GetComponentInParent<ShadowEncounterActor>()!=null ||
                light.GetComponentInParent<ShadowDemonView>()!=null || light.GetComponentInParent<GrandCombatLantern>()!=null ||
                light.GetComponentInParent<BossRoomReadability>()!=null ||
                light.GetComponentInParent<HunterWeaponView>()!=null ||
                light.GetComponentInParent<ParticleSystem>()!=null ||
                light.GetComponentInParent<Camera>()!=null)continue;
            var authored=light.GetComponentInParent<EncounterLantern>();
            if(authored!=null) {RegisterLantern(authored);continue;}
            foreach(var registered in authoredLanterns)
                if(registered!=null && registered.ControlsLight(light)) {authored=registered;break;}
            if(authored!=null)continue;
            if(inspectedSceneLights.Contains(light.GetInstanceID()))continue;
            if(light.intensity<=0 || light.range<=0)continue;
            string name=light.name;
            if(name.IndexOf("Window",StringComparison.OrdinalIgnoreCase)>=0 ||
               name.IndexOf("Spill",StringComparison.OrdinalIgnoreCase)>=0 ||
               name.IndexOf("Fill",StringComparison.OrdinalIgnoreCase)>=0)continue;
            var fixture=FindOrdinaryFixture(light,ref surfaces);
            if(fixture==null)continue;
            inspectedSceneLights.Add(light.GetInstanceID());
            bool budgeted=false;
            foreach(var budget in budgets)
                if(Array.IndexOf(budget.lights,light)>=0) {budgeted=true;break;}
            if(!light.transform.IsChildOf(fixture.transform) && !fixture.transform.IsChildOf(light.transform))
                light.transform.SetParent(fixture.transform,true);
            if(ordinaryFixtureAdapters.TryGetValue(fixture.GetInstanceID(),out var fixtureAdapter) && fixtureAdapter!=null) {
                fixtureAdapter.AddSupportingLight(light,budgeted);
                continue;
            }
            // The budget's disabled bit is a rendering decision, not an extinguished lamp.
            bool lit=budgeted || light.enabled;
            var host=new GameObject("Ordinary combat wick - "+fixture.name);
            host.SetActive(false);host.transform.SetParent(fixture.transform,true);
            host.transform.position=light.transform.position;
            var lamp=host.AddComponent<EncounterLantern>();
            lamp.Encounter=this;lamp.Wick=host.transform;lamp.EffectiveLight=light;
            lamp.StartsLit=lit;lamp.UseExternalLightBudget=budgeted;lamp.RuntimeSceneAdapter=true;
            lamp.VisualSurfaces=new[]{fixture};lamp.Lifetime=32;lamp.TargetRadius=.5f;
            if(fixtureColliders==null)fixtureColliders=FindObjectsByType<Collider>(FindObjectsSortMode.None);
            lamp.BindFixtureColliders(fixture,fixtureColliders);
            ordinaryFixtureAdapters[fixture.GetInstanceID()]=lamp;
            host.SetActive(true);
        }
    }
    bool IsBossLight(Light light) => hall!=null && Array.IndexOf(hall.GalleryLights ?? Array.Empty<Light>(),light)>=0;
    static Renderer FindOrdinaryFixture(Light light,ref Renderer[] surfaces)
    {
        const string prefix="WarmPool__";
        if(light.name.StartsWith(prefix,StringComparison.Ordinal)) {
            string fixtureName=light.name.Substring(prefix.Length);
            if(fixtureName.StartsWith("D02__Six_Combat_Lantern_",StringComparison.Ordinal))return null;
            // An authored fixture owns its light even after it is renamed or duplicated.
            var owner=light.GetComponentInParent<MeshRenderer>();
            if(owner!=null)return owner;
            if(surfaces==null)surfaces=FindObjectsByType<Renderer>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
            Renderer closest=null;float nearest=16;
            foreach(var renderer in surfaces) {
                if(renderer.gameObject.scene!=light.gameObject.scene || !renderer.enabled ||
                    (renderer.name!=fixtureName && !renderer.name.StartsWith(fixtureName+"__",StringComparison.Ordinal)))continue;
                float distance=renderer.bounds.SqrDistance(light.transform.position);
                if(distance<nearest) {nearest=distance;closest=renderer;}
            }
            return closest;
        }
        // User-added lamp prefabs can opt in through their visible fixture hierarchy.
        // Unnamed point-light fill and window lighting never become floating targets.
        for(Transform root=light.transform;root!=null;root=root.parent) {
            string name=root.name.ToLowerInvariant();
            bool named=name.Contains("lantern") || name.Contains("lamp") || name.Contains("torch") ||
                name.Contains("candle") || name.Contains("beacon") || name.Contains("灯") || name.Contains("火炬");
            if(named) {
                Renderer closest=null;float nearest=9;
                foreach(var renderer in root.GetComponentsInChildren<Renderer>()) {
                    if(renderer is ParticleSystemRenderer || renderer.GetComponentInParent<ShadowEncounterActor>()!=null ||
                        renderer.GetComponentInParent<EncounterLantern>()!=null || !renderer.enabled)continue;
                    float distance=renderer.bounds.SqrDistance(light.transform.position);
                    if(distance<nearest) {nearest=distance;closest=renderer;}
                }
                if(closest!=null)return closest;
            }
            // Do not infer a fixture from an entire scene or building container.
            if(root!=light.transform && root.childCount>12)break;
        }
        return null;
    }
    void TickOrdinaryLampDiscovery(float dt) {
        localLightDiscoveryClock-=dt;
        if(localLightDiscoveryClock>0)return;
        localLightDiscoveryClock=1;
        RefreshOrdinarySceneLamps();
    }
    public bool IsOrdinaryLamp(int index) {
        var lamp=AuthoredLamp(index);
        return lamp!=null && lamp.AvailableForClaim;
    }
    public int ClaimNearestOrdinaryLamp(FireThiefAcolyte owner,Vector3 position,float radius,int previous=-1) {
        if(owner==null || !owner.isActiveAndEnabled || !owner.Activated || !owner.Alive) {
            ReleaseOrdinaryLamp(owner);return -1;
        }
        radius=Mathf.Max(0,radius);float limit=radius*radius;
        bool Within(int index) => IsOrdinaryLamp(index) && (GetLampPosition(index)-position).sqrMagnitude<=limit;
        if(previous>=0 && Within(previous) && ordinaryLampClaims.TryGetValue(previous,out var current) && current==owner)return previous;
        ReleaseOrdinaryLamp(owner);
        int selected=-1;float nearest=float.PositiveInfinity;
        for(int i=0;i<authoredLanterns.Count;i++) {
            int index=(lamps?.Length ?? 0)+i;
            if(!Within(index))continue;
            if(ordinaryLampClaims.TryGetValue(index,out var claimant) && claimant!=null && claimant!=owner &&
                claimant.isActiveAndEnabled && claimant.Activated && claimant.Alive)continue;
            float distance=(GetLampPosition(index)-position).sqrMagnitude;
            if(distance<nearest) {selected=index;nearest=distance;}
        }
        if(selected>=0) {
            AuthoredLamp(selected).SetFireTheft(false);
            ordinaryLampClaims[selected]=owner;
        }
        return selected;
    }
    public void ReleaseOrdinaryLamp(FireThiefAcolyte owner) {
        if(owner==null)return;
        int remove=-1;
        foreach(var pair in ordinaryLampClaims)if(pair.Value==owner) {remove=pair.Key;break;}
        if(remove>=0) {
            ordinaryLampClaims.Remove(remove);
            var lamp=AuthoredLamp(remove);
            if(lamp!=null)lamp.SetFireTheft(false);
        }
    }
    internal void DisplayLampTheft(int index,FireThiefAcolyte owner,bool active) {
        if(!ordinaryLampClaims.TryGetValue(index,out var claimant) || claimant!=owner)return;
        var lamp=AuthoredLamp(index);
        if(lamp!=null)lamp.SetFireTheft(active);
    }
    public FireThiefAcolyte OrdinaryLampClaimant(int index) => ordinaryLampClaims.TryGetValue(index,out var owner) &&
        owner!=null && owner.isActiveAndEnabled && owner.Activated && owner.Alive ? owner : null;

    public MinorShadowLighting GetMinorShadowLighting(Vector3 foot,float searchRadius) =>
        GetMinorShadowLightingAt(foot+Vector3.up,searchRadius);

    public MinorShadowLighting GetMinorShadowLightingAt(Vector3 sample,float searchRadius)
    {
        int selected=-1;float nearest=float.PositiveInfinity;
        for(int i=0;i<authoredLanterns.Count;i++) {
            var lamp=authoredLanterns[i];
            if(lamp==null || !lamp.ProjectsAt(sample,searchRadius))continue;
            float distance=(lamp.Position-sample).sqrMagnitude;
            if(distance<nearest) {nearest=distance;selected=i;}
        }
        if(selected>=0)return new MinorShadowLighting {
            IsLocal=true,Position=authoredLanterns[selected].Position,LampIndex=(lamps?.Length ?? 0)+selected
        };
        Light sky=hall!=null ? hall.Moonlight : null;
        if(sky==null || sky.type!=LightType.Directional || !sky.isActiveAndEnabled || sky.intensity<=0)sky=RenderSettings.sun;
        Vector3 direction=sky!=null && sky.type==LightType.Directional && sky.isActiveAndEnabled && sky.intensity>0
            ? sky.transform.forward : new Vector3(.35f,-.8f,.48f).normalized;
        return new MinorShadowLighting {IsLocal=false,Direction=direction,LampIndex=-1};
    }
}

public struct MinorShadowLighting
{
    public bool IsLocal;
    public int LampIndex;
    public float ProjectionHeight;
    public Vector3 Position,Direction;
    public Vector2 Project(Vector3 foot,Vector3 offset,float scale) => IsLocal
        ? MinorEnemyRules.ProjectLocal(foot,offset,Position,scale,ProjectionHeight)
        : MinorEnemyRules.ProjectSky(foot,offset,Direction,scale);
}
