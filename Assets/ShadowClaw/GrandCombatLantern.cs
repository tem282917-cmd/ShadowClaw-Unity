using UnityEngine;

/// <summary>Presentation for the authored six lights; it never adds projection lights.</summary>
[SelectionBase, DisallowMultipleComponent]
public sealed class GrandCombatLantern : MonoBehaviour
{
    public Transform Wick;
    public Light GameplayLight;
    public Renderer PrimaryRenderer;
    public Renderer[] GlassSurfaces=System.Array.Empty<Renderer>();
    [Min(.1f)] public float TargetRadius=.9f;
    [Min(.1f)] public float FlameScale=3.8f;
    public Color WarmCore=new Color(1,.36f,.075f);
    public Color DormantCore=new Color(.38f,.055f,.8f);
    [Tooltip("Low texture-modulated visibility fill on brass only; does not create illumination or a gameplay shadow.")]
    public Color FrameEmission=new Color(.28f,.30f,.36f);
    MaterialPropertyBlock properties;
    public float Strength {get;private set;}=1;

    public void ConfigureRuntimeWick(Transform fixture,SphereCollider target,LanternFire flame) {
        if(target!=null)target.radius=TargetRadius;
        if(flame==null || fixture==null)return;
        var mount=fixture.Find("Large lantern flame scale");
        if(mount==null) {
            mount=new GameObject("Large lantern flame scale").transform;
            mount.SetParent(fixture,false);
        }
        mount.localScale=Vector3.one*FlameScale;
        flame.transform.SetParent(mount,false);
        flame.transform.localPosition=Vector3.zero;
    }
    public void SetStrength(float strength) {
        if(properties==null)properties=new MaterialPropertyBlock();
        // Older sample scenes still bind a light from the original small fixture.
        if(GameplayLight!=null && Wick!=null && GameplayLight.transform!=Wick)GameplayLight.transform.position=Wick.position;
        Strength=Mathf.Clamp01(strength);
        Color glow=Strength>0 ? WarmCore*Mathf.Lerp(.18f,1.5f,Mathf.Sqrt(Strength)) : DormantCore*.7f;
        foreach(var renderer in GlassSurfaces) {
            if(renderer==null)continue;
            var materials=renderer.sharedMaterials;
            for(int slot=0;slot<materials.Length;slot++) {
                if(materials[slot]==null || !materials[slot].name.Contains("Glass"))continue;
                renderer.GetPropertyBlock(properties,slot);
                properties.SetColor("_EmissionColor",glow);
                renderer.SetPropertyBlock(properties,slot);
            }
        }
        // IronHallEncounter scales old emission slots with lamp strength. Keep
        // this faint material fill constant so blackout does not erase the cage.
        if(PrimaryRenderer!=null) {
            var materials=PrimaryRenderer.sharedMaterials;
            for(int slot=0;slot<materials.Length;slot++) {
                if(materials[slot]==null || !materials[slot].name.Contains("Ornate Brass"))continue;
                PrimaryRenderer.GetPropertyBlock(properties,slot);
                properties.SetColor("_EmissionColor",FrameEmission);
                PrimaryRenderer.SetPropertyBlock(properties,slot);
            }
        }
    }
    void OnEnable() {SetStrength(Strength);}
}
