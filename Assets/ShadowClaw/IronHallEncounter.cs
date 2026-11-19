using UnityEngine;

// Bind the encounter to the authored colliders, lanterns and lighting in Iron Crown Hall.
public sealed class IronHallEncounter : MonoBehaviour
{
    [Header("Authored scene binding (legacy hall keeps its original defaults)")]
    public bool UseAuthoredLayout;
    public bool StartWithExploration;
    public BoxCollider BattleArea;
    [Min(1),Tooltip("Radius in metres of the upper hemisphere that retains scenery during the final battle.")]
    public float RoomRenderRadius=100;
    public BossFogGate FogGate;
    [Tooltip("Walkable surfaces that trigger the courtyard and upper bridge passages of the story.")]
    public Collider CourtyardNarrationFloor, UpperBridgeNarrationFloor;
    public Transform PlayerSpawn, DemonSpawn, MenuView;
    [Min(1),Tooltip("Return the hunter to PlayerSpawn this many metres below the chapter start.")]
    public float FallDepthBelowSpawn=12f;
    public Camera GameplayCamera;
    public Collider[] GroundColliders = System.Array.Empty<Collider>();
    public Transform[] WickAnchors = System.Array.Empty<Transform>();
    public BoxCollider Floor;
    public BoxCollider[] RaisedSurfaces;
    public GameObject PreviewCameras;
    public Renderer[] Lanterns;
    public Light[] GalleryLights;
    public Light Moonlight;

    public static readonly Rect Arena=Rect.MinMaxRect(-6.45f,-9.85f,6.45f,9.85f);
    public static readonly Rect ShadowBounds=Rect.MinMaxRect(-6.5f,-10f,6.5f,10f);
    public Rect PlayArea {
        get {
            if(!UseAuthoredLayout || BattleArea==null) return Arena;
            var b=BattleArea.bounds;
            return Rect.MinMaxRect(b.min.x,b.min.z,b.max.x,b.max.z);
        }
    }
    public Rect InkBounds => UseAuthoredLayout ? PlayArea : ShadowBounds;
    public Rect ExplorationArea => UseAuthoredLayout ? explorationArea : Arena;
    public Vector3 HunterStart => UseAuthoredLayout && PlayerSpawn!=null ? PlayerSpawn.position : new Vector3(-.45f,0,3.55f);
    public Vector3 DemonStart => UseAuthoredLayout && DemonSpawn!=null ? DemonSpawn.position : new Vector3(.36f,0,1.34f);
    struct Support {
        public Bounds Bounds;
        public Vector3 Top, Normal;
    }
    Support[] supports;
    struct GroundSupport { public Collider Collider; public Bounds Bounds; }
    GroundSupport[] authoredGround = System.Array.Empty<GroundSupport>();
    GroundSupport[] battleGround = System.Array.Empty<GroundSupport>();
    Rect explorationArea, cachedPlayArea;
    float authoredProjectionHeight;
    float[] lampIntensities, lampRanges;
    Color[][] lampEmission;
    MaterialPropertyBlock emissionBlock;
    Color ambient;
    float reflection;
    public BossRoomReadability Readability { get; private set; }

