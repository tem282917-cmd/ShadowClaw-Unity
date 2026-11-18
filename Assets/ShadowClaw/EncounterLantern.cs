using UnityEngine;

/// <summary>A lamp placed by the level author. Purple fire is a status marker only.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class EncounterLantern : MonoBehaviour
{
    public ShadowLab Encounter;
    public Transform Wick;
    public Light EffectiveLight;
    public Light[] SupportingLights=System.Array.Empty<Light>();
    [Tooltip("Set for practical lights controlled by a distance budget. Budget culling does not change gameplay lamp state.")]
    public bool UseExternalLightBudget;
    [HideInInspector] public bool RuntimeSceneAdapter;
    public Renderer[] VisualSurfaces=System.Array.Empty<Renderer>();
    [Tooltip("Optional lamp housing colliders. Hits on these belong to this lamp; unrelated walls still block arrows.")]
    public Collider[] InteractionSurfaces=System.Array.Empty<Collider>();
    public bool StartsLit=true;
    public bool DecayBeforeInteraction;
    [Min(4)] public float Lifetime=32;
    [Min(.1f)] public float TargetRadius=.5f;
    public float Remaining { get; private set; }
    public bool Lit => Remaining>0;
    public Vector3 Position => RuntimeSceneAdapter && EffectiveLight!=null ? EffectiveLight.transform.position :
        Wick!=null ? Wick.position : transform.position;
    public bool AvailableForClaim => isActiveAndEnabled && gameObject.activeInHierarchy &&
        (EffectiveLight==null || EffectiveLight.gameObject.activeInHierarchy &&
        (UseExternalLightBudget || !Lit || EffectiveLight.enabled));
    public float Strength => !Lit ? 0 : Remaining>Lifetime*.5f ? 1 : Remaining>Lifetime/6 ? .55f : .25f;
    public Collider Target { get; private set; }
    LanternFire flame;
    float baseIntensity;
    bool interacted,fireTheft;
    readonly System.Collections.Generic.Dictionary<Light,Color> originalBulbColors=new System.Collections.Generic.Dictionary<Light,Color>();
    static readonly Color VioletGlow=new Color(.75f,.075f,2f);
    static readonly Color VioletLight=new Color(.68f,.23f,1f);
    struct GlowSurface {
        public Renderer renderer;
        public int slot;
        public Color emission;
        public Color? flameColor;
        public bool recolorGlassMask;
    }
    readonly System.Collections.Generic.List<GlowSurface> glowSurfaces=new System.Collections.Generic.List<GlowSurface>();
    float lastSurfaceStrength=-1;
    MaterialPropertyBlock emission;
    readonly System.Collections.Generic.Dictionary<Light,float> supportingIntensity=new System.Collections.Generic.Dictionary<Light,float>();
    public bool ControlsLight(Light light) => EffectiveLight==light || System.Array.IndexOf(SupportingLights ?? System.Array.Empty<Light>(),light)>=0;
    public bool OwnsInteractionCollider(Collider collider) => collider!=null && isActiveAndEnabled &&
        (collider==Target || System.Array.IndexOf(InteractionSurfaces ?? System.Array.Empty<Collider>(),collider)>=0);
    public void BindFixtureColliders(Renderer fixture,Collider[] candidates) {
        if(fixture==null)return;
        var bound=new System.Collections.Generic.List<Collider>();
        string key=fixture.name;
        int suffix=key.LastIndexOf("__",System.StringComparison.Ordinal);
        if(suffix>=0 && int.TryParse(key.Substring(suffix+2),out _))key=key.Substring(0,suffix);
        Bounds bounds=fixture.bounds;bounds.Expand(.3f);
        foreach(var collider in candidates) {
            if(collider==null || collider.isTrigger || !collider.enabled || collider.gameObject.scene!=gameObject.scene)continue;
            bool owned=collider.transform==fixture.transform || collider.transform.IsChildOf(fixture.transform) ||
                collider.name.StartsWith("CW_Obstacle_Authored_"+key+"_Section_",System.StringComparison.Ordinal) ||
                collider.name=="Collision__"+fixture.name || collider.name=="Collision__"+key;
            if(owned && bounds.Intersects(collider.bounds))bound.Add(collider);
        }
        InteractionSurfaces=bound.ToArray();
    }
    public void AddSupportingLight(Light light,bool budgeted) {
        if(light==null || ControlsLight(light))return;
        var lights=new System.Collections.Generic.List<Light>(SupportingLights ?? System.Array.Empty<Light>()) {light};
        SupportingLights=lights.ToArray();supportingIntensity[light]=light.intensity;
        if(fireTheft)TintStolenBulb(light);
        UseExternalLightBudget|=budgeted;
        if(!Lit)light.enabled=false;
        Refresh();
    }
    void SetLightEnabled(bool value) {
        if(EffectiveLight!=null)EffectiveLight.enabled=value;
        foreach(var light in SupportingLights ?? System.Array.Empty<Light>())if(light!=null)light.enabled=value;
    }
    internal void SetFireTheft(bool active) {
        if(fireTheft==active)return;
        fireTheft=active;
        if(active) {
            TintStolenBulb(EffectiveLight);
            foreach(var light in SupportingLights ?? System.Array.Empty<Light>())TintStolenBulb(light);
        } else {
            foreach(var bulb in originalBulbColors)if(bulb.Key!=null)bulb.Key.color=bulb.Value;
            originalBulbColors.Clear();
        }
        Refresh();
    }
    void TintStolenBulb(Light light) {
        if(light==null || originalBulbColors.ContainsKey(light))return;
        originalBulbColors.Add(light,light.color);
        light.color=VioletLight;
    }

    void OnEnable() {
        if(Encounter==null) Encounter=FindFirstObjectByType<ShadowLab>();
        Encounter?.RegisterLantern(this);
        if(flame!=null) {SetLightEnabled(Lit);Refresh();}
    }
    void Start() {
        if(Encounter==null) Encounter=FindFirstObjectByType<ShadowLab>();
        Encounter?.RegisterLantern(this);
    }
    void OnDisable() {
        Encounter?.UnregisterLantern(this);flame?.SetStrength(0);
        SetLightEnabled(false);
    }
    public void ResetLantern() {
        if(flame==null) {
            Transform wick=Wick!=null ? Wick : transform;
            flame=LanternFire.Create(wick);
            var target=wick.gameObject.AddComponent<SphereCollider>();
            target.isTrigger=true; target.radius=TargetRadius;
            Target=target;
            if(!RuntimeSceneAdapter && (InteractionSurfaces==null || InteractionSurfaces.Length==0)) {
                var surfaces=new System.Collections.Generic.List<Collider>();
                foreach(var collider in GetComponentsInChildren<Collider>())
                    if(collider!=target && !collider.isTrigger)surfaces.Add(collider);
                InteractionSurfaces=surfaces.ToArray();
            }
            if(EffectiveLight!=null) baseIntensity=EffectiveLight.intensity;
            foreach(var light in SupportingLights ?? System.Array.Empty<Light>())
                if(light!=null && !supportingIntensity.ContainsKey(light))supportingIntensity[light]=light.intensity;
            VisualSurfaces=VisualSurfaces ?? System.Array.Empty<Renderer>();
            foreach(var surface in VisualSurfaces) {
                if(surface==null)continue;
                CacheGlow(surface,false);
                // The imported chapel candle has a wax holder and a separate visible flame mesh.
                foreach(Transform child in surface.transform)
                    if(child.TryGetComponent<MeshRenderer>(out var mesh) && System.Array.IndexOf(VisualSurfaces,mesh)<0) {
                        flame.ScaleImportedCandleFlame(mesh);
                        CacheGlow(mesh,true);
                    }
            }
        }
        Remaining=StartsLit ? Lifetime : 0;interacted=false;
        SetFireTheft(false);
        SetLightEnabled(Lit);
        Refresh();
    }
    public void Step(float dt) { if(DecayBeforeInteraction || interacted)Remaining=Mathf.Max(0,Remaining-dt);Refresh(); }
    public void Ignite() { interacted=true;Remaining=Lifetime;SetLightEnabled(true);Refresh(); }
    public void Extinguish() { interacted=true;Remaining=0;Refresh(); }
    void CacheGlow(Renderer surface,bool onlyFlame) {
        var materials=surface.sharedMaterials;
        for(int slot=0;slot<materials.Length;slot++) {
            var material=materials[slot];
            if(material==null || !material.HasProperty("_EmissionColor"))continue;
            bool isFlame=material.name=="Night_Candle_Flame" || surface.name.StartsWith("Candle_Flame__",System.StringComparison.Ordinal);
            if(onlyFlame && !isFlame)continue;
            Color glow=material.GetColor("_EmissionColor");
            if(glow.maxColorComponent<=.001f)continue;
            glowSurfaces.Add(new GlowSurface {renderer=surface,slot=slot,emission=glow,
                recolorGlassMask=material.HasProperty("_VioletWick"),
                flameColor=isFlame && material.HasProperty("_Color") ? material.GetColor("_Color") : (Color?)null});
        }
    }
    void Refresh() {
        if(RuntimeSceneAdapter && EffectiveLight!=null && Wick!=null)Wick.position=EffectiveLight.transform.position;
        float visibleStrength=fireTheft ? 0 : Strength;
        flame?.SetStrength(visibleStrength);
        if(EffectiveLight!=null) {
            if(!Lit)EffectiveLight.enabled=false;
            EffectiveLight.intensity=baseIntensity*Strength;
        }
        foreach(var light in SupportingLights ?? System.Array.Empty<Light>())if(light!=null) {
            if(!Lit)light.enabled=false;
            if(supportingIntensity.TryGetValue(light,out float intensity))light.intensity=intensity*Strength;
        }
        if(glowSurfaces.Count==0 || lastSurfaceStrength==visibleStrength)return;
        lastSurfaceStrength=visibleStrength;
        if(emission==null)emission=new MaterialPropertyBlock();
        foreach(var surface in glowSurfaces) {
            if(surface.renderer==null)continue;
            surface.renderer.GetPropertyBlock(emission,surface.slot);
            Color dormantGlow=surface.recolorGlassMask || surface.flameColor.HasValue ? VioletGlow : new Color(.16f,.015f,.32f);
            bool violet=visibleStrength==0;
            if(surface.recolorGlassMask)emission.SetFloat("_VioletWick",violet ? 1 : 0);
            emission.SetColor("_EmissionColor",violet ? dormantGlow : surface.emission*visibleStrength);
            if(surface.flameColor.HasValue)
                emission.SetColor("_Color",violet ? new Color(.48f,.10f,.85f,surface.flameColor.Value.a) : surface.flameColor.Value);
            surface.renderer.SetPropertyBlock(emission,surface.slot);
        }
    }
    void LateUpdate() {
        // A renderer-only budget can turn the original Light back on after Tick.
        // Extinction remains authoritative without defeating culling of lit fixtures.
        if(!Lit)SetLightEnabled(false);
    }
    public bool ProjectsAt(Vector3 position,float radius) {
        if(!isActiveAndEnabled || !Lit)return false;
        float maximum=Mathf.Max(0,radius);
        if(EffectiveLight!=null) {
            if(!EffectiveLight.gameObject.activeInHierarchy || (!UseExternalLightBudget && !EffectiveLight.enabled) ||
                EffectiveLight.intensity<=0)return false;
            maximum=Mathf.Min(maximum,EffectiveLight.range);
            if(EffectiveLight.type==LightType.Spot) {
                Vector3 toPoint=position-EffectiveLight.transform.position;
                if(toPoint.sqrMagnitude>.0001f && Vector3.Angle(EffectiveLight.transform.forward,toPoint)>EffectiveLight.spotAngle*.5f)return false;
            } else if(EffectiveLight.type!=LightType.Point)return false;
        }
        return (Position-position).sqrMagnitude<=maximum*maximum;
    }
}
