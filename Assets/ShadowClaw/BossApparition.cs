using UnityEngine;
using UnityEngine.Rendering;

// Reuses the shipped character, materials and animation clips. This elevated
// copy has no gameplay collider, light or connection to the shootable shadow.
public sealed class BossApparition {
    readonly Transform parent;
    readonly BossEffectLibrary library;
    GameObject root;
    ShadowDemonView view;
    SkinnedMeshRenderer[] surfaces;
    Transform head;
    readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
    Vector3 baseScale,anchor;
    Quaternion facing;
    float naturalHeight,naturalFoot;
    bool visible;
    HuntBoss.Move lastAction;
    public GameObject Root => root;
    public bool Visible => visible && root!=null && root.activeSelf;
    public float Height {get;private set;}

    public BossApparition(Transform parent,BossEffectLibrary library) {
        this.parent=parent;this.library=library;
    }
    bool Ensure() {
        if(root!=null)return true;
        var prefab=Resources.Load<GameObject>("ShadowDemon/Character");
        if(prefab==null)return false;
        root=Object.Instantiate(prefab,parent,false);
        root.name="High bell-sleeve apparition - visual only";
        view=root.GetComponent<ShadowDemonView>();
        if(view==null) {Object.Destroy(root);root=null;return false;}
        view.enabled=false;
        foreach(var child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=2; // Ignore Raycast
        foreach(var collider in root.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        foreach(var light in root.GetComponentsInChildren<Light>(true))light.enabled=false;
        foreach(var rigidbody in root.GetComponentsInChildren<Rigidbody>(true)) {rigidbody.isKinematic=true;rigidbody.detectCollisions=false;}
        foreach(var animator in root.GetComponentsInChildren<Animator>(true))animator.enabled=false;
        foreach(var animation in root.GetComponentsInChildren<Animation>(true))animation.enabled=false;
        surfaces=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if(surfaces.Length==0) {Object.Destroy(root);root=null;return false;}
        Bounds bounds=surfaces[0].bounds;
        foreach(var surface in surfaces) {
            bounds.Encapsulate(surface.bounds);surface.shadowCastingMode=ShadowCastingMode.Off;
            surface.receiveShadows=false;surface.updateWhenOffscreen=true;
        }
        naturalHeight=Mathf.Max(.1f,bounds.size.y);
        naturalFoot=bounds.min.y-root.transform.position.y;
        baseScale=root.transform.localScale;
        foreach(var bone in surfaces[0].bones)if(bone!=null && bone.name=="Head") {head=bone;break;}
        root.SetActive(false);return true;
    }
    public void Tick(HuntBoss boss,ClawState world,float dt) {
        bool show=boss.GrandAttacks && (boss.Action==HuntBoss.Move.SleeveTell || boss.Action==HuntBoss.Move.SleeveSweep ||
            boss.Action==HuntBoss.Move.BellTell || boss.Action==HuntBoss.Move.BellWaves || boss.Action==HuntBoss.Move.Ritual);
        if(!show || library!=null && !library.ShowApparition) {Hide();lastAction=boss.Action;return;}
        if(!Ensure())return;
        if(!visible) {
            float minimum=library ? library.ApparitionMinimumHeight : 8;
            float maximum=library ? Mathf.Max(minimum,library.ApparitionMaximumHeight) : 16;
            float arenaScale=library ? library.ApparitionArenaScale : .7f;
            Height=Mathf.Clamp(Mathf.Max(boss.MoveBounds.width,boss.MoveBounds.height)*arenaScale,minimum,maximum);
            float multiplier=Height/naturalHeight;
            root.transform.localScale=baseScale*multiplier;
            Vector3 toward=world.Player-world.Enemy;toward.y=0;
            if(toward.sqrMagnitude<.01f)toward=Vector3.back;
            toward.Normalize();
            anchor=world.Enemy-toward*Mathf.Max(2.5f,Height*.24f);
            anchor.x=Mathf.Clamp(anchor.x,boss.MoveBounds.xMin+.5f,boss.MoveBounds.xMax-.5f);
            anchor.z=Mathf.Clamp(anchor.z,boss.MoveBounds.yMin+.5f,boss.MoveBounds.yMax-.5f);
            anchor.y=world.Enemy.y+(library ? library.ApparitionGroundClearance : 4)-naturalFoot*multiplier;
            facing=Quaternion.LookRotation(toward,Vector3.up);
            root.SetActive(true);visible=true;
        }
        if(dt<=0 && lastAction==boss.Action)return;
        bool sleeves=boss.Action==HuntBoss.Move.SleeveTell || boss.Action==HuntBoss.Move.SleeveSweep;
        var clip=System.Array.Find(view.Clips,c=>c!=null && c.name==(sleeves ? "Cast" : "Ritual"));
        if(clip!=null) {
            float pose;
            if(boss.Action==HuntBoss.Move.SleeveTell)pose=Mathf.Lerp(.06f,.46f,boss.MoveProgress);
            else if(boss.Action==HuntBoss.Move.SleeveSweep)pose=Mathf.Lerp(.46f,1,boss.MoveProgress);
            else if(boss.Action==HuntBoss.Move.BellTell)pose=Mathf.Lerp(.08f,.58f,boss.MoveProgress);
            else if(boss.Action==HuntBoss.Move.BellWaves)pose=.58f+Mathf.Sin(boss.BellElapsed*3)*.035f;
            else pose=Mathf.Lerp(.12f,.92f,boss.RitualProgress);
            clip.SampleAnimation(root,clip.length*Mathf.Clamp01(pose));
        }
        root.transform.SetPositionAndRotation(anchor,facing);
        if(head!=null)foreach(var surface in surfaces) {
            surface.GetPropertyBlock(properties);properties.SetMatrix("_WorldToBellHead",head.worldToLocalMatrix);
            properties.SetColor("_Spirit",new Color(.65f,.42f,.92f));surface.SetPropertyBlock(properties);
        }
        lastAction=boss.Action;
    }
    public void Hide() {if(root!=null)root.SetActive(false);visible=false;}
    public void Dispose() {if(root!=null)Object.Destroy(root);root=null;visible=false;}
}
