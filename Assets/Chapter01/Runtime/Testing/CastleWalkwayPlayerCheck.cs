using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Drives the existing game's player in real Update frames. Never places a body,
    // substitutes a controller, invokes a fixed-step Tick loop, or changes geometry.
    [DefaultExecutionOrder(-100)]
    public sealed class CastleWalkwayPlayerCheck : MonoBehaviour
    {
        [Serializable] sealed class Stage
        {
            public string name, result;
            public Vector3 start, end;
            public int frames;
            public double seconds, walkedMetres;
            [NonSerialized] public double began;
            public float maximumGroundGap, maximumRouteHeightError;
        }
        [Serializable] sealed class BarrierResult
        {
            public int index;
            public string collider, category, scopeId, expectedPrefix;
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
        [Serializable] sealed class ProtectedSource
        {
            public string path, expectedSha256, actualSha256, status;
        }
        [Serializable] sealed class JumpResult
        {
            public Vector3 start, end;
            public float rise;
            public bool landed, stable;
        }
        [Serializable] sealed class Report
        {
            public string buildId;
            public string runtimeAssemblySha256;
            public string result = "RUNNING", failure, startedUtc, endedUtc, scene, unityVersion, cpu, gpu, graphicsApi;
            public string measurement = "Instrumented real Player Update intervals from QPC. Screenshots, warm-up, idle, obstacle pushes and diagnostic Camera.Render excluded. Includes ordinary walking and probe approaches, not combat; no GPU or physical-display frame-time measurement. Current VSync/quality settings are reported, not overridden.";
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
            public List<ProtectedSource> protectedSources = new List<ProtectedSource>();
            public List<JumpResult> jumps = new List<JumpResult>();
            public string[] allowedGroundColliders;
            public string combatBefore, combatAfter;
            public bool combatStableComponentsUnchanged, protectedSourceFilesMatch, entranceReachedWithoutFight;
            public Vector3 entranceFeet;
            public float entranceDistanceToBattleBoundary;
            public int completedCorners, forwardPathPoints, backwardPathPoints;
            public string failedMovementPhase;
            public Vector3 failedMovementTarget, failedMovementFeet;
            public string[] failedMovementContacts;
        }

        static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo Yaw = typeof(ShadowLab).GetField("yaw", PrivateInstance);
        static readonly FieldInfo Pitch = typeof(ShadowLab).GetField("pitch", PrivateInstance);
        static readonly PropertyInfo Automated = typeof(ShiyinUI).GetProperty("AutomatedCheck");
        readonly Report report = new Report();
        readonly List<double> intervals = new List<double>();
        readonly RaycastHit[] floorHits = new RaycastHit[64];
        readonly HashSet<string> allowedGrounds = new HashSet<string>();
        readonly Stack<Stage> parentStages = new Stack<Stage>();
        Collider[] sceneColliders;
        IronHallEncounter hall;
        ShadowLab lab;
        CharacterController body;
        CastleWalkwayContacts contacts;
        CastleWalkwayDescriptor descriptor;
        StreamWriter frames;
        string folder, phase = "startup";
        Vector3 drive, facing;
        float viewPitch = 5;
        Vector3? movementTarget;
        bool measuring, previousMeasuring, ready, finished, oldAutomated, oldRunInBackground, evidenceReady;
        double previousTimestamp, nextHeartbeat;
        Stage activeStage;
        Vector3 previousBody;
        Vector3[] lastNavigationTrail = Array.Empty<Vector3>(), lastPushTrail = Array.Empty<Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-check-castle-walkway") < 0) return;
            new GameObject("Castle walkway actual-player validation").AddComponent<CastleWalkwayPlayerCheck>();
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
            folder = Argument("-castle-walkway-evidence");
            Require(!string.IsNullOrWhiteSpace(folder) && Path.IsPathRooted(folder), "Supply -castle-walkway-evidence <absolute new directory>.");
            Directory.CreateDirectory(folder);
            Require(!File.Exists(Path.Combine(folder, "castle-walkway-check.json")), "Use a fresh evidence directory; previous results are never overwritten.");
            evidenceReady = true; report.startedUtc = DateTime.UtcNow.ToString("o");
            string assemblyPath = typeof(ShadowLab).Assembly.Location;
            Require(!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath), "Actual runtime gameplay assembly must exist on disk for binary-bound evidence.");
            using (var assemblyStream = File.OpenRead(assemblyPath))
            using (var sha = SHA256.Create())
                report.runtimeAssemblySha256 = BitConverter.ToString(sha.ComputeHash(assemblyStream)).Replace("-", "").ToLowerInvariant();
            descriptor = FindFirstObjectByType<CastleWalkwayDescriptor>(); lab = FindFirstObjectByType<ShadowLab>();
            Require(descriptor && descriptor.root && lab && lab.Eye, "Authored walkway descriptor and actual game player exist.");
            report.buildId = descriptor.buildId;
            Require(!string.IsNullOrWhiteSpace(report.buildId), "Installer-generated build ID is required to bind this run to its candidate scene.");
            ValidateDescriptor();
            hall = lab.GetComponent<IronHallEncounter>();
            body = lab.transform.Find("Hunter capsule")?.GetComponent<CharacterController>();
            Require(body && hall && hall.StartWithExploration && !lab.EncounterStarted, "Use the existing exploration controller without entering combat.");
            Require(Mathf.Abs(body.radius - .35f) < .001f && Mathf.Abs(body.height - 1.72f) < .001f, "Gameplay capsule dimensions remain .35m / 1.72m.");
            Require(Horizontal(body.transform.position - descriptor.path[0]).magnitude < .35f, "Existing authored spawn must coincide with the route start; checker never teleports.");
            Require(lab.UI && Automated?.GetSetMethod(true) != null && Yaw != null && Pitch != null, "Existing automated UI gate and camera fields available.");
            contacts = body.gameObject.AddComponent<CastleWalkwayContacts>();
            oldAutomated = lab.UI.AutomatedCheck; Automated.GetSetMethod(true).Invoke(lab.UI, new object[] { true });
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
            sceneColliders = Resources.FindObjectsOfTypeAll<Collider>().Where(x => x.gameObject.scene == lab.gameObject.scene).ToArray();
            CheckOldColliders(); CheckGrounds(); CheckProtectedSources();
            report.combatBefore = CombatSnapshot();
            frames = new StreamWriter(Path.Combine(folder, "real-player-frames.csv"), false);
            frames.WriteLine("frame,phase,qpc_update_interval_ms,unity_delta_ms,x,y,z,grounded,jump_height,ground_gap,focused");
            ready = true; nextHeartbeat = Time.realtimeSinceStartupAsDouble + 10;
            facing = Horizontal(descriptor.path[1] - descriptor.path[0]);
            yield return Rest(3, "warm-up");
            Require(body.isGrounded, "Initial authored spawn settles on the approved floor.");
            yield return CapturePlayer("00-start");
            yield return ForwardWithChecks();
            CheckEntrance();
            yield return CapturePlayer("80-normal-entrance-before-battle");
            yield return Traverse("centre-backward", descriptor.path.Reverse().ToArray());
            report.backwardPathPoints = descriptor.path.Length;
            Require(Horizontal(body.transform.position - descriptor.path[0]).magnitude < .15f, "Return walk ends at the original spawn route point.");
            yield return CapturePlayer("90-return-to-start");
            yield return CaptureOverview();
            CheckOldColliders(); CheckProtectedSources(); report.combatAfter = CombatSnapshot();
            report.combatStableComponentsUnchanged = report.combatBefore == report.combatAfter;
            Require(report.combatStableComponentsUnchanged && !lab.EncounterStarted && report.entranceReachedWithoutFight,
                "Stable combat components remain unchanged and encounter never starts.");
            Require(report.barriers.Count == descriptor.barrierStarts.Length && report.barriers.All(x => x.passed) &&
                report.rays.Count == descriptor.rayStarts.Length && report.rays.All(x => x.passed && x.actualWeaponReleased) &&
                report.completedCorners == descriptor.cornerChecks.Length && report.jumps.Count == descriptor.jumpPoints.Length &&
                report.jumps.All(x => x.landed && x.stable), "All declared acceptance cases completed.");
            report.result = "PASS"; Finish();
        }

        void ValidateDescriptor()
        {
            Require(descriptor.path != null && descriptor.path.Length >= 2, "The current geometry's continuous world-feet route is required; no legacy node count is assumed.");
            int n = descriptor.barrierStarts?.Length ?? 0;
            Require(n >= 5 && descriptor.barrierDirections.Length == n && descriptor.barrierNormals.Length == n &&
                descriptor.barrierMaxTravel.Length == n && descriptor.barrierExpectedColliderPrefixes.Length == n &&
                descriptor.barrierCategories.Length == n && descriptor.barrierScopeIds.Length == n, "Obstacle arrays have matching lengths and explicit identities.");
            foreach (string category in new[] { "rail", "column", "building" })
                Require(descriptor.barrierCategories.Contains(category), "Obstacle coverage includes " + category);
            Require(descriptor.barrierExpectedColliderPrefixes.All(x => !string.IsNullOrWhiteSpace(x)), "Each obstacle has an explicit collider-name prefix.");
            Require(descriptor.requiredBarrierScopes.Length >= 3 && descriptor.requiredBarrierScopes.All(x => descriptor.barrierScopeIds.Contains(x)),
                "Every installer-declared bridge/stair/corner scope has a push probe.");
            n = descriptor.rayStarts.Length;
            Require(n == descriptor.rayEnds.Length && n == descriptor.rayShouldHit.Length && n == descriptor.rayExpectedColliderNames.Length &&
                descriptor.rayShouldHit.Count(x => x) >= 2 && descriptor.rayShouldHit.Count(x => !x) >= 2, "At least two solid and two slot actual-eye rays are required.");
            for (int i = 0; i < n; ++i) if (descriptor.rayShouldHit[i]) Require(!string.IsNullOrWhiteSpace(descriptor.rayExpectedColliderNames[i]), "Solid-ray expected collider is explicit.");
            Require(descriptor.cornerChecks.Length >= 3 && descriptor.cornerChecks.All(x => x.points != null && x.points.Length >= 3 && x.lateralOffset >= .2f && x.lateralOffset <= 1),
                "At least three representative local corner routes have safe lateral offsets.");
            Require(descriptor.screenshotPoints.Length >= 4 && descriptor.screenshotLookAt.Length == descriptor.screenshotPoints.Length, "Key-route actual-player screenshots have look targets.");
            Require(descriptor.jumpPoints.Length > 0, "At least one real jump/landing probe is required.");
            Require(Vector3.Distance(descriptor.path[descriptor.path.Length - 1], descriptor.entrancePosition) < .15f, "Route ends at the explicit safe normal entrance point.");
        }

        IEnumerator ForwardWithChecks()
        {
            BeginStage("centre-forward"); measuring = true;
            for (int i = 0; i < descriptor.path.Length; ++i)
            {
                yield return MoveTo(descriptor.path[i]); ++report.forwardPathPoints;
                bool detour = false;
                for (int j = 0; j < descriptor.screenshotPoints.Length; ++j)
                    if (Nearest(descriptor.screenshotPoints[j]) == i)
                    {
                        measuring = previousMeasuring = false; drive = Vector3.zero;
                        FacePoint(descriptor.screenshotLookAt[j]);
                        yield return CapturePlayer("10-route-" + j.ToString("D2"));
                        phase = "centre-forward"; viewPitch = 5; previousMeasuring = false; measuring = true;
                    }
                for (int j = 0; j < descriptor.cornerChecks.Length; ++j)
                {
                    var corner = descriptor.cornerChecks[j];
                    if (Nearest(corner.points[corner.points.Length / 2]) != i) continue;
                    var left = OffsetPath(corner.points, -corner.lateralOffset, false);
                    var right = OffsetPath(corner.points, corner.lateralOffset, true);
                    yield return Navigate(left[0], "corner-approach-" + j);
                    yield return Traverse("corner-left-forward-" + j, left);
                    yield return Traverse("corner-right-backward-" + j, right);
                    ++report.completedCorners; detour = true;
                }
                for (int j = 0; j < descriptor.barrierStarts.Length; ++j)
                    if (Nearest(descriptor.barrierStarts[j]) == i)
                    {
                        yield return Navigate(descriptor.barrierStarts[j], "obstacle-approach-" + j);
                        var approachReturn = lastNavigationTrail.Reverse().ToArray();
                        yield return PushBarrier(j);
                        if (j < 3 || descriptor.barrierCategories[j] != "rail") yield return CapturePlayer("30-obstacle-" + j.ToString("D2"));
                        // A push may slide along a finite wall or around a corner.
                        // Retrace the actual capsule positions before returning along
                        // the approach that it has already walked successfully.
                        yield return Traverse("obstacle-withdraw-" + j, lastPushTrail.Reverse().ToArray());
                        yield return Traverse("obstacle-return-" + j, approachReturn);
                        detour = true;
                    }
                for (int j = 0; j < descriptor.rayStarts.Length; ++j)
                    if (Nearest(descriptor.rayStarts[j] - Vector3.up * 1.65f) == i)
                    {
                        yield return Navigate(descriptor.rayStarts[j] - Vector3.up * 1.65f, "ray-approach-" + j);
                        yield return Rest(.3f, "ray-settle-" + j); yield return CheckRay(j);
                        yield return CapturePlayer("40-ray-" + j.ToString("D2")); detour = true;
                    }
                for (int j = 0; j < descriptor.jumpPoints.Length; ++j)
                    if (Nearest(descriptor.jumpPoints[j]) == i)
                    {
                        yield return Navigate(descriptor.jumpPoints[j], "jump-approach-" + j);
                        yield return JumpAndRest(); yield return CapturePlayer("50-jump-landed-" + j.ToString("D2")); detour = true;
                    }
                if (detour) yield return Navigate(descriptor.path[i], "resume-route-" + i);
                phase = "centre-forward"; measuring = true;
            }
            measuring = false; drive = Vector3.zero; EndStage();
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
                    Debug.Log("CASTLE_WALKWAY_HEARTBEAT phase=" + phase + " feet=" + body.transform.position.ToString("F2") + " measuredFrames=" + intervals.Count);
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

        IEnumerator Traverse(string name, Vector3[] points, List<Vector3> visited = null)
        {
            BeginStage(name); measuring = true;
            foreach (var point in points) yield return MoveTo(point, visited);
            measuring = false; drive = Vector3.zero; EndStage();
        }

        IEnumerator MoveTo(Vector3 target, List<Vector3> visited = null)
        {
            movementTarget = target;
            contacts.movementNames.Clear(); contacts.recordingMovement = true;
            AppendTrail(visited, body.transform.position, true);
            double start = Time.realtimeSinceStartupAsDouble, progressAt = start;
            Vector3 progressPosition = body.transform.position;
            float initialDistance = Horizontal(target - progressPosition).magnitude;
            while (Horizontal(target - body.transform.position).magnitude > .10f)
            {
                Require(!lab.EncounterStarted && !lab.Developer.Paused && !lab.Finished, "Exploration must remain active and unpaused.");
                var delta = Horizontal(target - body.transform.position);
                facing = phase.Contains("backward") ? -delta : delta;
                drive = delta.normalized * Mathf.Min(1, delta.magnitude / Mathf.Max(.001f, lab.WalkSpeed * Time.deltaTime));
                Require(Time.realtimeSinceStartupAsDouble - start < initialDistance / Mathf.Max(1, lab.WalkSpeed) * 4 + 15,
                    "Movement timed out at " + target + ", feet=" + body.transform.position + ", actual contacts=" + string.Join(", ", contacts.movementNames));
                if (Time.realtimeSinceStartupAsDouble - progressAt >= 3)
                {
                    Require(Horizontal(body.transform.position - progressPosition).magnitude > .08f,
                        "Actual player blocked for three seconds: " + phase + ", target=" + target + ", feet=" + body.transform.position +
                        ", actual contacts=" + string.Join(", ", contacts.movementNames));
                    progressAt = Time.realtimeSinceStartupAsDouble; progressPosition = body.transform.position;
                    contacts.movementNames.Clear();
                }
                ValidateSupport();
                AppendTrail(visited, body.transform.position);
                yield return null;
            }
            AppendTrail(visited, body.transform.position, true);
            contacts.recordingMovement = false;
            drive = Vector3.zero; movementTarget = null;
            float error = Mathf.Abs(body.transform.position.y - target.y);
            if (activeStage != null) activeStage.maximumRouteHeightError = Mathf.Max(activeStage.maximumRouteHeightError, error);
            Require(error <= .55f, "Reached waypoint at wrong elevation: " + target + ", feet=" + body.transform.position);
        }

        IEnumerator Navigate(Vector3 destination, string name)
        {
            // Use a horizontal continuous-polyline projection, as in the
            // installer's ClearApproach, with a same-floor guard for stacked
            // routes. Visiting every intervening vertex preserves corners.
            var fromPoint = ProjectOntoRoute(body.transform.position, out float from);
            var toPoint = ProjectOntoRoute(destination, out float to);
            var points = new List<Vector3> { fromPoint };
            if (to > from)
                for (int i = Mathf.FloorToInt(from) + 1; i < to; ++i) points.Add(descriptor.path[i]);
            else if (to < from)
                for (int i = Mathf.CeilToInt(from) - 1; i > to; --i) points.Add(descriptor.path[i]);
            points.Add(toPoint);
            points.Add(destination);
            var visited = new List<Vector3> { body.transform.position };
            yield return Traverse(name, points.ToArray(), visited);
            lastNavigationTrail = visited.ToArray();
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
            string expectedPrefix = descriptor.barrierExpectedColliderPrefixes[index];
            var hits = Physics.CapsuleCastAll(low, high, body.radius, direction, 8, ~0, QueryTriggerInteraction.Ignore)
                .Where(x => x.collider.name.StartsWith(expectedPrefix, StringComparison.Ordinal))
                .OrderBy(x => x.distance).ToArray();
            Require(hits.Length > 0, "Barrier push must have an actual sample collider ahead.");
            var normal = descriptor.barrierNormals != null && descriptor.barrierNormals.Length > index && descriptor.barrierNormals[index].sqrMagnitude > .1f
                ? Horizontal(descriptor.barrierNormals[index]).normalized : -Horizontal(hits[0].normal).normalized;
            Require(Vector3.Dot(normal, direction) > .2f, "Outward normal must point towards the pushed barrier.");
            var result = new BarrierResult { index = index, start = start, direction = direction, outwardNormal = normal,
                collider = hits[0].collider.name, allowedTravel = descriptor.barrierMaxTravel[index],
                category = descriptor.barrierCategories[index], scopeId = descriptor.barrierScopeIds[index], expectedPrefix = expectedPrefix };
            report.barriers.Add(result);
            Require(result.allowedTravel >= 0 && result.allowedTravel < 6, "Barrier limit must be an explicit local safety limit.");
            var visited = new List<Vector3> { start };
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
                AppendTrail(visited, body.transform.position);
                ValidateSupport(); yield return null;
            }
            drive = Vector3.zero; result.end = body.transform.position;
            AppendTrail(visited, body.transform.position, true); lastPushTrail = visited.ToArray();
            contacts.recording = false; result.actualPlayerContacts = contacts.names.ToArray();
            result.finalHalfSecondOutwardTravel = Vector3.Dot(result.end - halfSecondStart, normal);
            result.passed = result.maximumOutwardTravel <= result.allowedTravel + .12f && result.finalHalfSecondOutwardTravel < .08f &&
                result.actualPlayerContacts.Any(x => x.StartsWith(expectedPrefix, StringComparison.Ordinal));
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
            var jump = new JumpResult { start = before }; report.jumps.Add(jump);
            double began = Time.realtimeSinceStartupAsDouble;
            bool rose = false;
            while (Time.realtimeSinceStartupAsDouble - began < 4)
            {
                float rise = body.transform.position.y - before.y;
                report.maximumJumpRise = Mathf.Max(report.maximumJumpRise, rise); rose |= rise > .3f;
                jump.rise = Mathf.Max(jump.rise, rise);
                if (rose && lab.ShadowState.JumpHeight <= .001f && body.isGrounded) break;
                yield return null;
            }
            report.jumpLanded = rose && body.isGrounded && lab.ShadowState.JumpHeight <= .001f && Mathf.Abs(body.transform.position.y - before.y) < .16f;
            jump.landed = report.jumpLanded;
            Require(report.jumpLanded, "Real game Jump must rise and land on the same support.");
            Vector3 resting = body.transform.position;
            yield return Rest(2, "rest-after-jump");
            report.restStable = body.isGrounded && Vector3.Distance(resting, body.transform.position) < .08f;
            jump.stable = report.restStable; jump.end = body.transform.position;
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
                string file = "99-diagnostic-overview.png"; File.WriteAllBytes(Path.Combine(folder, file), texture.EncodeToPNG()); Destroy(texture);
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
                if (((floorHits[i].collider.transform.IsChildOf(descriptor.root) && floorHits[i].collider.name.StartsWith("Collision__CW_", StringComparison.Ordinal)) ||
                    allowedGrounds.Contains(floorHits[i].collider.name)) && floorHits[i].distance < nearest)
                { hit = floorHits[i]; nearest = hit.distance; }
            return !float.IsPositiveInfinity(nearest);
        }
        void CheckGrounds()
        {
            foreach (string name in descriptor.allowedGroundColliderNames)
            {
                Require(!descriptor.replacedColliderNames.Contains(name), "Retained floor cannot also be replaced: " + name);
                Require(sceneColliders.Any(x => x.name == name && x.enabled && x.gameObject.activeInHierarchy && !x.isTrigger), "Explicit retained floor is active: " + name);
                allowedGrounds.Add(name);
            }
            report.allowedGroundColliders = allowedGrounds.ToArray();
            foreach (var point in descriptor.path)
                Require(FindGround(point, out _), "Every full-route point has sample or explicitly retained support: " + point);
        }
        void CheckEntrance()
        {
            Require(hall.BattleArea && !lab.EncounterStarted && !hall.ShouldStartEncounter(body.transform.position), "Player remains outside the live battle trigger.");
            var b = hall.BattleArea.bounds; var p = body.transform.position;
            float dx = Mathf.Max(b.min.x - p.x, 0, p.x - b.max.x);
            float dz = Mathf.Max(b.min.z - p.z, 0, p.z - b.max.z);
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            Require(distance >= .4f && distance <= descriptor.entranceMaximumDistance && Horizontal(p - descriptor.entrancePosition).magnitude < .15f,
                "Actual player reaches the normal entrance with capsule clearance outside combat: " + distance);
            report.entranceReachedWithoutFight = true; report.entranceFeet = p; report.entranceDistanceToBattleBoundary = distance;
        }
        void CheckProtectedSources()
        {
            Require(descriptor.protectedAssetPaths.Length >= 3 && descriptor.protectedAssetPaths.Length == descriptor.protectedAssetSha256.Length,
                "Before-install combat source fingerprints are supplied independently by the installer.");
            report.protectedSources.Clear();
            for (int i = 0; i < descriptor.protectedAssetPaths.Length; ++i)
            {
                var record = new ProtectedSource { path = descriptor.protectedAssetPaths[i], expectedSha256 = descriptor.protectedAssetSha256[i] };
                try
                {
                    using (var sha = SHA256.Create())
                        record.actualSha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(record.path))).Replace("-", "").ToLowerInvariant();
                    record.status = string.Equals(record.actualSha256, record.expectedSha256, StringComparison.OrdinalIgnoreCase) ? "MATCH" : "CHANGED_IN_WORKSPACE";
                }
                catch (Exception e) { record.status = "UNAVAILABLE: " + e.GetType().Name; }
                report.protectedSources.Add(record);
            }
            // Another authorized chat can edit workspace sources while this frozen
            // Player runs. Record that independently; it is not a geometry failure or
            // proof that this Player contains those later source changes.
            report.protectedSourceFilesMatch = report.protectedSources.All(x => x.status == "MATCH");
            if (!report.protectedSourceFilesMatch) Debug.LogWarning("CASTLE_WALKWAY_SOURCE_FINGERPRINT_NOTICE: workspace differs from install snapshot; inspect protectedSources separately from geometry result.");
        }
        string CombatSnapshot()
        {
            Require(hall.BattleArea && hall.PlayerSpawn && hall.DemonSpawn && hall.GameplayCamera == lab.Eye &&
                hall.Lanterns != null && hall.Lanterns.Length == 6 && hall.Lanterns.All(x => x) &&
                hall.GalleryLights != null && hall.GalleryLights.Length == 6 && hall.GalleryLights.All(x => x), "Core battle region, player, demon, camera and six lamp references exist.");
            string Reference(UnityEngine.Object value) => value ? value.GetType().Name + ":" + value.name + ":" + value.GetInstanceID() : "null";
            string Pose(Transform value) => value ? Reference(value) + ":" + value.position.ToString("F5") + ":" + value.rotation.ToString("F5") : "null";
            return string.Join("|", new[] { Reference(lab), Reference(hall), Reference(hall.BattleArea), hall.BattleArea.bounds.ToString("F5"),
                hall.UseAuthoredLayout.ToString(), hall.StartWithExploration.ToString(), Pose(hall.PlayerSpawn), Pose(hall.DemonSpawn), Pose(hall.MenuView),
                Reference(hall.GameplayCamera), Reference(hall.Moonlight), string.Join(";", hall.Lanterns.Select(Reference)),
                string.Join(";", hall.GalleryLights.Select(Reference)), string.Join(";", (hall.WickAnchors ?? Array.Empty<Transform>()).Select(Pose)) });
        }
        void FacePoint(Vector3 target)
        {
            facing = Vector3.zero;
            var rotation = Quaternion.LookRotation(target - lab.Eye.transform.position);
            Yaw.SetValue(lab, rotation.eulerAngles.y); Pitch.SetValue(lab, Mathf.DeltaAngle(0, rotation.eulerAngles.x)); lab.RefreshView();
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
            var renderers = Resources.FindObjectsOfTypeAll<Renderer>().Where(x => x.gameObject.scene == lab.gameObject.scene).ToArray();
            foreach (string name in descriptor.replacedRendererNames)
            {
                var matches = renderers.Where(x => x.name == name).ToArray();
                Require(matches.Length > 0 && matches.All(x => !x.enabled || !x.gameObject.activeInHierarchy), "Replaced visible layer is still active or unauditable: " + name);
            }
        }
        Vector3[] OffsetPath(Vector3[] source, float amount, bool reverse)
        {
            var result = new Vector3[source.Length];
            for (int i = 0; i < result.Length; ++i)
            {
                Vector3 before = Horizontal(source[i] - source[Mathf.Max(0, i - 1)]).normalized;
                Vector3 after = Horizontal(source[Mathf.Min(result.Length - 1, i + 1)] - source[i]).normalized;
                Vector3 tangent = (before + after).normalized;
                result[i] = source[i] + Vector3.Cross(Vector3.up, tangent) * amount;
            }
            if (reverse) Array.Reverse(result); return result;
        }
        int Nearest(Vector3 point)
        {
            int index = 0; float distance = float.PositiveInfinity;
            for (int i = 0; i < descriptor.path.Length; ++i)
            { float candidate = (descriptor.path[i] - point).sqrMagnitude; if (candidate < distance) { distance = candidate; index = i; } }
            return index;
        }
        Vector3 ProjectOntoRoute(Vector3 point, out float routeCoordinate)
        {
            float best = float.PositiveInfinity, bestHeight = float.PositiveInfinity;
            var projected = descriptor.path[0]; routeCoordinate = 0;
            for (int i = 1; i < descriptor.path.Length; ++i)
            {
                var delta = Horizontal(descriptor.path[i] - descriptor.path[i - 1]);
                float t = Mathf.Clamp01(Vector3.Dot(Horizontal(point - descriptor.path[i - 1]), delta) / Mathf.Max(.0001f, delta.sqrMagnitude));
                var candidate = Vector3.Lerp(descriptor.path[i - 1], descriptor.path[i], t);
                float heightError = Mathf.Abs(candidate.y - point.y);
                // Preserve the existing waypoint elevation tolerance. A route
                // directly below another is never a valid return shortcut.
                if (heightError > .55f) continue;
                float distance = Horizontal(point - candidate).sqrMagnitude;
                if (distance < best - .00001f || (Mathf.Abs(distance - best) <= .00001f && heightError < bestHeight))
                { best = distance; bestHeight = heightError; projected = candidate; routeCoordinate = i - 1 + t; }
            }
            Require(!float.IsPositiveInfinity(best), "No route projection on the same floor as " + point + "; do not navigate to a different level.");
            return projected;
        }
        static void AppendTrail(List<Vector3> trail, Vector3 position, bool endpoint = false)
        {
            if (trail == null) return;
            if (trail.Count == 0 || (trail[trail.Count - 1] - position).sqrMagnitude > (endpoint ? .000001f : .01f))
                trail.Add(position);
        }
        void BeginStage(string name)
        {
            if (activeStage != null) parentStages.Push(activeStage);
            phase = name; previousBody = body.transform.position;
            activeStage = new Stage { name = name, result = "RUNNING", start = previousBody, began = Time.realtimeSinceStartupAsDouble };
            report.stages.Add(activeStage);
        }
        void EndStage()
        {
            if (activeStage == null) return;
            activeStage.end = body.transform.position; activeStage.seconds = Time.realtimeSinceStartupAsDouble - activeStage.began; activeStage.result = "PASS";
            activeStage = parentStages.Count > 0 ? parentStages.Pop() : null; previousBody = body.transform.position;
        }
        void Fail(Exception exception)
        {
            if (finished) return;
            report.result = "FAIL"; report.failure = exception.ToString();
            if (movementTarget.HasValue)
            {
                report.failedMovementPhase = phase; report.failedMovementTarget = movementTarget.Value;
                report.failedMovementFeet = body ? body.transform.position : Vector3.zero;
                report.failedMovementContacts = contacts ? contacts.movementNames.ToArray() : Array.Empty<string>();
            }
            if (activeStage != null) { activeStage.result = "FAIL"; activeStage.end = body ? body.transform.position : Vector3.zero; activeStage.seconds = Time.realtimeSinceStartupAsDouble - activeStage.began; activeStage = null; }
            foreach (var parent in parentStages) { parent.result = "ABORTED"; parent.seconds = Time.realtimeSinceStartupAsDouble - parent.began; }
            Debug.LogException(exception); Finish();
        }
        void Finish()
        {
            if (finished) return; finished = true; measuring = false; drive = Vector3.zero;
            report.endedUtc = DateTime.UtcNow.ToString("o"); if (body) report.finalPosition = body.transform.position;
            frames?.Flush(); frames?.Dispose(); frames = null; SaveReport();
            if (lab && lab.UI && Automated?.GetSetMethod(true) != null) Automated.GetSetMethod(true).Invoke(lab.UI, new object[] { oldAutomated });
            if (ready) Application.runInBackground = oldRunInBackground;
            Debug.Log("CASTLE_WALKWAY_PLAYER_CHECK_" + report.result + " frames=" + report.measuredFrames + " fps=" + report.averageFps.ToString("F2", CultureInfo.InvariantCulture));
            if (!Application.isEditor) Application.Quit(report.result == "PASS" ? 0 : 1);
        }
        void SaveReport()
        {
            if (string.IsNullOrEmpty(folder) || !evidenceReady) return;
            foreach (var stage in report.stages.Where(x => x.result == "RUNNING")) stage.seconds = Time.realtimeSinceStartupAsDouble - stage.began;
            report.measuredFrames = intervals.Count;
            if (intervals.Count > 0)
            {
                report.averageFps = intervals.Count / report.measuredSeconds;
                var sorted = intervals.OrderBy(x => x).ToArray();
                report.p95FrameMs = sorted[Mathf.Clamp((int)Math.Ceiling(sorted.Length * .95) - 1, 0, sorted.Length - 1)] * 1000;
                int tail = Math.Max(1, (int)Math.Ceiling(sorted.Length * .01));
                report.onePercentLowFps = 1 / sorted.Skip(sorted.Length - tail).Average();
            }
            File.WriteAllText(Path.Combine(folder, "castle-walkway-check.json"), JsonUtility.ToJson(report, true));
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
    public sealed class CastleWalkwayContacts : MonoBehaviour
    {
        public readonly HashSet<string> names = new HashSet<string>();
        public readonly HashSet<string> movementNames = new HashSet<string>();
        public bool recording;
        public bool recordingMovement;
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (recording) names.Add(hit.collider.name);
            if (recordingMovement) movementNames.Add(hit.collider.name);
        }
    }
}
