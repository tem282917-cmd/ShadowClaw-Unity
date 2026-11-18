using UnityEngine;

/// <summary>Optional volume whose position and actor list are chosen by the level author.</summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class AuthoredEncounterTrigger : MonoBehaviour
{
    public ShadowLab Encounter;
    public ShadowEncounterActor[] Actors;
    public bool OncePerAttempt=true;
    BoxCollider volume;bool entered;
    void Awake() { volume=GetComponent<BoxCollider>();volume.isTrigger=true; }
    void Update() {
        if(Encounter==null)Encounter=FindFirstObjectByType<ShadowLab>();
        if(Encounter==null || Encounter.GameplayPaused || (entered && OncePerAttempt))return;
        Vector3 point=Encounter.HunterBodyPosition+Vector3.up*.9f;
        if((volume.ClosestPoint(point)-point).sqrMagnitude>.0001f)return;
        entered=true;
        if(Actors!=null)foreach(var actor in Actors)if(actor!=null)actor.SetActivated(true);
    }
    public void ResetTrigger() { entered=false; }
    void OnDrawGizmosSelected() {
        var box=GetComponent<BoxCollider>();if(box==null)return;
        Gizmos.color=new Color(.65f,.3f,.9f,.65f);Gizmos.matrix=transform.localToWorldMatrix;Gizmos.DrawWireCube(box.center,box.size);
    }
}
