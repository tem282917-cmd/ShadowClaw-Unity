using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Chapter01.Runtime.Testing
{
    // Collect actual Player-loop / engine frame data. No off-screen rendering is timed here.
    [DefaultExecutionOrder(30000)]
    public sealed class Chapter01FrameRecorder : MonoBehaviour
    {
        struct Frame
        {
            public int frame, segment, width, height;
            public long qpc;
            public double realtime, loopMs;
            public string phase, ground;
            public Vector3 feet;
            public bool focused, grounded;
            public float gap, deviation, heightError, travelled;
        }
        struct Timing { public FrameTiming timing; public long receivedQpc; public int receivedFrame, lastUpdatedFrame; }
        [Serializable] public sealed class Metadata
        {
            public string timestampUtc, mode, finalPhase, failure, routeSourceSha256, unityVersion, operatingSystem;
            public string processor, graphicsDevice, graphicsVersion, graphicsApi, colorSpace, scenePath;
            public string frameClockBackend;
            public string timingExplanation = "frames.csv uses actual Player Update-to-Update wall intervals. timing.csv contains delayed FrameTimingManager data deduplicated by frameStartTimestamp. PresentCalled is an API-call timestamp, not a physical display timestamp. GPU zeros are unavailable, never counted as zero cost. Benchmark contains no screenshot readback/encoding. Warmup is stationary; measurement is first traversal and retains first-use stutters.";
            public string lowDefinition = "1% low FPS = 1000 / arithmetic mean of slowest ceil(1% of valid frame intervals); average FPS = 1000 / mean interval; p95 uses nearest rank.";
            public int processorCount, systemMemoryMB, graphicsMemoryMB, width, height, antiAliasing, vSyncCount, renderFrameInterval, targetFrameRate, frameCount, timingCount, startSegment, endSegment;
            public float speedMetersPerSecond, warmupSeconds, travelledMeters, meshLodThreshold, shadowDistance;
            public string shadowResolution, shadowQuality;
            public bool developmentBuild, frameTimingEnabled, gpuRecorderSupported, routeCompleted, resolutionMatches;
            public long stopwatchFrequency;
            public long frameClockFrequency;
            public ulong cpuTimerFrequency, gpuTimerFrequency;
        }
        readonly List<Frame> frames = new List<Frame>(60000);
        readonly List<Timing> timings = new List<Timing>(60000);
        readonly Dictionary<ulong, int> timingIndices = new Dictionary<ulong, int>();
        readonly FrameTiming[] scratch = new FrameTiming[32];
        Chapter01PlayerRoute route;
        long previousQpc;
        double nextHeartbeat;
        bool finished;
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        public void Initialize(Chapter01PlayerRoute owner)
        { route = owner; previousQpc = Chapter01NativeClock.Timestamp(); nextHeartbeat = Time.realtimeSinceStartupAsDouble + 10; }

        void Update()
        {
            if (!route || finished) return;
            long now = Chapter01NativeClock.Timestamp();
            frames.Add(new Frame {
                frame = Time.frameCount, qpc = now, realtime = Time.realtimeSinceStartupAsDouble,
                loopMs = (now - previousQpc) * 1000d / Chapter01NativeClock.Frequency,
                phase = route.Phase, segment = route.Segment, feet = route.transform.position,
                focused = Application.isFocused, grounded = route.Grounded, gap = route.GroundGap,
                deviation = route.RouteDeviation, heightError = route.HeightError, travelled = route.TravelledMeters,
                ground = route.GroundCollider, width = Screen.width, height = Screen.height
            });
            previousQpc = now;
            FrameTimingManager.CaptureFrameTimings();
            CollectTimings();
            if (Time.realtimeSinceStartupAsDouble >= nextHeartbeat)
            {
                nextHeartbeat = Time.realtimeSinceStartupAsDouble + 10;
                Heartbeat(now);
            }
        }

        void Heartbeat(long now)
        {
            long cutoff = now - 2 * Chapter01NativeClock.Frequency;
            double loop = 0, cpu = 0, gpu = 0;
            int count = 0, cpuCount = 0, gpuCount = 0;
            for (int i = frames.Count - 1; i >= 0 && frames[i].qpc >= cutoff; i--)
                if (frames[i].phase == route.Phase && frames[i].loopMs > 0) { loop += frames[i].loopMs; count++; }
            for (int i = timings.Count - 1; i >= 0 && timings[i].receivedQpc >= cutoff; i--)
            {
                if (timings[i].timing.cpuFrameTime > 0) { cpu += timings[i].timing.cpuFrameTime; cpuCount++; }
                if (timings[i].timing.gpuFrameTime > 0) { gpu += timings[i].timing.gpuFrameTime; gpuCount++; }
            }
            string cpuText = cpuCount > 0 ? (cpu / cpuCount).ToString("F2", CI) : "unavailable";
            string gpuText = gpuCount > 0 ? (gpu / gpuCount).ToString("F2", CI) : "unavailable";
            Debug.Log("CHAPTER01_HEARTBEAT phase=" + route.Phase + " segment=" + route.Segment +
                " feet=" + route.transform.position.ToString("F2") + " recentLoopFPS=" + (loop > 0 ? (1000 * count / loop).ToString("F2", CI) : "unavailable") +
                " recentCPUms=" + cpuText + " recentGPUms=" + gpuText + " grounded=" + route.Grounded +
                " gap=" + route.GroundGap.ToString("F3", CI) + " travelled=" + route.TravelledMeters.ToString("F1", CI));
        }

        void CollectTimings()
        {
            uint n = FrameTimingManager.GetLatestTimings((uint)scratch.Length, scratch);
            for (int i = 0; i < n; i++)
            {
                var current = scratch[i];
                if (current.frameStartTimestamp == 0) continue;
                if (timingIndices.TryGetValue(current.frameStartTimestamp, out int index))
                {
                    var previous = timings[index];
                    // GPU/render-thread results can arrive after the CPU result for the same frame.
                    // Keep refreshing records; first-seen deduplication would silently discard them.
                    if (current.gpuFrameTime <= 0) current.gpuFrameTime = previous.timing.gpuFrameTime;
                    if (current.cpuRenderThreadFrameTime <= 0) current.cpuRenderThreadFrameTime = previous.timing.cpuRenderThreadFrameTime;
                    if (current.firstSubmitTimestamp == 0) current.firstSubmitTimestamp = previous.timing.firstSubmitTimestamp;
                    if (current.cpuTimePresentCalled == 0) current.cpuTimePresentCalled = previous.timing.cpuTimePresentCalled;
                    if (current.cpuTimeFrameComplete == 0) current.cpuTimeFrameComplete = previous.timing.cpuTimeFrameComplete;
                    previous.timing = current; previous.lastUpdatedFrame = Time.frameCount; timings[index] = previous;
                }
                else
                {
                    timingIndices.Add(current.frameStartTimestamp, timings.Count);
                    timings.Add(new Timing { timing = current, receivedQpc = Chapter01NativeClock.Timestamp(),
                        receivedFrame = Time.frameCount, lastUpdatedFrame = Time.frameCount });
                }
            }
        }

        public void Finish()
        {
            if (finished || !route) return;
            finished = true; CollectTimings();
            GetComponent<Chapter01RuntimeDiagnostics>()?.Finish();
            Directory.CreateDirectory(route.OutputDirectory);
            using (var w = new StreamWriter(Path.Combine(route.OutputDirectory, "frames.csv"), false, new UTF8Encoding(false)))
            {
                w.WriteLine("frame,qpc,realtimeSeconds,loopMs,phase,segment,x,y,z,focused,grounded,groundGap,routeDeviation,heightError,travelledMeters,width,height,groundCollider");
                foreach (var f in frames)
                    w.WriteLine(string.Join(",", f.frame, f.qpc, N(f.realtime), N(f.loopMs), f.phase, f.segment,
                        N(f.feet.x), N(f.feet.y), N(f.feet.z), f.focused ? 1 : 0, f.grounded ? 1 : 0,
                        N(f.gap), N(f.deviation), N(f.heightError), N(f.travelled), f.width, f.height, Quote(f.ground)));
            }
            timings.Sort((a, b) => a.timing.frameStartTimestamp.CompareTo(b.timing.frameStartTimestamp));
            using (var w = new StreamWriter(Path.Combine(route.OutputDirectory, "timing.csv"), false, new UTF8Encoding(false)))
            {
                w.WriteLine("frameStartTimestamp,receivedQpc,receivedFrame,cpuFrameMs,cpuMainThreadMs,cpuRenderThreadMs,presentWaitMs,gpuFrameMs,firstSubmitTimestamp,cpuTimePresentCalled,cpuTimeFrameComplete,syncInterval,widthScale,heightScale,lastUpdatedFrame");
                foreach (var row in timings)
                {
                    var t = row.timing;
                    w.WriteLine(string.Join(",", t.frameStartTimestamp, row.receivedQpc, row.receivedFrame,
                        N(t.cpuFrameTime), N(t.cpuMainThreadFrameTime), N(t.cpuRenderThreadFrameTime), N(t.cpuMainThreadPresentWaitTime),
                        N(t.gpuFrameTime), t.firstSubmitTimestamp, t.cpuTimePresentCalled, t.cpuTimeFrameComplete,
                        t.syncInterval, N(t.widthScale), N(t.heightScale), row.lastUpdatedFrame));
                }
            }
            var metadata = new Metadata {
                timestampUtc = DateTime.UtcNow.ToString("o"), mode = route.Mode, finalPhase = route.Phase, failure = route.Failure,
                routeSourceSha256 = route.RouteSourceSha256, unityVersion = Application.unityVersion,
                operatingSystem = SystemInfo.operatingSystem, processor = SystemInfo.processorType, processorCount = SystemInfo.processorCount,
                graphicsDevice = SystemInfo.graphicsDeviceName, graphicsVersion = SystemInfo.graphicsDeviceVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), systemMemoryMB = SystemInfo.systemMemorySize,
                graphicsMemoryMB = SystemInfo.graphicsMemorySize, width = Screen.width, height = Screen.height,
                antiAliasing = QualitySettings.antiAliasing, colorSpace = QualitySettings.activeColorSpace.ToString(),
                vSyncCount = QualitySettings.vSyncCount, renderFrameInterval = OnDemandRendering.renderFrameInterval,
                targetFrameRate = Application.targetFrameRate, developmentBuild = Debug.isDebugBuild,
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), gpuRecorderSupported = SystemInfo.supportsGpuRecorder,
                cpuTimerFrequency = FrameTimingManager.GetCpuTimerFrequency(), gpuTimerFrequency = FrameTimingManager.GetGpuTimerFrequency(),
                stopwatchFrequency = Stopwatch.Frequency, frameCount = frames.Count, timingCount = timings.Count,
                frameClockFrequency = Chapter01NativeClock.Frequency, frameClockBackend = Chapter01NativeClock.Backend,
                startSegment = route.StartSegment, endSegment = route.EndSegment, speedMetersPerSecond = route.WalkSpeed,
                warmupSeconds = route.WarmupSeconds, travelledMeters = route.TravelledMeters,
                meshLodThreshold = QualitySettings.meshLodThreshold, routeCompleted = route.Phase == "done",
                shadowDistance = QualitySettings.shadowDistance, shadowResolution = QualitySettings.shadowResolution.ToString(),
                shadowQuality = QualitySettings.shadows.ToString(),
                resolutionMatches = Screen.width == 1920 && Screen.height == 1080, scenePath = gameObject.scene.path
            };
            File.WriteAllText(Path.Combine(route.OutputDirectory, "runtime-metadata.json"), JsonUtility.ToJson(metadata, true));
            Debug.Log("Chapter01 runtime evidence saved: " + route.OutputDirectory);
        }
        static string N(double value) => value.ToString("R", CI);
        static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        void OnApplicationQuit() => Finish();
    }
}
