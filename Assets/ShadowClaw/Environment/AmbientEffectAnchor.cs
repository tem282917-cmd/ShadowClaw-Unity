using UnityEngine;

/// <summary>An author-selected atmosphere location. No lights or gameplay targets are created.</summary>
[DisallowMultipleComponent, AddComponentMenu("ShadowClaw/Environment/Ambient effect anchor")]
public sealed class AmbientEffectAnchor : MonoBehaviour
{
    public enum EffectKind { LampDust, SlowMist }
    public EffectKind Kind;
    [Tooltip("Optional original imported prefab. Empty uses the credited bundled source.")]
    public GameObject SourcePrefab;
    [Range(.2f, 3f)] public float Footprint = 1f;
    [Range(.05f, 1f)] public float Opacity = .35f;
    [Range(5, 100)] public float MaximumDistance = 28;
    [Tooltip("Optional lamp binding; dust is hidden when this lamp is extinguished.")]
    public EncounterLantern Lamp;
    [HideInInspector] public bool Automatic;
    public Vector3 Position => transform.position;
    public float BoundsRadius => Kind == EffectKind.SlowMist ? 3f * Footprint : 1.3f * Footprint;
    public bool Available => isActiveAndEnabled && (Lamp == null || Lamp.Lit);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Kind == EffectKind.SlowMist ? new Color(.5f, .75f, .9f, .5f) : new Color(.9f, .75f, .4f, .5f);
        Gizmos.DrawWireSphere(Position, BoundsRadius);
    }
}
