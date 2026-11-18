using UnityEngine;
using UnityEngine.Rendering;

// The gameplay clock samples the pose, including when a long frame crosses a reload boundary.
public sealed class HunterWeaponView : MonoBehaviour
{
    public AnimationClip Idle, Cycle, Lower, Load, Raise;
    public MeshRenderer RailBolt;
    public SkinnedMeshRenderer Bow;
    public Material FlightTrail;
    Transform flying;
    TrailRenderer trail;
    Vector3 origin, endpoint;
    float age, travel, idleClock;
    bool shotInFlight;
    public float ShotImpactDelay=>.0256f+travel;
    Vector3 locomotionPosition,locomotionRotation;
    float knifeThrowRemaining;
    public void SetLocomotion(Vector3 position,Vector3 rotation) {locomotionPosition=position;locomotionRotation=rotation;}
    public void NotifyKnifeThrow() {knifeThrowRemaining=HolyKnifeThrowMotion.Duration;}
    public void CancelKnifeThrow() {knifeThrowRemaining=0;}

    static readonly Color[] Effects = {
        new Color(.72f,.9f,1f), new Color(1f,.28f,.06f),
        new Color(.36f,.57f,1f), new Color(1f,.9f,.43f)
    };

    public static HunterWeaponView Equip(Camera camera,Transform world) {
        var prefab=Resources.Load<HunterWeaponView>("HunterWeapon");
        var view=Instantiate(prefab,camera.transform,false);
        view.name="Crossbow";
        var projectile=new GameObject("Flying bolt",typeof(MeshFilter),typeof(MeshRenderer));
        view.flying=projectile.transform;
        view.flying.SetParent(world,false);
        projectile.GetComponent<MeshFilter>().sharedMesh=view.RailBolt.GetComponent<MeshFilter>().sharedMesh;
        var renderer=projectile.GetComponent<MeshRenderer>();
        renderer.sharedMaterial=view.RailBolt.sharedMaterial;
        renderer.shadowCastingMode=ShadowCastingMode.Off;
        view.trail=projectile.AddComponent<TrailRenderer>();
        view.trail.sharedMaterial=view.FlightTrail;
        view.trail.time=.065f;
        view.trail.startWidth=.018f; view.trail.endWidth=.001f;
        view.trail.minVertexDistance=.015f;
        projectile.SetActive(false);
        return view;
    }

    public void ResetView() {
        idleClock=0;
        locomotionPosition=locomotionRotation=Vector3.zero;knifeThrowRemaining=0;
        shotInFlight=false;
        trail.Clear();
        flying.gameObject.SetActive(false);
    }

    // The raycast owns the hit. The travelling bolt is visual, with no collider or delayed damage.
    public void Release(Vector3 hitPoint,Bolt kind) {
        age=0;
        shotInFlight=true;
        var rail=RailBolt.transform;
        origin=rail.position+rail.forward*.225f;
        endpoint=hitPoint;
        Vector3 path=endpoint-origin;
        float length=path.magnitude;
        var heading=length>.001f ? path/length : rail.forward;
        // Stop the tip, rather than the bolt centre, at the contact point.
        endpoint-=heading*Mathf.Min(.15f,length);
        travel=Mathf.Clamp(length/57f,.035f,.24f);
        flying.SetPositionAndRotation(origin,Quaternion.LookRotation(heading));
        flying.gameObject.SetActive(false);
        trail.Clear();
        Color color=Effects[(int)kind];
        trail.startColor=color; color.a=0; trail.endColor=color;
    }

    public void SyncPose(HunterCrossbow weapon,float dt) {
        idleClock+=dt;
        var phase=weapon.Phase;
        var clip=Idle;
        float t=weapon.PhaseProgress;
        bool show=weapon.Rounds>0;
        switch(phase) {
            case HunterCrossbow.Motion.Cycle:
                clip=Cycle;
                show=t<.08f || (show && t>=.60f);
                break;
            case HunterCrossbow.Motion.Lower: clip=Lower; break;
            case HunterCrossbow.Motion.Load:
                clip=Load;
                show=(show && t<.19f) || t>=.42f;
                break;
            case HunterCrossbow.Motion.Raise: clip=Raise; break;
            default: t=Mathf.Repeat(idleClock/clip.length,1); break;
        }
        clip.SampleAnimation(gameObject,t*clip.length);
        // Offsets are applied to the view root after sampling, so existing
        // reload/bolt/hand animations retain their authored relative transforms.
        knifeThrowRemaining=Mathf.Max(0,knifeThrowRemaining-dt);
        float throwAge=HolyKnifeThrowMotion.Duration-knifeThrowRemaining;
        float throwPose=knifeThrowRemaining>0 ? Mathf.SmoothStep(0,1,throwAge/.14f)*
            (1-Mathf.SmoothStep(0,1,(throwAge-.30f)/.32f)) : 0;
        float motionWeight=phase==HunterCrossbow.Motion.Ready ? 1 : .38f;
        transform.localPosition=locomotionPosition*motionWeight+new Vector3(.09f,-.13f,-.025f)*throwPose;
        transform.localRotation=Quaternion.Euler(locomotionRotation*motionWeight+new Vector3(throwPose*11,throwPose*4,throwPose*-5));
        RailBolt.enabled=show;
        if(!shotInFlight) return;
        age+=dt;
        float fraction=(age-.0256f)/travel;
        if(fraction<0) return;
        if(fraction>=1) {
            flying.gameObject.SetActive(false);
            shotInFlight=false;
            return;
        }
        flying.position=Vector3.Lerp(origin,endpoint,fraction);
        if(!flying.gameObject.activeSelf) {
            flying.gameObject.SetActive(true);
            trail.Clear();
        }
    }
}
