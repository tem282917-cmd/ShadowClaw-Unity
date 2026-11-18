using UnityEngine;

/// <summary>Authored combat actors share the hunter, lamp rules and shot ordering.</summary>
public abstract class ShadowEncounterActor : MonoBehaviour
{
    public ShadowLab Encounter;
    public bool Activated=true;
    public virtual bool Alive => true;

    protected virtual void OnEnable() {
        if(Encounter==null) Encounter=FindFirstObjectByType<ShadowLab>();
        Encounter?.RegisterActor(this);
    }
    protected virtual void Start() {
        if(Encounter==null) Encounter=FindFirstObjectByType<ShadowLab>();
        Encounter?.RegisterActor(this);
    }
    protected virtual void OnDisable() { Encounter?.UnregisterActor(this); }
    public void SetActivated(bool value) { if(Activated==value)return;Activated=value;OnActivationChanged(value); }
    protected virtual void OnActivationChanged(bool value) { }
    public abstract void Tick(float dt);
    public abstract void ResetEncounter();
    public abstract bool TryHitShadow(Ray ray,float maxDistance,out float distance);
    public abstract void ReceiveShadowHit(Bolt kind,Vector3 point);
    public virtual void OnLampShot(int index) { }
    public virtual void OnEncounterEnded() { }
}
