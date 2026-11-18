using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Drives the existing game's player in real Update frames. Never places a body,
    // substitutes a controller, invokes a fixed-step Tick loop, or changes geometry.
    [DefaultExecutionOrder(-100)]
    public sealed class UpperBridgeSamplePlayerCheck : MonoBehaviour
    {
        [Serializable] sealed class Stage
        {
            public string name, result;
            public Vector3 start, end;
            public int frames;
            public double seconds, walkedMetres;
            public float maximumGroundGap, maximumRouteHeightError;
        }
        [Serializable] sealed class BarrierResult
        {
            public int index;
            public string collider;
            public string[] actualPlayerContacts;
            public Vector3 start, end, direction, outwardNormal;
            public float allowedTravel, maximumOutwardTravel, finalHalfSecondOutwardTravel;
            public bool passed;
        }
        [Serializable] sealed class RayResult
        {
            public int index;
            public bool expectedHit, actualHit, passed, actualWeaponReleased;
            public string expectedCollider, actualCollider;
            public Vector3 authoredOrigin, actualCameraOrigin, direction, hitPoint;
            public float distance, originError;
        }
        [Serializable] sealed class Capture
        {
            public string file, kind, stage;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
        }
        [Serializable] sealed class Report
        {
            public string result = "RUNNING", failure, startedUtc, endedUtc, scene, unityVersion, cpu, gpu, graphicsApi;
            public string measurement = "Instrumented real Player Update intervals from QPC; no physical display/PresentMon measurement. Screenshots, warm-up, idle and diagnostic Camera.Render excluded. Not a repeat of the whole-castle benchmark.";
            public string controller = "Existing ShadowLab Hunter capsule, public Walk/Jump and normal ShadowLab.Update Tick; UI automated input gate only. No tester teleport or replacement controller.";
            public int width, height, vsync, targetFrameRate, antiAliasing, pathPoints, oldColliderCount, measuredFrames, unfocusedMeasuredFrames;
            public float radius, heightMetres, stepOffset, skinWidth, slopeLimit, shadowDistance, meshLodThreshold, maximumJumpRise;
            public string renderingPath;
            public bool allReplacedCollidersDisabled, walkedWithoutTeleport = true, jumpLanded, restStable;
            public double measuredSeconds, averageFps, onePercentLowFps, p95FrameMs;
            public Vector3 initialPosition, finalPosition;
            public List<Stage> stages = new List<Stage>();
            public List<BarrierResult> barriers = new List<BarrierResult>();
            public List<RayResult> rays = new List<RayResult>();
            public List<Capture> captures = new List<Capture>();
            public List<string> connectionEvidence = new List<string>();
            public string[] allowedConnectionColliders;
        }

        static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo Yaw = typeof(ShadowLab).GetField("yaw", PrivateInstance);
        static readonly FieldInfo Pitch = typeof(ShadowLab).GetField("pitch", PrivateInstance);
        static readonly PropertyInfo Automated = typeof(ShiyinUI).GetProperty("AutomatedCheck");
        readonly Report report = new Report();
        readonly List<double> intervals = new List<double>();
        readonly RaycastHit[] floorHits = new RaycastHit[64];
        readonly HashSet<string> allowedConnections = new HashSet<string>();
        ShadowLab lab;
        CharacterController body;
        UpperBridgeSampleContacts contacts;
        UpperBridgeSampleDescriptor descriptor;
        StreamWriter frames;
        string folder, phase = "startup";
        Vector3 drive, facing;
        float viewPitch = 5;
        Vector3? movementTarget;
        bool measuring, previousMeasuring, ready, finished, oldAutomated, oldRunInBackground, evidenceReady;
        double previousTimestamp, nextHeartbeat;
        Stage activeStage;
        Vector3 previousBody;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-check-upper-bridge") < 0) return;
            new GameObject("Upper bridge actual-player validation").AddComponent<UpperBridgeSamplePlayerCheck>();
        }

        void Start() { StartCoroutine(Guard(Main())); }

        IEnumerator Guard(IEnumerator run)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(run);
            while (stack.Count > 0 && !finished)
            {
                bool advanced = false; object yielded = null; Exception error = null;
                try { advanced = stack.Peek().MoveNext(); if (advanced) yielded = stack.Peek().Current; }
                catch (Exception e) { error = e; }
                if (error != null) { Fail(error); yield break; }
                if (!advanced) { stack.Pop(); continue; }
                if (yielded is IEnumerator nested) { stack.Push(nested); continue; }
                yield return yielded;
            }
        }

        IEnumerator Main()
        {
            folder = Argument("-upper-bridge-evidence");
            Require(!string.IsNullOrWhiteSpace(folder) && Path.IsPathRooted(folder), "Supply -upper-bridge-evidence <absolute directory>.");
            Directory.CreateDirectory(folder);
            Require(!File.Exists(Path.Combine(folder, "upper-bridge-check.json")), "Evidence directory already contains a report; use a new run directory.");
            evidenceReady = true;
            report.startedUtc = DateTime.UtcNow.ToString("o");
            descriptor = FindFirstObjectByType<UpperBridgeSampleDescriptor>();
            lab = FindFirstObjectByType<ShadowLab>();
            Require(descriptor && descriptor.root && lab && lab.Eye, "Sample descriptor and existing ShadowLab camera must be present.");
            Require(descriptor.path != null && descriptor.path.Length >= 6, "Sample path needs its authored world-space feet positions.");
            Require(descriptor.barrierStarts != null && descriptor.barrierStarts.Length >= 3, "Coverage requires at least three barrier push probes.");
            Require(descriptor.barrierDirections != null && descriptor.barrierDirections.Length == descriptor.barrierStarts.Length &&
                descriptor.barrierMaxTravel != null && descriptor.barrierMaxTravel.Length == descriptor.barrierStarts.Length, "Barrier arrays must have matching lengths.");
            Require(descriptor.rayStarts != null && descriptor.rayEnds != null && descriptor.rayShouldHit != null &&
                descriptor.rayStarts.Length == descriptor.rayEnds.Length && descriptor.rayStarts.Length == descriptor.rayShouldHit.Length &&
                descriptor.rayShouldHit.Count(x => x) >= 2 && descriptor.rayShouldHit.Count(x => !x) >= 2,
                "Coverage requires at least two solid and two open-slot ray probes.");
            var hall = lab.GetComponent<IronHallEncounter>();
            body = lab.transform.Find("Hunter capsule")?.GetComponent<CharacterController>();
            Require(body && hall && hall.StartWithExploration && !lab.EncounterStarted, "Use the actual exploration player and authored sample spawn.");
            Require(Mathf.Abs(body.radius - .35f) < .001f && Mathf.Abs(body.height - 1.72f) < .001f, "Current gameplay controller dimensions must remain unchanged.");
            contacts = body.gameObject.AddComponent<UpperBridgeSampleContacts>();
            Require(Horizontal(body.transform.position - descriptor.path[0]).magnitude < .3f, "Player must already start at the sample marker; tester does not teleport.");
            Require(lab.UI && Automated?.GetSetMethod(true) != null && Yaw != null && Pitch != null, "Existing UI automated gate and view fields are available.");
            oldAutomated = lab.UI.AutomatedCheck;
            Automated.GetSetMethod(true).Invoke(lab.UI, new object[] { true });
            lab.Developer.ClearPauseReasons(); lab.SetMenuPresentation(false);
            oldRunInBackground = Application.runInBackground; Application.runInBackground = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
            report.initialPosition = body.transform.position;
            report.scene = lab.gameObject.scene.path; report.unityVersion = Application.unityVersion;
            report.cpu = SystemInfo.processorType; report.gpu = SystemInfo.graphicsDeviceName; report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            report.width = Screen.width; report.height = Screen.height; report.vsync = QualitySettings.vSyncCount;
            report.targetFrameRate = Application.targetFrameRate; report.antiAliasing = QualitySettings.antiAliasing;
            report.shadowDistance = QualitySettings.shadowDistance; report.meshLodThreshold = QualitySettings.meshLodThreshold;
            report.renderingPath = lab.Eye.actualRenderingPath.ToString(); report.pathPoints = descriptor.path.Length;
            report.radius = body.radius; report.heightMetres = body.height; report.stepOffset = body.stepOffset;
            report.skinWidth = body.skinWidth; report.slopeLimit = body.slopeLimit;
            CheckOldColliders();
            CheckConnectionColliders();
            frames = new StreamWriter(Path.Combine(folder, "real-player-frames.csv"), false);
            frames.WriteLine("frame,phase,qpc_update_interval_ms,unity_delta_ms,x,y,z,grounded,jump_height,ground_gap,focused");
            ready = true; nextHeartbeat = Time.realtimeSinceStartupAsDouble + 10;
            facing = Horizontal(descriptor.path[1] - descriptor.path[0]);
            yield return Rest(2, "warm-up");
            Require(body.isGrounded, "Initial drop must settle on the sample support.");
            yield return CapturePlayer("00-start");
            yield return Traverse("centre-forward", OffsetPath(0, false));
            yield return CapturePlayer("01-upper-end");
            yield return Traverse("centre-backward", OffsetPath(0, true));
            yield return CapturePlayer("02-return");
            yield return Traverse("left-forward-turns", OffsetPath(-descriptor.safeLateralOffset, false));
            yield return Traverse("right-backward-turns", OffsetPath(descriptor.safeLateralOffset, true));
            for (int i = 0; i < descriptor.barrierStarts.Length; ++i)
            {
                yield return Navigate(descriptor.barrierStarts[i], "barrier-approach-" + i);
                yield return PushBarrier(i);
                yield return CapturePlayer("03-barrier-" + i);
            }
            for (int i = 0; i < descriptor.rayStarts.Length; ++i)
            {
                // Descriptor origins are eye-height positions reachable on the real walkway.
                yield return Navigate(descriptor.rayStarts[i] - Vector3.up * 1.65f, "ray-approach-" + i);
                yield return Rest(.3f, "ray-settle-" + i);
                yield return CheckRay(i);
                yield return CapturePlayer("04-ray-" + i);
            }
            int middle = descriptor.path.Length / 2;
            yield return Navigate(descriptor.path[middle], "jump-approach");
            yield return JumpAndRest();
            yield return CapturePlayer("05-jump-landed");
            yield return CaptureOverview();
            CheckOldColliders();
            Require(report.barriers.All(x => x.passed) && report.rays.All(x => x.passed) && report.jumpLanded && report.restStable, "Every sample check must pass.");
            report.result = "PASS";
            Finish();
        }

        void Update()
        {
            if (!ready || finished) return;
            try
            {
                double now = Chapter01NativeClock.Timestamp() / (double)Chapter01NativeClock.Frequency;
                if (measuring && previousMeasuring && previousTimestamp > 0)
                {
                    double interval = now - previousTimestamp;
                    Require(interval > 0 && !double.IsNaN(interval), "Valid monotonic real-frame interval.");
                    intervals.Add(interval); report.measuredSeconds += interval;
                    if (!Application.isFocused) ++report.unfocusedMeasuredFrames;
                    bool floor = FindGround(body.transform.position, out var ground);
                    float gap = floor ? body.transform.position.y - ground.point.y : -999;
                    frames.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F6},{3:F6},{4:F5},{5:F5},{6:F5},{7},{8:F5},{9:F5},{10}",
                        Time.frameCount, phase, interval * 1000, Time.deltaTime * 1000, body.transform.position.x, body.transform.position.y,
                        body.transform.position.z, body.isGrounded ? 1 : 0, lab.ShadowState.JumpHeight, gap, Application.isFocused ? 1 : 0));
                }
                previousTimestamp = now; previousMeasuring = measuring;
                if (movementTarget.HasValue)
                {
                    var delta = Horizontal(movementTarget.Value - body.transform.position);
                    drive = delta.normalized * Mathf.Min(1, delta.magnitude / Mathf.Max(.001f, lab.WalkSpeed * Time.deltaTime));
                    facing = phase.Contains("backward") ? -delta : delta;
                }
                if (facing.sqrMagnitude > .0001f)
                {
                    Yaw.SetValue(lab, Quaternion.LookRotation(facing).eulerAngles.y); Pitch.SetValue(lab, viewPitch);
                }
                if (drive.sqrMagnitude > .00001f) lab.Walk(drive, Time.deltaTime);
                if (Time.realtimeSinceStartupAsDouble >= nextHeartbeat)
                {
                    frames.Flush(); SaveReport(); nextHeartbeat += 10;
                    Debug.Log("UPPER_BRIDGE_HEARTBEAT phase=" + phase + " feet=" + body.transform.position.ToString("F2") + " measuredFrames=" + intervals.Count);
                }
            }
            catch (Exception e) { Fail(e); }
        }

        void LateUpdate()
        {
            if (!ready || finished || activeStage == null) return;
            ++activeStage.frames;
            activeStage.walkedMetres += Vector3.Distance(previousBody, body.transform.position);
            previousBody = body.transform.position;
        }

        IEnumerator Traverse(string name, Vector3[] points)
        {
            BeginStage(name); measuring = true;
            bool centre = name == "centre-forward" || name == "centre-backward";
            if (centre) RecordConnection(name + "-start");
            var captureIndices = name == "centre-forward" ? new HashSet<int>(new[] {
                Nearest(new Vector3(78, 52, 73)), Nearest(new Vector3(60, 54, 79)), Nearest(new Vector3(24, 56, 64)) }) : new HashSet<int>();
            for (int i = 0; i < points.Length; ++i)
            {
                yield return MoveTo(points[i]);
                if (captureIndices.Contains(i) && i + 1 < points.Length)
                {
                    measuring = previousMeasuring = false; drive = Vector3.zero;
                    facing = Horizontal(points[i + 1] - body.transform.position); viewPitch = 6;
                    if (facing.sqrMagnitude > .001f) Yaw.SetValue(lab, Quaternion.LookRotation(facing).eulerAngles.y);
                    Pitch.SetValue(lab, viewPitch); lab.RefreshView();
                    yield return CapturePlayer("01-turn-" + i.ToString("D3"));
                    phase = name; viewPitch = 5; previousMeasuring = false; measuring = true;
                }
            }
            if (centre) RecordConnection(name + "-end");
            measuring = false; drive = Vector3.zero; EndStage();
        }

        IEnumerator MoveTo(Vector3 target)
        {
            movementTarget = target;
            double start = Time.realtimeSinceStartupAsDouble, progressAt = start;
            Vector3 progressPosition = body.transform.position;
            float initialDistance = Horizontal(target - progressPosition).magnitude;
            while (Horizontal(target - body.transform.position).magnitude > .10f)
            {
                Require(!lab.EncounterStarted && !lab.Developer.Paused && !lab.Finished, "Exploration must remain active and unpaused.");
                var delta = Horizontal(target - body.transform.position);
                facing = phase.Contains("backward") ? -delta : delta;
                drive = delta.normalized * Mathf.Min(1, delta.magnitude / Mathf.Max(.001f, lab.WalkSpeed * Time.deltaTime));
                Require(Time.realtimeSinceStartupAsDouble - start < initialDistance / Mathf.Max(1, lab.WalkSpeed) * 4 + 15, "Movement timed out at " + target + ", feet=" + body.transform.position);
                if (Time.realtimeSinceStartupAsDouble - progressAt >= 3)
                {
                    Require(Horizontal(body.transform.position - progressPosition).magnitude > .08f, "Actual player blocked for three seconds: " + phase);
                    progressAt = Time.realtimeSinceStartupAsDouble; progressPosition = body.transform.position;
                }
                ValidateSupport();
                yield return null;
            }
            drive = Vector3.zero; movementTarget = null;
            float error = Mathf.Abs(body.transform.position.y - target.y);
            if (activeStage != null) activeStage.maximumRouteHeightError = Mathf.Max(activeStage.maximumRouteHeightError, error);
            Require(error <= .55f, "Reached waypoint at wrong elevation: " + target + ", feet=" + body.transform.position);
        }

        IEnumerator Navigate(Vector3 destination, string name)
        {
            // Walk back to the nearest route point, follow its connected centreline,
            // then approach the probe. No straight-line shortcuts across the void.
            int from = Nearest(body.transform.position), to = Nearest(destination);
            var points = new List<Vector3> { descriptor.path[from] };
            if (from != to) for (int i = from + Math.Sign(to - from); ; i += Math.Sign(to - from))
            { points.Add(descriptor.path[i]); if (i == to) break; }
            points.Add(destination);
            yield return Traverse(name, points.ToArray());
        }

        IEnumerator PushBarrier(int index)
        {
            BeginStage("barrier-push-" + index); measuring = false; drive = Vector3.zero;
            yield return Rest(.3f, phase);
            var start = body.transform.position;
            var direction = Horizontal(descriptor.barrierDirections[index]).normalized;
            Require(direction.sqrMagnitude > .9f, "Barrier direction is valid.");
            var low = start + body.center + Vector3.down * (body.height * .5f - body.radius);
            var high = start + body.center + Vector3.up * (body.height * .5f - body.radius);
            var hits = Physics.CapsuleCastAll(low, high, body.radius, direction, 8, ~0, QueryTriggerInteraction.Ignore)
                .Where(x => x.collider.transform.IsChildOf(descriptor.root) && !x.collider.name.StartsWith("Collision__UB_", StringComparison.Ordinal))
                .OrderBy(x => x.distance).ToArray();
            Require(hits.Length > 0, "Barrier push must have an actual sample collider ahead.");
            var normal = descriptor.barrierNormals != null && descriptor.barrierNormals.Length > index && descriptor.barrierNormals[index].sqrMagnitude > .1f
                ? Horizontal(descriptor.barrierNormals[index]).normalized : -Horizontal(hits[0].normal).normalized;
            Require(Vector3.Dot(normal, direction) > .2f, "Outward normal must point towards the pushed barrier.");
            var result = new BarrierResult { index = index, start = start, direction = direction, outwardNormal = normal,
                collider = hits[0].collider.name, allowedTravel = descriptor.barrierMaxTravel[index] };
            report.barriers.Add(result);
            Require(result.allowedTravel >= 0 && result.allowedTravel < 6, "Barrier limit must be an explicit local safety limit.");
            contacts.names.Clear(); contacts.recording = true;
            double began = Time.realtimeSinceStartupAsDouble; Vector3 halfSecondStart = start;
            const float duration = 2;
            facing = direction; drive = direction;
            while (Time.realtimeSinceStartupAsDouble - began < duration)
            {
                float outward = Vector3.Dot(body.transform.position - start, normal);
                result.maximumOutwardTravel = Mathf.Max(result.maximumOutwardTravel, outward);
                result.end = body.transform.position;
                result.actualPlayerContacts = contacts.names.ToArray();
                Require(outward <= result.allowedTravel + .12f, "Player crossed barrier envelope at probe " + index);
                if (Time.realtimeSinceStartupAsDouble - began <= duration - .5f) halfSecondStart = body.transform.position;
                ValidateSupport(); yield return null;
            }
            drive = Vector3.zero; result.end = body.transform.position;
            contacts.recording = false; result.actualPlayerContacts = contacts.names.ToArray();
            result.finalHalfSecondOutwardTravel = Vector3.Dot(result.end - halfSecondStart, normal);
            result.passed = result.maximumOutwardTravel <= result.allowedTravel + .12f && result.finalHalfSecondOutwardTravel < .08f &&
                result.actualPlayerContacts.Any(x => x.StartsWith("UB_Obstacle_", StringComparison.Ordinal));
            Require(result.passed, "Diagonal barrier push must stop outward movement while allowing sideways sliding.");
            EndStage();
        }

        IEnumerator CheckRay(int index)
        {
            phase = "actual-camera-ray-" + index; measuring = false; drive = Vector3.zero; facing = Vector3.zero;
            var vector = descriptor.rayEnds[index] - lab.Eye.transform.position;
            var rotation = Quaternion.LookRotation(vector);
            Yaw.SetValue(lab, rotation.eulerAngles.y);
            Pitch.SetValue(lab, Mathf.DeltaAngle(0, rotation.eulerAngles.x));
            lab.RefreshView(); yield return null;
            Ray ray = lab.Eye.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            float distance = Mathf.Min(40, Vector3.Distance(lab.Eye.transform.position, descriptor.rayEnds[index]));
            bool hit = Physics.Raycast(ray, out var info, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            var result = new RayResult { index = index, expectedHit = descriptor.rayShouldHit[index], actualHit = hit,
                expectedCollider = descriptor.rayExpectedColliderNames != null && descriptor.rayExpectedColliderNames.Length > index ? descriptor.rayExpectedColliderNames[index] : "",
                actualCollider = hit ? info.collider.name : "", authoredOrigin = descriptor.rayStarts[index], actualCameraOrigin = lab.Eye.transform.position,
                direction = ray.direction, distance = distance, originError = Vector3.Distance(lab.Eye.transform.position, descriptor.rayStarts[index]), hitPoint = hit ? info.point : ray.GetPoint(distance) };
            result.passed = hit == result.expectedHit && result.originError < .22f &&
                (!hit || string.IsNullOrEmpty(result.expectedCollider) || result.actualCollider == result.expectedCollider);
            // Exercise the real weapon as well; in exploration it intentionally reports
            // practice shooting. The finite slot probe only proves this railing aperture,
            // not that the full 40 m weapon ray has no scenery beyond the supplied end.
            double began = Time.realtimeSinceStartupAsDouble;
            if (lab.Weapon.Rounds == 0) lab.Weapon.Request(lab.Weapon.Loaded);
            while (lab.Weapon.Phase != HunterCrossbow.Motion.Ready || lab.Weapon.Rounds == 0)
            {
                Require(Time.realtimeSinceStartupAsDouble - began < 4, "Actual crossbow reload/cycle must finish.");
                yield return null;
            }
            began = Time.realtimeSinceStartupAsDouble;
            int rounds = lab.Weapon.Rounds;
            while (Time.realtimeSinceStartupAsDouble - began < 3 && lab.Weapon.Rounds == rounds)
            { lab.Shoot(); yield return null; }
            result.actualWeaponReleased = lab.Weapon.Rounds < rounds;
            report.rays.Add(result);
            Require(result.passed && result.actualWeaponReleased, "Camera ray/real weapon check failed at " + index + ": hit=" + result.actualCollider);
        }

        IEnumerator JumpAndRest()
        {
            phase = "jump-and-rest"; measuring = false; drive = Vector3.zero;
            yield return Rest(.5f, phase); Require(body.isGrounded, "Jump begins grounded.");
            Vector3 before = body.transform.position; lab.Jump();
            double began = Time.realtimeSinceStartupAsDouble;
            bool rose = false;
            while (Time.realtimeSinceStartupAsDouble - began < 4)
            {
                float rise = body.transform.position.y - before.y;
                report.maximumJumpRise = Mathf.Max(report.maximumJumpRise, rise); rose |= rise > .3f;
                if (rose && lab.ShadowState.JumpHeight <= .001f && body.isGrounded) break;
                yield return null;
            }
            report.jumpLanded = rose && body.isGrounded && lab.ShadowState.JumpHeight <= .001f && Mathf.Abs(body.transform.position.y - before.y) < .16f;
            Require(report.jumpLanded, "Real game Jump must rise and land on the same support.");
            Vector3 resting = body.transform.position;
            yield return Rest(2, "rest-after-jump");
            report.restStable = body.isGrounded && Vector3.Distance(resting, body.transform.position) < .08f;
            Require(report.restStable, "Idle player must remain stable on the bridge after landing.");
        }

        IEnumerator Rest(float seconds, string name)
        {
            phase = name; drive = Vector3.zero;
            double start = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - start < seconds) yield return null;
        }

        IEnumerator CapturePlayer(string label)
        {
            measuring = false; previousMeasuring = false; drive = Vector3.zero; phase = "capture-" + label;
            yield return null; yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            Require(texture != null, "Actual framebuffer capture available.");
            string file = label + "-actual-player.png";
            File.WriteAllBytes(Path.Combine(folder, file), texture.EncodeToPNG()); Destroy(texture);
            report.captures.Add(new Capture { file = file, kind = "actual rendered Player framebuffer; existing gameplay camera and UI", stage = phase,
                cameraPosition = lab.Eye.transform.position, cameraRotation = lab.Eye.transform.rotation });
            yield return null; yield return null;
        }

        IEnumerator CaptureOverview()
        {
            measuring = false; previousMeasuring = false; drive = Vector3.zero; phase = "diagnostic-overview";
            var renderers = descriptor.root.GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToArray();
            Require(renderers.Length > 0, "Sample renderers exist for diagnostic overview.");
            Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var go = new GameObject("Temporary diagnostic overview"); var camera = go.AddComponent<Camera>(); camera.CopyFrom(lab.Eye);
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = Mathf.Max(bounds.size.x / (16f / 9), bounds.size.z) * .65f;
            camera.transform.position = bounds.center + new Vector3(0, Mathf.Max(80, bounds.size.magnitude), -bounds.size.z * .15f);
            camera.transform.LookAt(bounds.center, Vector3.forward); camera.nearClipPlane = .1f;
            var rt = new RenderTexture(1600, 900, 24); var old = RenderTexture.active;
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
                string file = "06-diagnostic-overview.png"; File.WriteAllBytes(Path.Combine(folder, file), texture.EncodeToPNG()); Destroy(texture);
                report.captures.Add(new Capture { file = file, kind = "diagnostic offscreen Camera.Render; no gameplay/postprocess or FPS equivalence claimed", stage = phase,
                    cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation });
            }
            finally { RenderTexture.active = old; camera.targetTexture = null; rt.Release(); Destroy(rt); Destroy(go); }
            yield return null;
        }

        void ValidateSupport()
        {
            Require(FindGround(body.transform.position, out var hit), "Player has no sample floor beneath: " + phase + " " + body.transform.position);
            float gap = body.transform.position.y - hit.point.y;
            if (activeStage != null) activeStage.maximumGroundGap = Mathf.Max(activeStage.maximumGroundGap, Mathf.Abs(gap));
            Require(gap >= -.16f && gap <= .6f, "Feet crossed or fell away from sample support: gap=" + gap + " " + phase);
        }
        bool FindGround(Vector3 feet, out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * .35f, Vector3.down, floorHits, 3, ~0, QueryTriggerInteraction.Ignore);
            hit = default; float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
                if (((floorHits[i].collider.transform.IsChildOf(descriptor.root) && floorHits[i].collider.name.StartsWith("Collision__UB_", StringComparison.Ordinal)) ||
                    allowedConnections.Contains(floorHits[i].collider.name)) && floorHits[i].distance < nearest)
                { hit = floorHits[i]; nearest = hit.distance; }
            return !float.IsPositiveInfinity(nearest);
        }
        void CheckConnectionColliders()
        {
            Require(descriptor.allowedConnectionColliderNames != null && descriptor.allowedConnectionColliderNames.Length >= 2,
                "Both retained terminal connections must be explicitly named.");
            var all = Resources.FindObjectsOfTypeAll<Collider>().Where(x => x.gameObject.scene == lab.gameObject.scene).ToArray();
            foreach (string name in descriptor.allowedConnectionColliderNames)
            {
                Require(!descriptor.replacedColliderNames.Contains(name), "Retained connection cannot also be replaced: " + name);
                Require(all.Any(x => x.name == name && x.enabled && x.gameObject.activeInHierarchy && !x.isTrigger), "Retained connection collider must be active: " + name);
                allowedConnections.Add(name);
            }
            report.allowedConnectionColliders = allowedConnections.ToArray();
            Require(FindGround(descriptor.path[0], out var start) && allowedConnections.Contains(start.collider.name), "Extended path starts on a named old floor.");
            Require(FindGround(descriptor.path[descriptor.path.Length - 1], out var end) && allowedConnections.Contains(end.collider.name) && start.collider != end.collider,
                "Extended path ends on the other named old floor.");
        }
        void RecordConnection(string label)
        {
            Require(FindGround(body.transform.position, out var hit) && allowedConnections.Contains(hit.collider.name), "Actual player must reach retained floor at " + label);
            report.connectionEvidence.Add(label + ": " + hit.collider.name + "; feet=" + body.transform.position.ToString("F4"));
        }
        void CheckOldColliders()
        {
            Require(descriptor.replacedColliderNames != null && descriptor.replacedColliderNames.Length > 0, "Descriptor must identify replaced old colliders.");
            var all = Resources.FindObjectsOfTypeAll<Collider>().Where(x => x.gameObject.scene == lab.gameObject.scene).ToArray();
            foreach (string name in descriptor.replacedColliderNames)
            {
                var matches = all.Where(x => x.name == name).ToArray();
                Require(matches.Length > 0, "Replaced collider is missing from auditable scene: " + name);
                Require(matches.All(x => !x.enabled || !x.gameObject.activeInHierarchy), "Old replaced collider is still active: " + name);
            }
            report.oldColliderCount = descriptor.replacedColliderNames.Length; report.allReplacedCollidersDisabled = true;
        }
        Vector3[] OffsetPath(float amount, bool reverse)
        {
            var result = new Vector3[descriptor.path.Length];
            for (int i = 0; i < result.Length; ++i)
            {
                Vector3 before = Horizontal(descriptor.path[i] - descriptor.path[Mathf.Max(0, i - 1)]).normalized;
                Vector3 after = Horizontal(descriptor.path[Mathf.Min(result.Length - 1, i + 1)] - descriptor.path[i]).normalized;
                Vector3 tangent = (before + after).normalized;
                result[i] = descriptor.path[i] + Vector3.Cross(Vector3.up, tangent) * amount;
            }
            if (reverse) Array.Reverse(result); return result;
        }
        int Nearest(Vector3 point)
        {
            int index = 0; float distance = float.PositiveInfinity;
            for (int i = 0; i < descriptor.path.Length; ++i)
            { float candidate = Horizontal(descriptor.path[i] - point).sqrMagnitude; if (candidate < distance) { distance = candidate; index = i; } }
            return index;
        }
        void BeginStage(string name)
        {
            phase = name; previousBody = body.transform.position;
            activeStage = new Stage { name = name, result = "RUNNING", start = previousBody, seconds = -Time.realtimeSinceStartupAsDouble };
            report.stages.Add(activeStage);
        }
        void EndStage()
        {
            if (activeStage == null) return;
            activeStage.end = body.transform.position; activeStage.seconds += Time.realtimeSinceStartupAsDouble; activeStage.result = "PASS"; activeStage = null;
        }
        void Fail(Exception exception)
        {
            if (finished) return;
            report.result = "FAIL"; report.failure = exception.ToString();
            if (activeStage != null) { activeStage.result = "FAIL"; activeStage.end = body ? body.transform.position : Vector3.zero; activeStage.seconds += Time.realtimeSinceStartupAsDouble; activeStage = null; }
            Debug.LogException(exception); Finish();
        }
        void Finish()
        {
            if (finished) return; finished = true; measuring = false; drive = Vector3.zero;
            report.endedUtc = DateTime.UtcNow.ToString("o"); if (body) report.finalPosition = body.transform.position;
            frames?.Flush(); frames?.Dispose(); frames = null; SaveReport();
            if (lab && lab.UI && Automated?.GetSetMethod(true) != null) Automated.GetSetMethod(true).Invoke(lab.UI, new object[] { oldAutomated });
            if (ready) Application.runInBackground = oldRunInBackground;
            Debug.Log("UPPER_BRIDGE_PLAYER_CHECK_" + report.result + " frames=" + report.measuredFrames + " fps=" + report.averageFps.ToString("F2", CultureInfo.InvariantCulture));
            if (!Application.isEditor) Application.Quit(report.result == "PASS" ? 0 : 1);
        }
        void SaveReport()
        {
            if (string.IsNullOrEmpty(folder) || !evidenceReady) return;
            report.measuredFrames = intervals.Count;
            if (intervals.Count > 0)
            {
                report.averageFps = intervals.Count / report.measuredSeconds;
                var sorted = intervals.OrderBy(x => x).ToArray();
                report.p95FrameMs = sorted[Mathf.Clamp((int)Math.Ceiling(sorted.Length * .95) - 1, 0, sorted.Length - 1)] * 1000;
                int tail = Math.Max(1, (int)Math.Ceiling(sorted.Length * .01));
                report.onePercentLowFps = 1 / sorted.Skip(sorted.Length - tail).Average();
            }
            File.WriteAllText(Path.Combine(folder, "upper-bridge-check.json"), JsonUtility.ToJson(report, true));
        }
        void OnApplicationQuit()
        {
            if (finished) return;
            report.result = "CANCELLED"; report.failure = "Player quit before validation completed; partial evidence only.";
            report.endedUtc = DateTime.UtcNow.ToString("o"); if (body) report.finalPosition = body.transform.position;
            finished = true; frames?.Flush(); frames?.Dispose(); frames = null; SaveReport();
        }
        static Vector3 Horizontal(Vector3 vector) { vector.y = 0; return vector; }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }

    // Added only to the real Hunter capsule in this explicit validation run.
    // A stopped coordinate alone could be a movement-bounds clamp; require PhysX
    // contact with the sample obstacle as independent evidence of physical blocking.
    public sealed class UpperBridgeSampleContacts : MonoBehaviour
    {
        public readonly HashSet<string> names = new HashSet<string>();
        public bool recording;
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (recording && hit.collider.name.StartsWith("UB_Obstacle_", StringComparison.Ordinal)) names.Add(hit.collider.name);
        }
    }
}