    public void Prepare() {
        emissionBlock = new MaterialPropertyBlock();
        if(Moonlight!=null) {
            var lighting=new MaterialPropertyBlock();
            foreach(var cage in Lanterns ?? System.Array.Empty<Renderer>()) {
                if(cage==null) continue;
                cage.GetPropertyBlock(lighting);
                lighting.SetVector("_MoonOrigin",Moonlight.transform.position);
                cage.SetPropertyBlock(lighting);
            }
        }
        if(PreviewCameras!=null) PreviewCameras.SetActive(false);
        var raised=RaisedSurfaces ?? System.Array.Empty<BoxCollider>();
        supports=new Support[raised.Length];
        for(int i=0;i<supports.Length;i++) {
            var box=raised[i];
            if(box==null) continue;
            supports[i]=new Support { Bounds=box.bounds, Normal=box.transform.up,
                Top=box.transform.TransformPoint(box.center+Vector3.up*(box.size.y*.5f)) };
        }
        if(UseAuthoredLayout) {
            // Newly placed library floors carry Collision__ children. Discover them once
            // when entering Play so duplicating a prefab needs no extra combat wiring.
            var authored = new System.Collections.Generic.HashSet<Collider>(GroundColliders ?? System.Array.Empty<Collider>());
            foreach(var root in gameObject.scene.GetRootGameObjects())
                foreach(var collider in root.GetComponentsInChildren<Collider>())
                    if(collider.name.StartsWith("Collision__",System.StringComparison.Ordinal)) authored.Add(collider);
            authored.RemoveWhere(collider=>collider==null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger);
            GroundColliders = new Collider[authored.Count];
            authored.CopyTo(GroundColliders);
            var ground=new System.Collections.Generic.List<GroundSupport>();
            var battle=new System.Collections.Generic.List<GroundSupport>();
            var area=cachedPlayArea=PlayArea;
            Vector2 minimum=new Vector2(HunterStart.x,HunterStart.z), maximum=minimum;
            authoredProjectionHeight=Mathf.Max(HunterStart.y,DemonStart.y)+4.3f;
            foreach(var collider in GroundColliders ?? System.Array.Empty<Collider>()) {
                if(collider==null || !collider.enabled || collider.isTrigger) continue;
                var b=collider.bounds;
                ground.Add(new GroundSupport { Collider=collider,Bounds=b });
                minimum=Vector2.Min(minimum,new Vector2(b.min.x,b.min.z));
                maximum=Vector2.Max(maximum,new Vector2(b.max.x,b.max.z));
                if(b.max.x<area.xMin || b.min.x>area.xMax || b.max.z<area.yMin || b.min.z>area.yMax) continue;
                battle.Add(new GroundSupport { Collider=collider,Bounds=b });
                // Keep the source above heads throughout an elevated, authored arena.
                authoredProjectionHeight=Mathf.Max(authoredProjectionHeight,b.max.y+4.3f);
            }
            authoredGround=ground.ToArray();
            battleGround=battle.ToArray();
            explorationArea=Rect.MinMaxRect(minimum.x-.5f,minimum.y-.5f,maximum.x+.5f,maximum.y+.5f);
        }
        lampIntensities=new float[GalleryLights.Length];
        lampRanges=new float[GalleryLights.Length];
        for(int i=0;i<GalleryLights.Length;i++) {
            lampIntensities[i]=GalleryLights[i].intensity;
            lampRanges[i]=GalleryLights[i].range;
        }
        if(UseAuthoredLayout) {
            lampEmission=new Color[Lanterns.Length][];
            for(int i=0;i<Lanterns.Length;i++) {
                var materials=Lanterns[i].sharedMaterials;
                lampEmission[i]=new Color[materials.Length];
                for(int slot=0;slot<materials.Length;slot++)
                    if(materials[slot]!=null && materials[slot].HasProperty("_EmissionColor"))
                        lampEmission[i][slot]=materials[slot].GetColor("_EmissionColor");
            }
        }
        ambient=RenderSettings.ambientLight;
        reflection=RenderSettings.reflectionIntensity;
        if(UseAuthoredLayout) Readability=BossRoomReadability.Create(this);
    }
    public Vector3 WickPosition(int slot) {
        if(UseAuthoredLayout && WickAnchors!=null && slot<WickAnchors.Length && WickAnchors[slot]!=null)
            return WickAnchors[slot].position;
        // Centre of the burner in the authored lantern (below the open glass panels).
        return Lanterns[slot].transform.TransformPoint(new Vector3(0,-.18f,.15f));
    }
    public bool IsGround(Collider collider) {
        if(UseAuthoredLayout) return System.Array.IndexOf(GroundColliders,collider)>=0;
        return collider==Floor || System.Array.IndexOf(RaisedSurfaces,collider)>=0;
    }
    internal string PassageNarrationAt(Vector3 feet) {
        if(OnNarrationFloor(CourtyardNarrationFloor,feet,.15f))return "22_courtyard";
        if(OnNarrationFloor(UpperBridgeNarrationFloor,feet,0))return "23_upper_bridge";
        return null;
    }
    static bool OnNarrationFloor(Collider floor,Vector3 feet,float insetFraction) {
        if(floor==null || !floor.enabled || !floor.gameObject.activeInHierarchy)return false;
        Bounds bounds=floor.bounds;
        Vector3 inset=bounds.size*insetFraction;
        if(feet.x<bounds.min.x+inset.x || feet.x>bounds.max.x-inset.x ||
            feet.z<bounds.min.z+inset.z || feet.z>bounds.max.z-inset.z)return false;
        // Test the actual walking mesh close to the hunter's feet. A storey
        // under the bridge or a neighbouring stair cannot trigger the passage.
        return floor.Raycast(new Ray(feet+Vector3.up*.35f,Vector3.down),out RaycastHit hit,.8f) && hit.normal.y>.5f;
    }
    public float SurfaceHeight(float x,float z) {
        if(UseAuthoredLayout) {
            float highest=float.NegativeInfinity;
            var candidates=cachedPlayArea.Contains(new Vector2(x,z)) ? battleGround : authoredGround;
            foreach(var support in candidates) {
                var b=support.Bounds;
                if(x<b.min.x-.001f || x>b.max.x+.001f || z<b.min.z-.001f || z>b.max.z+.001f) continue;
                var ray=new Ray(new Vector3(x,b.max.y+.5f,z),Vector3.down);
                if(support.Collider.Raycast(ray,out var hit,b.size.y+1f) && hit.normal.y>.5f)
                    highest=Mathf.Max(highest,hit.point.y);
            }
            return float.IsNegativeInfinity(highest) ? HunterStart.y : highest;
        }
        float y=0;
        foreach(var s in supports) {
            if(x<s.Bounds.min.x || x>s.Bounds.max.x || z<s.Bounds.min.z || z>s.Bounds.max.z) continue;
            float top=s.Top.y-((x-s.Top.x)*s.Normal.x+(z-s.Top.z)*s.Normal.z)/s.Normal.y;
            y=Mathf.Max(y,Mathf.Min(top,s.Bounds.max.y));
        }
        return y;
    }
    public bool ShouldStartEncounter(Vector3 position) {
        if(!UseAuthoredLayout || !StartWithExploration || BattleArea==null) return false;
        if(FogGate!=null) return FogGate.IsInside(position) &&
            Mathf.Abs(position.y-SurfaceHeight(position.x,position.z))<=.25f;
        var b=BattleArea.bounds;
        if(position.x<b.min.x || position.x>b.max.x || position.z<b.min.z || position.z>b.max.z ||
            position.y<b.min.y-.12f || position.y>b.max.y+.12f) return false;
        return Mathf.Abs(position.y-SurfaceHeight(position.x,position.z))<=.25f;
    }
    public Vector3 ProjectionLight(Vector3 average) {
        if(!UseAuthoredLayout) return new Vector3(average.x*.65f,4.3f,-.35f+average.z*.25f);
        Vector2 center=PlayArea.center;
        return new Vector3(center.x+Mathf.Clamp((average.x-center.x)*.65f,-3.2f,3.2f),
            authoredProjectionHeight,
            center.y-.35f+Mathf.Clamp((average.z-center.y)*.25f,-2.5f,2.5f));
    }
    public CharacterController CreateBody(string label,Transform parent) {
        var body=new GameObject(label);
        body.transform.SetParent(parent,false);
        body.layer=LayerMask.NameToLayer("Ignore Raycast");
        var controller=body.AddComponent<CharacterController>();
        controller.height=1.72f; controller.radius=.35f;
        controller.center=Vector3.up*.86f; controller.stepOffset=UseAuthoredLayout ? .4f : .22f;
        controller.skinWidth=.02f; controller.minMoveDistance=0;
        controller.slopeLimit=48;
        return controller;
    }
    public void SetLamp(int slot,float strength) {
        var light=GalleryLights[slot];
        light.enabled=strength>0;
        float phase=Time.time*6.4f+slot*2.1f;
        float flicker=1+.038f*Mathf.Sin(phase)+.016f*Mathf.Sin(phase*2.71f);
        light.intensity=lampIntensities[slot]*strength*flicker;
        light.range=lampRanges[slot]*Mathf.Lerp(.52f,1,Mathf.Sqrt(strength));
        if(UseAuthoredLayout && lampEmission!=null && slot<lampEmission.Length) {
            for(int material=0;material<lampEmission[slot].Length;material++) {
                if(lampEmission[slot][material].maxColorComponent<=0) continue;
                Lanterns[slot].GetPropertyBlock(emissionBlock,material);
                emissionBlock.SetColor("_EmissionColor",lampEmission[slot][material]*strength);
                Lanterns[slot].SetPropertyBlock(emissionBlock,material);
            }
        }
        if(slot<Lanterns.Length && Lanterns[slot]!=null)
            Lanterns[slot].GetComponentInParent<GrandCombatLantern>()?.SetStrength(strength);
    }
    public void SetRoomLight(float power) {
        if(UseAuthoredLayout) {
            // Combat darkness belongs to the six warm lamps. Keep the authored
            // global night baseline stable for this room and the rest of the level.
            Readability?.SetLampPower(power/HuntBoss.LampCapacity);
            return;
        }
        float brightness=Mathf.Lerp(.12f,1f,power/HuntBoss.LampCapacity);
        RenderSettings.ambientLight=ambient*Mathf.Lerp(.12f,1,brightness);
        RenderSettings.reflectionIntensity=reflection*brightness;
    }
}
