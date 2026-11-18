using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

// Explicit Round3 harness helper. Never attached to or saved into the authored scene.
public static class EnvironmentRound3Probe
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] sealed class BenchmarkReport
    {
        public bool validFixedView = false, lightsUnchanged = false, reducedMotion = false, effectsObserved = false;
        public AmbientDynamicsBenchmark.PhaseResult[] phases = Array.Empty<AmbientDynamicsBenchmark.PhaseResult>();
    }
    [Serializable] sealed class Evidence
    {
        public string result, lamp, anchor, benchmark, startedUtc, completedUtc;
        public string captureMode;
        public string measurementScope = "Controlled offscreen frames: the actual Eye renders the world and its post-processing to a 1920x1080 RenderTexture every frame; a separate UI camera overlays the native Canvas without world post-processing. ABBA includes this controlled rendering workload and Unity frame scheduling, with the gameplay Update disabled by Round3. It is not the user's displayed screen, a full-game FPS measurement, or isolated GPU time; OnGUI is not captured.";
        public Vector3 cameraPosition, aim;
        public int visibleEffects, visibleParticles, renderedFramesBeforeBenchmark, renderedFramesAfterBenchmark;
        public float distance;
        public bool gameplayClockAdvanced, labUpdateDisabled, controlledLampRelight;
        public List<Chapter01.Runtime.Testing.Chapter01Round3Check.ScreenPixels> screenPixels = new List<Chapter01.Runtime.Testing.Chapter01Round3Check.ScreenPixels>();
    }
    public static IEnumerator Run(ShadowLab lab, string folder)
    {
        if (lab == null || !lab.GameplayReady || lab.Eye == null || lab.UI == null)
            throw new InvalidOperationException("Ambient probe requires the live chapter and UI.");
        if (lab.enabled || lab.GameplayPaused)
            throw new InvalidOperationException("Ambient probe requires Round3's disabled lab Update with gameplay unpaused.");
        Directory.CreateDirectory(folder);
        var director = UnityEngine.Object.FindFirstObjectByType<AmbientDynamicsDirector>();
        if (director == null || !director.enabled) throw new InvalidOperationException("Actual chapter ambient director is missing or disabled.");
        var frameCapture = Round3FrameCapture.Attach(lab);
        if (frameCapture == null || !frameCapture.isActiveAndEnabled) throw new InvalidOperationException("Round3 controlled frame renderer is unavailable.");
        var camera = lab.Eye;
        Vector3 originalPosition = camera.transform.position;
        Quaternion originalRotation = camera.transform.rotation;
        bool originalDynamics = director.DynamicsEnabled, originalReduced = lab.UI.Settings.ReducedMotion;
        int originalDust = director.MaximumDust;
        var benchmark = director.GetComponent<AmbientDynamicsBenchmark>();
        bool createdBenchmark = benchmark == null;
        bool originalBenchmarkEnabled = benchmark != null && benchmark.enabled;
        int originalWarmup = benchmark != null ? benchmark.WarmupFrames : 60;
        int originalSamples = benchmark != null ? benchmark.SampleFrames : 180;
        string originalPath = benchmark != null ? benchmark.OutputPath : null;
        EncounterLantern lamp = null;
        float originalRemaining = 0;
        bool originalInteracted = false, relit = false;
        var evidence = new Evidence { result = "RUNNING", startedUtc = DateTime.UtcNow.ToString("O"), labUpdateDisabled = !lab.enabled,
            captureMode = frameCapture.CaptureMode };
        try
        {
            // --ambient-benchmark may already have begun during earlier Round3 camera changes.
            // End that opt-in sample and start a fresh fixed-view sample here.
            if (benchmark != null) benchmark.enabled = false;
            lab.UI.Settings.ReducedMotion = false; director.MaximumDust = 3;
            director.SetDynamicsEnabled(true); director.RefreshAnchors();
            float nearest = 22 * 22;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<EncounterLantern>(FindObjectsSortMode.None))
            {
                if (!candidate.isActiveAndEnabled || !candidate.RuntimeSceneAdapter || candidate.Encounter != lab) continue;
                float distance = (candidate.Position - originalPosition).sqrMagnitude;
                if (Physics.Linecast(originalPosition, candidate.Position + Vector3.up * .65f, out var obstruction,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && candidate.transform.parent != null &&
                    obstruction.transform != candidate.transform.parent && !obstruction.transform.IsChildOf(candidate.transform.parent)) continue;
                if (distance < nearest) { nearest = distance; lamp = candidate; }
            }
            if (lamp == null) throw new InvalidOperationException("No actual ordinary lamp within 22 m of Round3's grounded Bailey view; no synthetic anchor is substituted.");
            originalRemaining = lamp.Remaining;
            originalInteracted = (bool)typeof(EncounterLantern).GetField("interacted", Private).GetValue(lamp);
            if (!lamp.Lit) { lamp.Ignite(); relit = true; }
            AmbientEffectAnchor anchor = null;
            float deadline = Time.realtimeSinceStartup + 12;
            while (anchor == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (var candidate in UnityEngine.Object.FindObjectsByType<AmbientEffectAnchor>(FindObjectsSortMode.None))
                    if (candidate.Automatic && candidate.Lamp == lamp && candidate.Kind == AmbientEffectAnchor.EffectKind.LampDust)
                    { anchor = candidate; break; }
                if (anchor == null) yield return null;
            }
            if (anchor == null) throw new InvalidOperationException("The ordinary lamp did not receive its production automatic dust anchor.");
            camera.transform.SetPositionAndRotation(originalPosition, Quaternion.LookRotation(anchor.Position - originalPosition));
            director.RefreshAnchors();
            File.WriteAllText(Path.Combine(folder, "round3-ambient-prepare.json"), director.CaptureDiagnosticJson());
            float clockBefore = director.PresentationClock;
            deadline = Time.realtimeSinceStartup + 15;
            while (Time.realtimeSinceStartup < deadline &&
                (director.PresentationClock < 6 || director.PresentationClock - clockBefore < 1.25f || director.ActiveEffects == 0 || director.LiveParticleCount == 0))
            {
                if (lab.GameplayPaused) throw new InvalidOperationException("Gameplay became paused while preparing actual ambient effects.");
                yield return null;
            }
            if (director.ActiveEffects == 0 || director.LiveParticleCount == 0 || director.PresentationClock - clockBefore < 1)
            {
                string diagnostic = director.CaptureDiagnosticJson();
                File.WriteAllText(Path.Combine(folder, "round3-ambient-failure.json"), diagnostic);
                throw new InvalidOperationException("Ambient production particles were not visible/running at the fixed real-lamp view. " +
                    "effects=" + director.ActiveEffects + ", particles=" + director.LiveParticleCount + ", clockDelta=" + (director.PresentationClock - clockBefore) +
                    "; full selection/PS state: round3-ambient-failure.json\n" + diagnostic);
            }
            File.WriteAllText(Path.Combine(folder, "round3-ambient-visible.json"), director.CaptureDiagnosticJson());
            evidence.lamp = lamp.name; evidence.anchor = anchor.name; evidence.cameraPosition = camera.transform.position;
            evidence.aim = anchor.Position; evidence.distance = Vector3.Distance(originalPosition, anchor.Position);
            evidence.visibleEffects = director.ActiveEffects; evidence.visibleParticles = director.LiveParticleCount;
            evidence.gameplayClockAdvanced = true; evidence.controlledLampRelight = relit;
            Canvas.ForceUpdateCanvases(); yield return WaitForRenderedFrame(frameCapture);
            Capture(frameCapture, Path.Combine(folder, "round3-ambient-on.png"), evidence);
            director.SetDynamicsEnabled(false); yield return null; yield return null;
            yield return WaitForRenderedFrame(frameCapture); Capture(frameCapture, Path.Combine(folder, "round3-ambient-off.png"), evidence);
            director.SetDynamicsEnabled(true);
            if (benchmark == null) benchmark = director.gameObject.AddComponent<AmbientDynamicsBenchmark>();
            benchmark.enabled = true; benchmark.WarmupFrames = 60; benchmark.SampleFrames = 180;
            string path = Path.GetFullPath(Path.Combine(folder, "round3-ambient-abba.json"));
            evidence.renderedFramesBeforeBenchmark = frameCapture.RenderedFrames;
            benchmark.Begin(director, path);
            if (!benchmark.Running) throw new InvalidOperationException("Ambient ABBA benchmark failed to start.");
            deadline = Time.realtimeSinceStartup + 120;
            while (benchmark.Running && Time.realtimeSinceStartup < deadline)
            {
                if (lab.GameplayPaused) throw new InvalidOperationException("Gameplay became paused during ambient ABBA measurement.");
                if (Vector3.Distance(camera.transform.position, originalPosition) > .04f)
                    throw new InvalidOperationException("Actual camera moved during ambient ABBA measurement.");
                yield return null;
            }
            if (benchmark.Running || string.IsNullOrEmpty(benchmark.LastReport) || !File.Exists(path))
                throw new TimeoutException("Ambient ABBA did not complete and write its measured report.");
            evidence.renderedFramesAfterBenchmark = frameCapture.RenderedFrames;
            if (evidence.renderedFramesAfterBenchmark - evidence.renderedFramesBeforeBenchmark < 720)
                throw new InvalidOperationException("Ambient ABBA did not include at least 720 actively rendered controlled offscreen frames.");
            var report = JsonUtility.FromJson<BenchmarkReport>(benchmark.LastReport);
            if (report == null || !report.validFixedView || !report.lightsUnchanged || !report.effectsObserved || report.reducedMotion ||
                report.phases == null || report.phases.Length != 4)
                throw new InvalidOperationException("Ambient ABBA flags failed: " + benchmark.LastReport);
            foreach (var phase in report.phases)
                if (phase.samples != 180 || phase.maximumEffects > 4 || phase.maximumParticles > 50 ||
                    (phase.enabled && (phase.maximumEffects == 0 || phase.maximumParticles == 0)))
                    throw new InvalidOperationException("Ambient ABBA did not measure actual visible effects within its production budget.");
            evidence.result = "PASS"; evidence.benchmark = path; evidence.completedUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(Path.Combine(folder, "round3-ambient-view.json"), JsonUtility.ToJson(evidence, true));
            Debug.Log("PASS ROUND3 ambient controlled offscreen On/Off and ABBA (not full-game FPS/GPU-only): " + path);
        }
        finally
        {
            if (benchmark != null)
            {
                benchmark.enabled = false;
                if (createdBenchmark) UnityEngine.Object.Destroy(benchmark);
                else
                {
                    benchmark.WarmupFrames = originalWarmup; benchmark.SampleFrames = originalSamples;
                    benchmark.OutputPath = originalPath; benchmark.enabled = originalBenchmarkEnabled;
                }
            }
            if (relit && lamp != null)
            {
                typeof(EncounterLantern).GetProperty("Remaining").GetSetMethod(true).Invoke(lamp, new object[] { originalRemaining });
                typeof(EncounterLantern).GetField("interacted", Private).SetValue(lamp, originalInteracted);
                typeof(EncounterLantern).GetMethod("Refresh", Private).Invoke(lamp, null);
            }
            camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
            lab.UI.Settings.ReducedMotion = originalReduced; director.MaximumDust = originalDust;
            director.SetDynamicsEnabled(originalDynamics); director.RefreshAnchors();
        }
    }
    static IEnumerator WaitForRenderedFrame(Round3FrameCapture capture)
    {
        int before = capture.RenderedFrames;
        float deadline = Time.realtimeSinceStartup + 10;
        while (capture != null && capture.isActiveAndEnabled && capture.RenderedFrames <= before && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (capture == null || capture.RenderedFrames <= before)
            throw new TimeoutException("Round3 controlled offscreen renderer did not produce a new frame.");
    }
    static void Capture(Round3FrameCapture capture, string path, Evidence evidence)
    {
        var image = capture.ReadFrame();
        try
        {
            var pixels = Chapter01.Runtime.Testing.Chapter01Round3Check.InspectScreen(image, Path.GetFileName(path));
            evidence.screenPixels.Add(pixels);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Chapter01.Runtime.Testing.Chapter01Round3Check.RequireVisibleScreen(pixels);
        }
        finally { if (image != null) UnityEngine.Object.Destroy(image); }
    }
}
