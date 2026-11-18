using System.Collections.Generic;
using UnityEngine;

// Presentation-only moon fill. It is deliberately separate from the six combat
// sources and EncounterLantern: these lights never cast a shootable shadow.
[DisallowMultipleComponent]
public sealed class BossRoomReadability : MonoBehaviour
{
    readonly List<Light> fills = new List<Light>(3);
    ShadowDemonView demon;
    SkinnedMeshRenderer guardianArmour;
    MaterialPropertyBlock armourTint, originalTint;
    Color armourColor, armourEmission;
    bool presentationEnabled = true;
    public IReadOnlyList<Light> FillLights => fills;
    public float LampPower01 { get; private set; } = 1;
    public bool PresentationEnabled {
        get => presentationEnabled;
        set { presentationEnabled = value; Apply(); }
    }

    public static BossRoomReadability Create(IronHallEncounter hall)
    {
        if (hall == null || !hall.UseAuthoredLayout) return null;
        var existing = hall.transform.Find("Boss room - presentation moon fill");
        if (existing != null) return existing.GetComponent<BossRoomReadability>();
        var root = new GameObject("Boss room - presentation moon fill");
        root.transform.SetParent(hall.transform, false);
        var result = root.AddComponent<BossRoomReadability>();
        var area = hall.PlayArea;
        // Fixed world positions keep modelling stable while the player circles.
        // Three broad, overlapping pools illuminate the paving under every
        // possible combat shadow without adding realtime shadow-map passes.
        for (int i = 0; i < 3; ++i) {
            float z = Mathf.Lerp(area.yMin, area.yMax, (i + .5f) / 3);
            float x = area.center.x + (i == 1 ? -1 : 1) * area.width * .08f;
            result.AddFill("Moon Fill " + (i + 1), new Vector3(x, hall.SurfaceHeight(x, z) + 7, z),
                Mathf.Max(area.width * .72f, area.height / 3 * 1.28f), 2.4f);
        }
        result.Apply();
        return result;
    }

    void AddFill(string name, Vector3 position, float range, float intensity)
    {
        var fill = new GameObject(name).AddComponent<Light>();
        fill.transform.SetParent(transform, false);
        fill.transform.position = position;
        fill.type = LightType.Point;
        fill.color = new Color(.52f, .65f, .86f);
        fill.intensity = intensity;
        fill.range = range;
        fill.shadows = LightShadows.None;
        fill.renderMode = LightRenderMode.ForcePixel;
        fill.bounceIntensity = 0;
        fills.Add(fill);
    }

    public static BossRoomReadability CreateForGuardian(FixedLanternGuardian guardian)
    {
        var hall = guardian.Encounter.GetComponent<IronHallEncounter>();
        if (hall == null || !hall.UseAuthoredLayout) return null;
        var arena = guardian.ArenaCenter != null ? guardian.ArenaCenter : guardian.transform;
        var root = new GameObject("Guardian chapel - presentation moon fill");
        root.transform.SetParent(guardian.transform, false);
        var result = root.AddComponent<BossRoomReadability>();
        Vector3 front = Quaternion.Euler(0, guardian.transform.eulerAngles.y, 0) * Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, front);
        float width = Mathf.Clamp(guardian.ArenaSize.x, 8, 36);
        float depth = Mathf.Clamp(guardian.ArenaSize.y, 8, 48);
        float range = Mathf.Clamp(Mathf.Max(width * .78f, depth * .56f), 11, 25);
        Vector3 boss = guardian.transform.position;
        // Cover the frontal approach, then split key and rim across the armour.
        // The resulting pools overlap on the paving beneath the target shadow.
        result.AddFill("Entrance floor fill", arena.position + front * (depth * .33f) +
            side * (width * .08f) + Vector3.up * 6, range, 2.6f);
        result.AddFill("Armour key fill", boss + front * (depth * .20f) +
            side * (width * .21f) + Vector3.up * (guardian.BodyHeight * .84f), range, 3.0f);
        result.AddFill("Armour rim fill", boss - front * (depth * .10f) -
            side * (width * .22f) + Vector3.up * (guardian.BodyHeight * 1.12f), range, 2.6f);
        var originalSurface = Resources.Load<Material>("Guardian/F01Surface");
        foreach (var skin in guardian.GetComponentsInChildren<SkinnedMeshRenderer>()) {
            if (originalSurface == null || skin.sharedMaterial != originalSurface ||
                skin.GetComponentInParent<EncounterAuthoringPreview>() != null) continue;
            result.guardianArmour = skin;
            result.originalTint = new MaterialPropertyBlock();
            result.armourTint = new MaterialPropertyBlock();
            skin.GetPropertyBlock(result.originalTint);
            skin.GetPropertyBlock(result.armourTint);
            result.armourColor = originalSurface.color;
            result.armourEmission = originalSurface.GetColor("_EmissionColor");
            break;
        }
        result.Apply();
        return result;
    }

    public void Bind(ShadowDemonView view)
    {
        if (demon != null && demon != view) demon.SetReadability(1, 1);
        demon = view;
        Apply();
    }

    public void SetLampPower(float power01)
    {
        power01 = Mathf.Clamp01(power01);
        if (Mathf.Approximately(power01, LampPower01)) return;
        LampPower01 = power01;
        Apply();
    }

    void Apply()
    {
        bool active = isActiveAndEnabled && presentationEnabled;
        foreach (var fill in fills) if (fill != null) fill.enabled = active;
        if (guardianArmour != null) {
            if (active) {
                Color surface = armourColor * Mathf.Lerp(1.22f, 1.16f, LampPower01);
                surface.a = armourColor.a;
                armourTint.SetColor("_Color", surface);
                armourTint.SetColor("_EmissionColor", armourEmission * Mathf.Lerp(1.65f, 1.40f, LampPower01));
            }
            guardianArmour.SetPropertyBlock(active ? armourTint : originalTint);
        }
        // Losing warm combat lamps increases the existing cool silk rim very
        // slightly. The body remains readable even when its target shadow is off.
        if (demon != null) demon.SetReadability(
            active ? Mathf.Lerp(1.22f, 1.16f, LampPower01) : 1,
            active ? Mathf.Lerp(1.60f, 1.40f, LampPower01) : 1);
    }

    void OnEnable() => Apply();
    void OnDisable() => Apply();
    void OnDestroy() { if (demon != null) demon.SetReadability(1, 1); }
}
