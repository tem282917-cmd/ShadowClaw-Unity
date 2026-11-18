using System;
using System.Collections.Generic;
using System.Diagnostics;
using Chapter01.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>Small, optional atmosphere budget. Authoring remains in scene anchors, not a generated layout.</summary>
[DisallowMultipleComponent, AddComponentMenu("ShadowClaw/Environment/Ambient dynamics director")]
public sealed class AmbientDynamicsDirector : MonoBehaviour
{
    public const string MainScene = "Chapter01_Main";
    public bool DynamicsEnabled = true;
    public bool AutomaticOrdinaryLampDust = true;
    public bool UseExistingMistVolumes = true;
    [Range(0, 3)] public int MaximumDust = 3;
    [Range(0, 1)] public int MaximumMist = 1;
    [Range(0, .2f)] public float WindVolume = .065f;
    public AudioClip WindLoop;
    public int ActiveEffects { get; private set; }
    public int LiveParticleCount { get; private set; }
    public int AnchorCount => anchors.Count;
    public double LastCpuMilliseconds { get; private set; }
    public float PresentationClock { get; private set; }
    public ShadowLab Encounter { get; private set; }
    public bool Suspended => Encounter == null || Encounter.GameplayPaused;
    public bool ReducedMotion => Encounter != null && Encounter.UI != null && Encounter.UI.Settings.ReducedMotion;
    readonly List<AmbientEffectAnchor> anchors = new List<AmbientEffectAnchor>(40);
    readonly List<AmbientHangingSway> sways = new List<AmbientHangingSway>(12);
    readonly HashSet<int> automaticLamps = new HashSet<int>();
    readonly HashSet<int> authoredMistVolumes = new HashSet<int>();
    readonly AmbientEffectAnchor[] selected = new AmbientEffectAnchor[4];
    readonly AmbientImportedEffect[] effects = new AmbientImportedEffect[4];
    readonly Plane[] planes = new Plane[6];
    float nextDiscovery, nextSelection;
    AudioSource wind;
    bool windPaused,finalRoomSuspended;
    IronHallEncounter hall;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
        SceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }
    static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != MainScene) return;
        foreach (var existing in FindObjectsByType<AmbientDynamicsDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (existing.gameObject.scene == scene) return;
        var root = new GameObject("Ambient dynamics - runtime only");
        SceneManager.MoveGameObjectToScene(root, scene); root.AddComponent<AmbientDynamicsDirector>();
    }
    void Start()
    {
        wind = gameObject.AddComponent<AudioSource>(); wind.playOnAwake = false; wind.loop = true;
        wind.spatialBlend = 0; wind.volume = 0; wind.priority = 220;
        wind.clip = WindLoop != null ? WindLoop : Resources.Load<AudioClip>("EnvironmentDynamics/WindWooshLoop");
        if (wind.clip != null) wind.Play();
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg == "--ambient-benchmark") gameObject.AddComponent<AmbientDynamicsBenchmark>();
    }
    void Update()
    {
        long started = Stopwatch.GetTimestamp();
        if (Encounter == null)
            foreach (var candidate in FindObjectsByType<ShadowLab>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { Encounter = candidate; break; }
        if (Encounter == null || !Encounter.GameplayReady || Encounter.Eye == null) return;
        if(Encounter.FinalRoomIsolated) {
            if(!finalRoomSuspended)Advance(0,true,true,Encounter.Eye);
            finalRoomSuspended=true;
            return;
        }
        if(finalRoomSuspended){finalRoomSuspended=false;nextSelection=0;}
        if (Time.unscaledTime >= nextDiscovery)
        {
            Discover(); nextDiscovery = Time.unscaledTime + 5;
        }
        Advance(Mathf.Clamp(Time.unscaledDeltaTime, 0, .1f), Suspended, ReducedMotion, Encounter.Eye);
        LastCpuMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
    }
    void Discover()
    {
        if (hall == null) hall = Encounter.GetComponent<IronHallEncounter>();
        if (hall == null)
            foreach (var candidate in FindObjectsByType<IronHallEncounter>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { hall = candidate; break; }
        if (AutomaticOrdinaryLampDust)
            foreach (var lamp in FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None))
            {
                if (lamp.gameObject.scene != gameObject.scene || !lamp.RuntimeSceneAdapter ||
                    !automaticLamps.Add(lamp.GetInstanceID()) || IsBossArea(lamp.Position)) continue;
                var host = new GameObject("Ambient dust - existing ordinary fixture");
                host.transform.SetParent(lamp.transform, false); host.transform.position = lamp.Position + Vector3.up * .65f;
                var anchor = host.AddComponent<AmbientEffectAnchor>();
                anchor.Kind = AmbientEffectAnchor.EffectKind.LampDust; anchor.Lamp = lamp;
                anchor.Automatic = true; anchor.MaximumDistance = 24; anchor.Opacity = .3f;
            }
        if (UseExistingMistVolumes)
        {
            var post = Encounter.Eye.GetComponent<Chapter01NightPostProcess>();
            if (post != null && post.mistVolumes != null)
                for (int i = 0; i < Mathf.Min(4, post.mistVolumes.Length); i++)
                {
                    if (authoredMistVolumes.Contains(i)) continue;
                    var volume = post.mistVolumes[i];
                    Vector3 position = volume.center;
                    float floor = Encounter.GroundHeightAt(position.x, position.z);
                    // Reuse the authored volume centre in XZ, lifted above combat silhouettes.
                    // A volume whose central clear zone contains it is deliberately left empty.
                    position.y = Mathf.Max(position.y, floor + 4.5f);
                    if (volume.density <= 0 || position.y + 1.5f > volume.center.y + volume.radius.y ||
                        !OutsideClearZone(position, volume.clearCenter, volume.clearRadius, 4) || IsBossArea(position))
                    { authoredMistVolumes.Add(i); continue; }
                    var host = new GameObject("Ambient slow mist - authored volume " + i);
                    host.transform.SetParent(transform, false); host.transform.position = position;
                    var anchor = host.AddComponent<AmbientEffectAnchor>(); anchor.Automatic = true;
                    anchor.Kind = AmbientEffectAnchor.EffectKind.SlowMist;
                    anchor.MaximumDistance = 70; anchor.Footprint = 1.5f; anchor.Opacity = .24f;
                    authoredMistVolumes.Add(i);
                }
        }
        anchors.Clear(); sways.Clear();
        foreach (var anchor in FindObjectsByType<AmbientEffectAnchor>(FindObjectsSortMode.None))
            if (anchor.gameObject.scene == gameObject.scene) anchors.Add(anchor);
        foreach (var sway in FindObjectsByType<AmbientHangingSway>(FindObjectsSortMode.None))
            if (sway.gameObject.scene == gameObject.scene) sways.Add(sway);
    }
    bool IsBossArea(Vector3 position)
    {
        if (hall == null) return false;
        var bounds = hall.PlayArea; bounds.xMin -= 6; bounds.xMax += 6; bounds.yMin -= 6; bounds.yMax += 6;
        return bounds.Contains(new Vector2(position.x, position.z));
    }
    public static bool OutsideClearZone(Vector3 position, Vector3 center, float radius, float padding)
    {
        if (radius <= 0) return true;
        return new Vector2(position.x - center.x, position.z - center.z).sqrMagnitude >= (radius + padding) * (radius + padding);
    }
    public void RefreshAnchors() { nextDiscovery = 0; nextSelection = 0; }
    public void SetDynamicsEnabled(bool enabled) { DynamicsEnabled = enabled; nextSelection = 0; }
    [Serializable] sealed class Diagnostic
    {
        public bool componentEnabled, objectActive, dynamicsEnabled, encounterReady, paused, reducedMotion;
        public string scene;
        public Vector3 cameraPosition, cameraForward;
        public float clock;
        public int anchorCount, visibleEffects, particles;
        public AnchorDiagnostic[] anchors;
        public AmbientImportedEffect.Diagnostic[] effects;
    }
    [Serializable] sealed class AnchorDiagnostic
    {
        public string name, kind, source, lamp;
        public Vector3 position;
        public bool available, lampLit, bossArea, inFrustum, inRange, sourceFound, sourceHasLight;
        public float distance, maximumDistance;
        public int selectedSlot, sourceSystems;
    }
    /// <summary>Explicit diagnostic snapshots only; never allocated in the normal frame loop.</summary>
    public string CaptureDiagnosticJson()
    {
        var camera = Encounter != null ? Encounter.Eye : null;
        var diagnosticPlanes = new Plane[6];
        if (camera != null) GeometryUtility.CalculateFrustumPlanes(camera, diagnosticPlanes);
        var value = new Diagnostic { componentEnabled = enabled, objectActive = gameObject.activeInHierarchy,
            dynamicsEnabled = DynamicsEnabled, encounterReady = Encounter != null && Encounter.GameplayReady,
            paused = Suspended, reducedMotion = ReducedMotion, scene = gameObject.scene.name,
            cameraPosition = camera != null ? camera.transform.position : Vector3.zero,
            cameraForward = camera != null ? camera.transform.forward : Vector3.zero,
            clock = PresentationClock, anchorCount = anchors.Count, visibleEffects = ActiveEffects, particles = LiveParticleCount,
            anchors = new AnchorDiagnostic[anchors.Count], effects = new AmbientImportedEffect.Diagnostic[effects.Length] };
        for (int i = 0; i < anchors.Count; i++)
        {
            var anchor = anchors[i]; if (anchor == null) continue;
            var source = anchor.SourcePrefab != null ? anchor.SourcePrefab : Resources.Load<GameObject>(
                anchor.Kind == AmbientEffectAnchor.EffectKind.LampDust ? AmbientImportedEffect.DustSource : AmbientImportedEffect.MistSource);
            float distance = camera != null ? Vector3.Distance(anchor.Position, camera.transform.position) : -1;
            value.anchors[i] = new AnchorDiagnostic { name = anchor.name, kind = anchor.Kind.ToString(), position = anchor.Position,
                available = anchor.Available, lamp = anchor.Lamp != null ? anchor.Lamp.name : "none", lampLit = anchor.Lamp != null && anchor.Lamp.Lit,
                bossArea = IsBossArea(anchor.Position), inFrustum = camera != null && GeometryUtility.TestPlanesAABB(diagnosticPlanes,
                    new Bounds(anchor.Position, Vector3.one * anchor.BoundsRadius * 2)),
                distance = distance, maximumDistance = anchor.MaximumDistance, inRange = distance >= 0 && distance <= anchor.MaximumDistance,
                selectedSlot = Array.IndexOf(selected, anchor), sourceFound = source != null, source = source != null ? source.name : "missing",
                sourceHasLight = source != null && source.GetComponentInChildren<Light>(true) != null,
                sourceSystems = source != null ? source.GetComponentsInChildren<ParticleSystem>(true).Length : 0 };
        }
        for (int i = 0; i < effects.Length; i++) value.effects[i] = effects[i]?.CaptureDiagnostic();
        return JsonUtility.ToJson(value, true);
    }
    internal void Advance(float dt, bool paused, bool reduced, Camera camera)
    {
        bool animate = DynamicsEnabled && !reduced;
        if (!paused) PresentationClock += dt;
        float gust = .8f + .15f * Mathf.Sin(PresentationClock * .27f) + .05f * Mathf.Sin(PresentationClock * .71f);
        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        if (!paused && (Time.unscaledTime >= nextSelection || !animate))
        {
            SelectAnchors(camera, animate); nextSelection = Time.unscaledTime + .25f;
        }
        ActiveEffects = LiveParticleCount = 0;
        for (int i = 0; i < effects.Length; i++)
        {
            var effect = effects[i]; if (effect == null) continue;
            bool visible = animate && selected[i] != null && selected[i].Available;
            effect.Step(dt, gust, visible, paused);
            if (effect.Visible) { ActiveEffects++; LiveParticleCount += effect.ParticleCount; }
        }
        foreach (var sway in sways) if (sway != null)
        {
            // Pausing preserves the exact previous pose; accessibility mode restores neutral.
            if (!paused || !animate) sway.Advance(PresentationClock, gust, camera, planes, !animate);
        }
        if (wind == null) return;
        if (paused != windPaused) { windPaused = paused; if (paused) wind.Pause(); else wind.UnPause(); }
        if (paused) return;
        float nearby = 0;
        foreach (var anchor in anchors)
            if (anchor != null && anchor.isActiveAndEnabled)
                nearby = Mathf.Max(nearby, Mathf.Clamp01(1 - Vector3.Distance(anchor.Position, camera.transform.position) / anchor.MaximumDistance));
        float effectsVolume = Encounter != null && Encounter.UI != null ? Encounter.UI.Settings.Effects : .9f;
        float target = DynamicsEnabled && !IsBossArea(camera.transform.position) ? WindVolume * effectsVolume * gust * nearby : 0;
        wind.volume = Mathf.MoveTowards(wind.volume, target, dt * .045f);
    }
    void SelectAnchors(Camera camera, bool visible)
    {
        Array.Clear(selected, 0, selected.Length);
        int dustBudget = Mathf.Clamp(MaximumDust, 0, 3), mistBudget = Mathf.Clamp(MaximumMist, 0, 1);
        for (int slot = 0; slot < 4; slot++)
        {
            var kind = slot < 3 ? AmbientEffectAnchor.EffectKind.LampDust : AmbientEffectAnchor.EffectKind.SlowMist;
            bool allowed = visible && (slot < 3 ? slot < dustBudget : mistBudget > 0);
            float nearest = float.PositiveInfinity;
            if (allowed) foreach (var anchor in anchors)
            {
                if (anchor == null || !anchor.Available || anchor.Kind != kind || Array.IndexOf(selected, anchor) >= 0 ||
                    IsBossArea(anchor.Position) ||
                    !GeometryUtility.TestPlanesAABB(planes, new Bounds(anchor.Position, Vector3.one * anchor.BoundsRadius * 2))) continue;
                float distance = (anchor.Position - camera.transform.position).sqrMagnitude;
                if (distance < nearest && distance <= anchor.MaximumDistance * anchor.MaximumDistance)
                { selected[slot] = anchor; nearest = distance; }
            }
            if (selected[slot] == null) { effects[slot]?.Assign(null); continue; }
            var source = selected[slot].SourcePrefab != null ? selected[slot].SourcePrefab : Resources.Load<GameObject>(
                kind == AmbientEffectAnchor.EffectKind.LampDust ? AmbientImportedEffect.DustSource : AmbientImportedEffect.MistSource);
            // Reject effects that contain physical lights instead of adding disabled lights to the scene.
            if (source == null || source.GetComponentInChildren<Light>(true) != null)
            { selected[slot] = null; effects[slot]?.Assign(null); continue; }
            if (effects[slot] == null || effects[slot].Source != source)
            { effects[slot]?.Dispose(); effects[slot] = new AmbientImportedEffect(source, transform, kind); }
            effects[slot].Assign(selected[slot]);
        }
    }
    void OnDisable()
    {
        if (wind != null) wind.Pause(); windPaused = true;
        foreach (var effect in effects) effect?.Step(0, 0, false, true);
        foreach (var sway in sways) if (sway != null) sway.Restore();
    }
    void OnDestroy() { foreach (var effect in effects) effect?.Dispose(); }
}
