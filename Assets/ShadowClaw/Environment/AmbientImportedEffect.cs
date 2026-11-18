using UnityEngine;
using UnityEngine.Rendering;

// Runtime instances of credited author prefabs. Source assets/materials/shaders are never edited.
internal sealed class AmbientImportedEffect
{
    internal const string DustSource = "ImportedVFX/FreeQuickEffectsVol1/Prefabs/vfx_Smoke_01";
    internal const string MistSource = "ImportedVFX/MirzaFog/Prefabs/Ground Fog";
    readonly GameObject root;
    readonly ParticleSystem[] particles;
    readonly ParticleSystemRenderer[] renderers;
    readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    readonly AmbientEffectAnchor.EffectKind kind;
    AmbientEffectAnchor owner;
    float opacity;
    bool needsWarmup = true;
    public AmbientEffectAnchor Owner => owner;
    public int ParticleCount { get { int count = 0; foreach (var p in particles) if (p != null) count += p.particleCount; return count; } }
    public bool Visible => root != null && root.activeSelf;
    public GameObject Source { get; }
    public AmbientImportedEffect(GameObject source, Transform parent, AmbientEffectAnchor.EffectKind effectKind)
    {
        Source = source; kind = effectKind;
        root = Object.Instantiate(source, parent, false); root.name = "Ambient imported " + effectKind;
        root.SetActive(false);
        foreach (var light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var audio in root.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
        foreach (var animation in root.GetComponentsInChildren<Animator>(true)) animation.enabled = false;
        particles = root.GetComponentsInChildren<ParticleSystem>(true);
        renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
        bool fog = kind == AmbientEffectAnchor.EffectKind.SlowMist;
        foreach (var system in particles)
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true; main.prewarm = false; main.playOnAwake = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = fog ? 8 : 14; main.startLifetime = fog ? 12 : 7;
            main.startSpeed = 0; main.gravityModifier = 0;
            main.startSize3D = false; main.startSize = fog ? new ParticleSystem.MinMaxCurve(1.3f, 2.1f) : new ParticleSystem.MinMaxCurve(.018f, .045f);
            main.startColor = fog ? new Color(.65f, .73f, .8f, .28f) : new Color(.85f, .79f, .65f, .48f);
            var emission = system.emission; emission.enabled = true; emission.rateOverTime = fog ? .5f : 1.5f; emission.burstCount = 0;
            var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero; shape.scale = fog ? new Vector3(2.2f, .25f, 1.2f) : new Vector3(.85f, .7f, .85f);
            var collision = system.collision; collision.enabled = false;
            var trigger = system.trigger; trigger.enabled = false;
            var lights = system.lights; lights.enabled = false;
            var noise = system.noise; noise.enabled = false;
            var velocity = system.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = .04f; velocity.y = fog ? 0 : .018f; velocity.z = .02f;
        }
        foreach (var renderer in renderers)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            // Fog's original shader uses global _Time. Freeze that animation and let the
            // manually stepped particle velocity supply motion, so menu/pause truly freezes it.
            properties.Clear(); properties.SetVector("_SimpleNoiseAnimation", Vector4.zero);
            properties.SetVector("_SimplexNoiseAnimation", Vector4.zero); properties.SetVector("_VoronoiNoiseAnimation", Vector4.zero);
            renderer.SetPropertyBlock(properties);
        }
    }
    public void Assign(AmbientEffectAnchor anchor)
    {
        if (owner == anchor) return;
        owner = anchor; opacity = 0; needsWarmup = true;
        root.SetActive(false);
        foreach (var p in particles) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (owner != null)
        {
            root.transform.position = owner.Position; root.transform.rotation = owner.transform.rotation;
            root.transform.localScale = Vector3.one * owner.Footprint;
        }
    }
    public void Step(float dt, float gust, bool show, bool paused)
    {
        if (root == null) return;
        if (owner == null || !show) { root.SetActive(false); needsWarmup = true; return; }
        if (paused) return;
        if (needsWarmup || !root.activeInHierarchy)
        {
            // Simulating an inactive prefab does not establish a running emitter.
            // Activate first, explicitly start emission, then manually advance it.
            root.SetActive(true);
            foreach (var p in particles)
            {
                p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                p.Play(false); p.Simulate(4, false, true, true); p.Pause(false);
            }
            needsWarmup = false;
        }
        opacity = Mathf.MoveTowards(opacity, Mathf.Clamp01(owner.Opacity), dt * .22f);
        foreach (var renderer in renderers)
        {
            renderer.GetPropertyBlock(properties);
            // Only Mirza uses _Albedo. Free Quick Effects colour stays in its original material.
            if (kind == AmbientEffectAnchor.EffectKind.SlowMist) properties.SetColor("_Albedo", new Color(.65f, .73f, .8f, opacity));
            renderer.SetPropertyBlock(properties);
        }
        foreach (var p in particles)
        {
            var velocity = p.velocityOverLifetime;
            velocity.x = .04f * gust; velocity.z = .02f * gust;
            var main = p.main;
            if (kind == AmbientEffectAnchor.EffectKind.LampDust) main.startColor = new Color(.85f, .79f, .65f, opacity);
            // Simulate pauses automatically. Resume before advancing so the emission
            // state survives every manual-clock step and a distance-cull round trip.
            p.Play(false); p.Simulate(dt, false, false, false); p.Pause(false);
        }
    }
    [System.Serializable] internal sealed class Diagnostic
    {
        public string source, owner;
        public bool activeSelf, activeInHierarchy, needsWarmup;
        public int count;
        public SystemDiagnostic[] systems;
    }
    [System.Serializable] internal sealed class SystemDiagnostic
    {
        public string name, shader;
        public bool active, playing, paused, stopped, emission, rendererEnabled, rendererVisible, shaderSupported;
        public int count, maximum;
        public float time, rate;
    }
    internal Diagnostic CaptureDiagnostic()
    {
        var value = new Diagnostic { source = Source != null ? Source.name : "missing", owner = owner != null ? owner.name : "none",
            activeSelf = root != null && root.activeSelf, activeInHierarchy = root != null && root.activeInHierarchy,
            needsWarmup = needsWarmup, count = ParticleCount, systems = new SystemDiagnostic[particles.Length] };
        for (int i = 0; i < particles.Length; i++)
        {
            var p = particles[i]; var renderer = p.GetComponent<ParticleSystemRenderer>(); var material = renderer != null ? renderer.sharedMaterial : null;
            value.systems[i] = new SystemDiagnostic { name = p.name, active = p.gameObject.activeInHierarchy,
                playing = p.isPlaying, paused = p.isPaused, stopped = p.isStopped, emission = p.emission.enabled,
                count = p.particleCount, maximum = p.main.maxParticles, time = p.time, rate = p.emission.rateOverTimeMultiplier,
                rendererEnabled = renderer != null && renderer.enabled, rendererVisible = renderer != null && renderer.isVisible,
                shader = material != null && material.shader != null ? material.shader.name : "missing",
                shaderSupported = material != null && material.shader != null && material.shader.isSupported };
        }
        return value;
    }
    public void Dispose() { if (root != null) Object.Destroy(root); }
}
