using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Opt-in ABBA sample at a fixed player view. Does not move the player or alter renderer settings.</summary>
[DisallowMultipleComponent]
public sealed class AmbientDynamicsBenchmark : MonoBehaviour
{
    public int WarmupFrames = 60;
    public int SampleFrames = 180;
    public string OutputPath;
    public bool Running { get; private set; }
    public string LastReport { get; private set; }
    AmbientDynamicsDirector director;
    bool originalEnabled;
    int phase = -1, frames;
    bool cameraMoved;
    Vector3 initialPosition;
    Quaternion initialRotation;
    readonly List<float> frameTimes = new List<float>(360);
    readonly List<float> directorTimes = new List<float>(360);
    readonly List<PhaseResult> results = new List<PhaseResult>(4);
    readonly List<LightState> lights = new List<LightState>();
    [Serializable] public sealed class PhaseResult
    {
        public bool enabled;
        public int samples, maximumParticles, maximumEffects;
        public float frameMedianMs, frameP95Ms, directorMedianMs, directorP95Ms;
    }
    [Serializable] sealed class Report
    {
        public string utc, scene, method;
        public bool validFixedView, lightsUnchanged, reducedMotion, effectsObserved;
        public int width, height, vSyncCount, targetFrameRate;
        public float frameMedianDeltaMs, directorMedianDeltaMs;
        public PhaseResult[] phases;
    }
    struct LightState
    {
        public Light source;
        public Vector3 position;
        public Quaternion rotation;
        public float intensity, range;
        public Color color;
    }
    int maximumParticles, maximumEffects;
    void Start() { if (!Running) Begin(GetComponent<AmbientDynamicsDirector>(), OutputPath); }
    public void Begin(AmbientDynamicsDirector target, string outputPath = null)
    {
        if (Running || target == null) return;
        director = target; OutputPath = outputPath; originalEnabled = target.DynamicsEnabled;
        Running = true; phase = -1; frames = 0; cameraMoved = false;
        results.Clear(); lights.Clear();
    }
    void LateUpdate()
    {
        if (!Running || director == null || director.Suspended || director.Encounter.Eye == null) return;
        var camera = director.Encounter.Eye;
        if (phase < 0)
        {
            // Wait for normal lamp discovery and startup shader/audio loading.
            if (director.PresentationClock < 6) return;
            initialPosition = camera.transform.position; initialRotation = camera.transform.rotation;
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (light.gameObject.scene == gameObject.scene) lights.Add(new LightState
                { source = light, position = light.transform.position, rotation = light.transform.rotation,
                    intensity = light.intensity, range = light.range, color = light.color });
            NextPhase(); return;
        }
        if (Vector3.Distance(camera.transform.position, initialPosition) > .04f || Quaternion.Angle(camera.transform.rotation, initialRotation) > .15f)
            cameraMoved = true;
        frames++;
        if (frames <= Mathf.Max(1, WarmupFrames)) return;
        frameTimes.Add(Time.unscaledDeltaTime * 1000f); directorTimes.Add((float)director.LastCpuMilliseconds);
        maximumParticles = Mathf.Max(maximumParticles, director.LiveParticleCount);
        maximumEffects = Mathf.Max(maximumEffects, director.ActiveEffects);
        if (frameTimes.Count < Mathf.Max(30, SampleFrames)) return;
        results.Add(new PhaseResult { enabled = phase == 1 || phase == 2, samples = frameTimes.Count,
            frameMedianMs = Percentile(frameTimes, .5f), frameP95Ms = Percentile(frameTimes, .95f),
            directorMedianMs = Percentile(directorTimes, .5f), directorP95Ms = Percentile(directorTimes, .95f),
            maximumParticles = maximumParticles, maximumEffects = maximumEffects });
        if (phase < 3) NextPhase(); else Finish();
    }
    void NextPhase()
    {
        phase++; frames = maximumParticles = maximumEffects = 0; frameTimes.Clear(); directorTimes.Clear();
        director.SetDynamicsEnabled(phase == 1 || phase == 2);
    }
    static float Percentile(List<float> values, float fraction)
    {
        var sorted = values.ToArray(); Array.Sort(sorted);
        return sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];
    }
    void Finish()
    {
        bool unchanged = true; int actualCount = 0;
        foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (light.gameObject.scene == gameObject.scene) actualCount++;
        unchanged &= actualCount == lights.Count;
        foreach (var value in lights)
            unchanged &= value.source != null && value.source.transform.position == value.position &&
                value.source.transform.rotation == value.rotation && Mathf.Approximately(value.source.intensity, value.intensity) &&
                Mathf.Approximately(value.source.range, value.range) && value.source.color == value.color;
        var report = new Report { utc = DateTime.UtcNow.ToString("O"), scene = gameObject.scene.name,
            method = "Fixed camera Off-On-On-Off; 60 warmup + 180 measured frames per phase by default. Frame delta includes rendering and VSync; CPU column only measures the director.",
            validFixedView = !cameraMoved, lightsUnchanged = unchanged, reducedMotion = director.ReducedMotion,
            effectsObserved = results[1].maximumEffects > 0 || results[2].maximumEffects > 0,
            width = Screen.width, height = Screen.height, vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
            frameMedianDeltaMs = (results[1].frameMedianMs + results[2].frameMedianMs - results[0].frameMedianMs - results[3].frameMedianMs) * .5f,
            directorMedianDeltaMs = (results[1].directorMedianMs + results[2].directorMedianMs - results[0].directorMedianMs - results[3].directorMedianMs) * .5f,
            phases = results.ToArray() };
        LastReport = JsonUtility.ToJson(report, true);
        try
        {
            string path = string.IsNullOrWhiteSpace(OutputPath) ? Path.Combine(Application.persistentDataPath, "ambient-dynamics-benchmark.json") : OutputPath;
            string folder = Path.GetDirectoryName(Path.GetFullPath(path)); Directory.CreateDirectory(folder);
            File.WriteAllText(path, LastReport); Debug.Log("AMBIENT_BENCHMARK " + Path.GetFullPath(path) + "\n" + LastReport);
        }
        catch (Exception error) { Debug.LogError("Ambient benchmark report could not be written: " + error.Message); }
        Running = false; director.SetDynamicsEnabled(originalEnabled);
    }
    void OnDisable() { if (Running && director != null) director.SetDynamicsEnabled(originalEnabled); Running = false; }
}
