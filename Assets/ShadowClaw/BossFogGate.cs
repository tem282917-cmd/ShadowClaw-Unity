using UnityEngine;

/// <summary>A visible, traversable entrance before combat and a physical seal during combat.</summary>
[DisallowMultipleComponent]
public sealed class BossFogGate : MonoBehaviour
{
    public ShadowLab Encounter;
    public Material CurtainMaterial;
    [Min(1)] public float Width=7.6f;
    [Min(2)] public float Height=13.2f;
    [Min(.05f)] public float Thickness=.6f;
    [Min(.4f)] public float EntryClearance=.7f;
    [Min(.2f)] public float FallSpeed=2.7f;
    public BoxCollider Barrier;
    public bool ShowGuidance=true;
    GameObject visual;
    MeshRenderer curtain;
    float pausedAt, pausedDuration;
    bool started,finished,won,paused;
    float fade=1;
    Font hintFont;
    MaterialPropertyBlock properties;
    public bool Sealed => Barrier!=null && Barrier.enabled;
    public bool MistVisible => visual!=null && visual.activeSelf;
    public float MistOpacity => fade;
    public Vector3 Inward => transform.forward;
    public Vector3 FloorCenter => transform.position;

    // The containing hall checks its own floor and arena bounds. Waiting until
    // the capsule clears the plane prevents the newly sealed door pushing it out.
    public bool IsInside(Vector3 position) {
        var local=transform.InverseTransformPoint(position);
        return Mathf.Abs(local.x)<=Width*.5f && local.z>=EntryClearance && local.y>=-.25f && local.y<=Height;
    }
    public void SetEncounterState(bool encounterStarted,bool encounterFinished,bool encounterWon) {
        started=encounterStarted;finished=encounterFinished;won=encounterWon;
        if(Application.isPlaying)EnsureVisuals();
        if(Barrier!=null)Barrier.enabled=started && !finished;
        Encounter?.SetFinalRoomSeal(started && !finished);
        if(!won && fade<1)SetFade(1);
    }
    public void ResetGate() {
        started=finished=won=false;fade=1;
        if(Barrier!=null)Barrier.enabled=false;
        if(visual!=null)visual.SetActive(true);
        SetFade(1);
    }
    void Awake() {
        if(Encounter==null)Encounter=GetComponentInParent<ShadowLab>();
        if(Barrier==null)Barrier=GetComponentInChildren<BoxCollider>();
        // ShadowLab may already have restored the encounter earlier in Awake.
        if(Barrier!=null)Barrier.enabled=started && !finished;
        EnsureVisuals();
    }
    void EnsureVisuals() {
        if(visual!=null)return;
        var source=CurtainMaterial!=null ? CurtainMaterial : Resources.Load<Material>("ChapelBlackMist");
        if(source==null)return;
        visual=GameObject.CreatePrimitive(PrimitiveType.Quad);
        visual.name="White falling mist";
        var collider=visual.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
        visual.transform.SetParent(transform,false);
        visual.transform.localPosition=Vector3.up*(Height*.5f);
        visual.transform.localScale=new Vector3(Width,Height,1);
        curtain=visual.GetComponent<MeshRenderer>();curtain.sharedMaterial=source;
        curtain.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;curtain.receiveShadows=false;
        properties=new MaterialPropertyBlock();
        properties.SetFloat("_WhiteMist",1);
        properties.SetFloat("_FlowRate",FallSpeed/Mathf.Max(1,Height)*6);
        SetFade(fade);
    }
    void SetFade(float value) {
        fade=Mathf.Clamp01(value);
        if(curtain==null)return;
        visual.SetActive(fade>.001f);
        properties.SetFloat("_Opacity",fade);
        properties.SetFloat("_ClockOffset",pausedDuration);
        properties.SetFloat("_FreezeAt",paused ? pausedAt-pausedDuration : -1f);
        curtain.SetPropertyBlock(properties);
    }
    void Update() {
        AdvancePresentation(Time.unscaledDeltaTime,Encounter!=null && Encounter.GameplayPaused);
    }
    internal void AdvancePresentation(float elapsed,bool suspend) {
        if(suspend!=paused) {
            if(suspend)pausedAt=Time.time;
            else pausedDuration+=Time.time-pausedAt;
            paused=suspend;SetFade(fade);
        }
        // Finished encounters pause gameplay, but their visible exit must still open.
        if(won && fade>0)SetFade(fade-Mathf.Max(0,elapsed)/1.2f);
    }
    void OnGUI() {
        if(Event.current.type!=EventType.Repaint || !ShowGuidance || Encounter==null || Encounter.GameplayPaused || Encounter.Finished ||
           Encounter.Eye==null || Encounter.UI==null || Encounter.UI.CurrentView!="battle")return;
        Vector3 nearby=transform.InverseTransformPoint(Encounter.Eye.transform.position);
        if(Mathf.Abs(nearby.z)>4.2f || Mathf.Abs(nearby.x)>Width*.5f+1 || nearby.y<0 || nearby.y>Height)return;
        if(hintFont==null)hintFont=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",20);
        string hint=Sealed ? "战斗中 · 雾门已封闭" : "穿过雾幕，挑战影魔";
        var style=new GUIStyle(GUI.skin.label) {font=hintFont,fontSize=20,alignment=TextAnchor.MiddleCenter};
        style.normal.textColor=new Color(.9f,.91f,.95f);
        var box=new Rect(Screen.width*.5f-240,Screen.height*.67f,480,42);
        Color previous=GUI.color;GUI.color=new Color(.025f,.03f,.045f,.76f);
        GUI.DrawTexture(box,Texture2D.whiteTexture);GUI.color=previous;GUI.Label(box,hint,style);
    }
    void OnDestroy() {if(hintFont!=null)Destroy(hintFont);}
    void OnDrawGizmosSelected() {
        Gizmos.matrix=transform.localToWorldMatrix;
        Gizmos.color=new Color(.65f,.8f,1,.65f);
        Gizmos.DrawWireCube(Vector3.up*Height*.5f,new Vector3(Width,Height,Thickness));
        Gizmos.DrawLine(Vector3.up,Vector3.up+Vector3.forward*EntryClearance);
    }
}
