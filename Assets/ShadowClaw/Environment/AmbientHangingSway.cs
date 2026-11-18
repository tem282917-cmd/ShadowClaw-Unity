using UnityEngine;

/// <summary>Opt-in only: assign the visual cloth/chain child, never a combat lamp or physical fixture.</summary>
[DisallowMultipleComponent, AddComponentMenu("ShadowClaw/Environment/Hanging decoration sway")]
public sealed class AmbientHangingSway : MonoBehaviour
{
    public Transform Visual;
    [Tooltip("Pivot in the visual's parent coordinates; set it to the existing upper attachment.")]
    public Vector3 LocalPivot;
    [Range(.1f, 2f)] public float Degrees = .65f;
    [Range(.05f, .5f)] public float Frequency = .12f;
    [Range(5, 60)] public float MaximumDistance = 35;
    Quaternion originalRotation;
    Vector3 originalPosition;
    Renderer surface;
    bool captured;
    public bool SafeTarget => IsSafeTarget(Visual);
    public static bool IsSafeTarget(Transform visual) => visual != null &&
        visual.GetComponentInChildren<Renderer>() != null &&
        visual.GetComponentInChildren<Light>(true) == null &&
        visual.GetComponentInChildren<Collider>(true) == null &&
        visual.GetComponentInChildren<Rigidbody>(true) == null &&
        visual.GetComponentInChildren<Animator>(true) == null &&
        visual.GetComponentInParent<EncounterLantern>() == null &&
        visual.GetComponentInParent<GrandCombatLantern>() == null &&
        visual.GetComponentInParent<Light>() == null &&
        visual.GetComponentInParent<ShadowDemonView>() == null &&
        visual.GetComponentInParent<HunterWeaponView>() == null &&
        visual.GetComponentInParent<ShadowEncounterActor>() == null;

    public void Advance(float clock, float gust, Camera camera, Plane[] planes, bool suspended)
    {
        if (!captured)
        {
            if (!SafeTarget) return;
            originalPosition = Visual.localPosition; originalRotation = Visual.localRotation;
            surface = Visual.GetComponentInChildren<Renderer>(); captured = true;
        }
        if (Visual == null) return;
        if (suspended || !isActiveAndEnabled || camera == null ||
            Vector3.Distance(camera.transform.position, Visual.position) > MaximumDistance ||
            surface == null || !GeometryUtility.TestPlanesAABB(planes, surface.bounds)) { Restore(); return; }
        Quaternion swing = Quaternion.AngleAxis(Mathf.Sin(clock * Frequency * Mathf.PI * 2) * Degrees * gust, Vector3.forward);
        Visual.localRotation = swing * originalRotation;
        Visual.localPosition = LocalPivot + swing * (originalPosition - LocalPivot);
    }
    public void Restore()
    {
        if (!captured || Visual == null) return;
        Visual.localRotation = originalRotation; Visual.localPosition = originalPosition;
    }
    void OnDisable() { Restore(); }
    void OnDestroy() { Restore(); }
}
