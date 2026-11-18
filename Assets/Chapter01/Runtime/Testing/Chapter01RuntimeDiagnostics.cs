using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chapter01.Rendering;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Chapter01.Runtime.Testing
{
    [DefaultExecutionOrder(29000)]
    public sealed class Chapter01RuntimeDiagnostics : MonoBehaviour
    {
        [Serializable] public sealed class Configuration
        {
            public string requestedRenderingPath, actualRenderingPath;
            public string note = "Only explicitly supplied CLI switches override scene/runtime defaults. Render counters may include depth/shadow/additional passes, not just unique visible geometry. Counter samples are the last completed frame, labelled separately from current frame. Deferred Built-in rendering does not support MSAA; optional FXAA is recorded.";
            public float shadowDistance, meshLodThreshold, supportBias;
            public int shadowCascades, qualityMsaa, changedSupportRenderers;
            public bool supportBiasOverridden, fxaa, fxaaActive, gpuProfilerAreaEnabled, gpuProfilerAreaRequested;
            public List<string> appliedOverrides = new List<string>();
            public List<CounterDescription> counters = new List<CounterDescription>();
        }
        [Serializable] public sealed class CounterDescription { public string name, category, unit, dataType; public bool valid; }
        struct Row { public int currentFrame, completedFrame, segment; public long qpc; public string phase; }
        sealed class Counter { public string name; public ProfilerRecorder recorder; }
        readonly List<Counter> counters = new List<Counter>();
        readonly List<Row> rows = new List<Row>(30000);
        readonly List<long> values = new List<long>(300000);
        static readonly HashSet<string> Wanted = new HashSet<string> {
            "Triangles Count", "Vertices Count", "Batches Count", "SetPass Calls Count", "Draw Calls Count",
            "Main Thread", "Render Thread", "GPU Frame Time", "Gfx.WaitForPresentOnGfxThread", "Gfx.PresentFrame",
            "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "CPU Main Thread Present Wait Time"
        };
        Chapter01PlayerRoute route;
        Configuration configuration;
        string previousPhase;
        int previousSegment;
        bool ready, finished;

        static bool Number(string key, out float value)
        { return float.TryParse(Chapter01PlayerRoute.Argument(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value); }

        public void Initialize(Chapter01PlayerRoute owner)
        {
            route = owner; configuration = new Configuration();
            var camera = route.ViewCamera;
            string path = Chapter01PlayerRoute.Argument("--chapter01-rendering-path", "").ToLowerInvariant();
            if (path == "forward") camera.renderingPath = RenderingPath.Forward;
            else if (path == "deferred") camera.renderingPath = RenderingPath.DeferredShading;
            if (path != "") configuration.appliedOverrides.Add("renderingPath=" + path);
            if (Number("--chapter01-shadow-distance", out var distance))
            { QualitySettings.shadowDistance = Mathf.Clamp(distance, 0, 250); configuration.appliedOverrides.Add("shadowDistance=" + distance); }
            if (Number("--chapter01-shadow-cascades", out var cascades) && (cascades == 0 || cascades == 2 || cascades == 4))
            { QualitySettings.shadowCascades = (int)cascades; configuration.appliedOverrides.Add("shadowCascades=" + cascades); }
            if (Number("--chapter01-lod-threshold", out var threshold))
            { QualitySettings.meshLodThreshold = Mathf.Clamp(threshold, .25f, 16); configuration.appliedOverrides.Add("meshLodThreshold=" + threshold); }
            if (Number("--chapter01-msaa", out var msaa) && (msaa == 0 || msaa == 2 || msaa == 4 || msaa == 8))
            { QualitySettings.antiAliasing = (int)msaa; configuration.appliedOverrides.Add("qualityMsaa=" + msaa); }
            if (Number("--chapter01-support-bias", out var bias))
            {
                bias = Mathf.Clamp(bias, -3, 3);
                foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (renderer.gameObject.scene == gameObject.scene && (renderer.name.StartsWith("C05_") || renderer.name.StartsWith("C07_")))
                    { renderer.meshLodSelectionBias = bias; configuration.changedSupportRenderers++; }
                configuration.supportBias = bias; configuration.supportBiasOverridden = true;
                configuration.appliedOverrides.Add("C05/C07.meshLodSelectionBias=" + bias);
            }
            string gpu = Chapter01PlayerRoute.Argument("--chapter01-gpu-profiler", "");
            if (gpu != "")
            { Profiler.SetAreaEnabled(ProfilerArea.GPU, gpu == "true"); configuration.gpuProfilerAreaRequested = gpu == "true"; configuration.appliedOverrides.Add("gpuProfilerArea=" + gpu); }
            var post = camera.GetComponent<Chapter01NightPostProcess>();
            string fxaa = Chapter01PlayerRoute.Argument("--chapter01-fxaa", path == "deferred" ? "true" : "");
            if (post && fxaa != "") { post.fxaa = fxaa == "true"; configuration.appliedOverrides.Add("fxaa=" + fxaa); }
            StartCoroutine(InitializeCounters());
        }

        IEnumerator InitializeCounters()
        {
            // Wait for the first real render to register its native counters, within warmup.
            yield return new WaitForSecondsRealtime(1);
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var available = new List<string>();
            var collected = new HashSet<string>();
            foreach (var handle in handles)
            {
                var info = ProfilerRecorderHandle.GetDescription(handle);
                available.Add(info.Category.Name + " | " + info.Name + " | " + info.UnitType + " | " + info.DataType);
                if (!Wanted.Contains(info.Name) || !collected.Add(info.Name)) continue;
                var counter = new Counter { name = info.Name, recorder = ProfilerRecorder.StartNew(info.Category, info.Name, 1) };
                counters.Add(counter);
                configuration.counters.Add(new CounterDescription { name = info.Name, category = info.Category.Name,
                    unit = info.UnitType.ToString(), dataType = info.DataType.ToString(), valid = counter.recorder.Valid });
            }
            File.WriteAllLines(Path.Combine(route.OutputDirectory, "available-profiler-counters.txt"), available.OrderBy(x => x));
            previousPhase = route.Phase; previousSegment = route.Segment; ready = true;
            SaveConfiguration();
        }

        void Update()
        {
            if (!ready || finished) return;
            rows.Add(new Row { currentFrame = Time.frameCount, completedFrame = Time.frameCount - 1,
                qpc = Chapter01NativeClock.Timestamp(), phase = previousPhase, segment = previousSegment });
            foreach (var counter in counters)
                values.Add(counter.recorder.Valid && counter.recorder.Count > 0 ? counter.recorder.LastValue : -1);
            previousPhase = route.Phase; previousSegment = route.Segment;
        }

        void SaveConfiguration()
        {
            configuration.requestedRenderingPath = route.ViewCamera.renderingPath.ToString();
            configuration.actualRenderingPath = route.ViewCamera.actualRenderingPath.ToString();
            configuration.shadowDistance = QualitySettings.shadowDistance; configuration.shadowCascades = QualitySettings.shadowCascades;
            configuration.meshLodThreshold = QualitySettings.meshLodThreshold; configuration.qualityMsaa = QualitySettings.antiAliasing;
            var post = route.ViewCamera.GetComponent<Chapter01NightPostProcess>(); configuration.fxaa = post && post.fxaa;
            configuration.fxaaActive = post && post.FxaaActive;
            configuration.gpuProfilerAreaEnabled = Profiler.GetAreaEnabled(ProfilerArea.GPU);
            File.WriteAllText(Path.Combine(route.OutputDirectory, "diagnostic-configuration.json"), JsonUtility.ToJson(configuration, true));
        }

        public void Finish()
        {
            if (finished) return; finished = true;
            SaveConfiguration();
            using (var writer = new StreamWriter(Path.Combine(route.OutputDirectory, "render-counters.csv")))
            {
                writer.Write("currentFrame,completedFrame,qpc,phase,segment");
                foreach (var counter in counters) writer.Write(",\"" + counter.name + "\"");
                writer.WriteLine(); int offset = 0;
                foreach (var row in rows)
                {
                    writer.Write(string.Join(",", row.currentFrame, row.completedFrame, row.qpc, row.phase, row.segment));
                    for (int i = 0; i < counters.Count; i++) writer.Write("," + values[offset++].ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine();
                }
            }
            foreach (var counter in counters) counter.recorder.Dispose();
        }
        void OnApplicationQuit() { if (route) Finish(); }
    }
}
