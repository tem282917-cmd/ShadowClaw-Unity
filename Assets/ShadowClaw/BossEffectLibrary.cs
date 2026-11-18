using UnityEngine;

// Assign reviewed third-party prefabs on the encounter root. No effects are
// procedurally authored here; missing prefabs leave the accurate tell borders.
public sealed class BossEffectLibrary : MonoBehaviour {
    public GameObject SleeveTellPrefab;
    public GameObject SleeveStrikePrefab;
    public GameObject BellChargePrefab;
    public GameObject BellWavePrefab;
    public GameObject RitualPrefab;
    public GameObject DarkHuntPrefab;
    [Min(.01f)] public float SleeveReferenceWidth=8;
    [Tooltip("Zero measures the authored horizontal ring at the frozen sample; a positive value overrides its radius in metres.")]
    [Min(0)] public float BellWaveReferenceRadius;
    [Min(.01f)] public float BellWaveSampleTime=.3f;
    [Tooltip("Visible ring radius in texture UV units. Free Quick Effects Swirl01 peaks at 0.365, inside its transparent padding.")]
    [Range(.05f,.5f)] public float BellWaveTextureRadius=.365f;
    [Header("Existing character apparition")]
    public bool ShowApparition=true;
    [Min(2)] public float ApparitionMinimumHeight=8;
    [Min(2)] public float ApparitionMaximumHeight=16;
    [Min(.1f)] public float ApparitionArenaScale=.7f;
    [Min(2)] public float ApparitionGroundClearance=4;
}
