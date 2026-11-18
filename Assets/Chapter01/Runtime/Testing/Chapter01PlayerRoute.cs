using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chapter01.Runtime.Testing
{
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class Chapter01PlayerRoute : MonoBehaviour
    {
        public Camera ViewCamera;
        public Vector3[] Route = Array.Empty<Vector3>();
        public string RouteSourceSha256;
        public string CollisionRootName = "Chapter01_Collision";
        public float WalkSpeed = 3f;
        public float EyeHeight = 1.7f;
        public string Mode { get; private set; } = "manual";
        public string Phase { get; private set; } = "startup";
        public string Failure { get; private set; } = "";
        public string OutputDirectory { get; private set; }
        public int Segment { get; private set; }
        public int StartSegment { get; private set; }
        public int EndSegment { get; private set; }
        public bool Grounded => controller && controller.isGrounded;
        public float GroundGap { get; private set; } = -999;
        public float RouteDeviation { get; private set; }
        public float HeightError { get; private set; }
        public float TravelledMeters { get; private set; }
        public string GroundCollider { get; private set; } = "";
        public bool CapturePaused { get; set; }
        public float MovementBudgetBeforeCapture { get; set; } = float.PositiveInfinity;
        public bool Finished => Phase == "done" || Phase == "failed";
        public bool Automated => Mode != "manual";
        public double PhaseStarted { get; private set; }
        public float WarmupSeconds { get; private set; }
        public bool QuitAfterRun { get; private set; }

        CharacterController controller;
        Transform collisionRoot;
        Chapter01FrameRecorder recorder;
        float verticalSpeed, pitch, yaw, ungroundedSeconds, stuckSeconds;
        Vector3 lastProgressPosition;
        double started, lastProgressTime;
        bool completed;
        readonly RaycastHit[] groundHits = new RaycastHit[32];

        public static string Argument(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == key && i + 1 < args.Length) return args[i + 1];
            return fallback;
        }
        static float Number(string key, float fallback)
        { return float.TryParse(Argument(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback; }

        void Start()
        {
            controller = GetComponent<CharacterController>();
            collisionRoot = GameObject.Find(CollisionRootName)?.transform;
            Mode = Argument("--chapter01-mode", "manual").ToLowerInvariant();
            if (Mode != "manual" && Mode != "benchmark" && Mode != "visual") Mode = "manual";
            WalkSpeed = Mathf.Clamp(Number("--chapter01-speed", WalkSpeed), .5f, 6f);
            WarmupSeconds = Mathf.Max(5, Number("--chapter01-warmup-seconds", 20));
            StartSegment = Mathf.Clamp((int)Number("--chapter01-start-segment", 0), 0, Route.Length - 2);
            EndSegment = Mathf.Clamp((int)Number("--chapter01-end-segment", Route.Length - 2), StartSegment, Route.Length - 2);
            Segment = StartSegment;
            QuitAfterRun = Argument("--chapter01-quit-after-run", "false") == "true";
            OutputDirectory = Path.GetFullPath(Argument("--chapter01-output", Path.Combine(Application.persistentDataPath,
                "Chapter01-" + Mode + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"))));
            Directory.CreateDirectory(OutputDirectory);
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            OnDemandRendering.renderFrameInterval = 1;
            if (Automated) Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            started = Time.realtimeSinceStartupAsDouble;
            PhaseStarted = started;
            recorder = gameObject.AddComponent<Chapter01FrameRecorder>();
            recorder.Initialize(this);
            if (!collisionRoot || Route.Length < 2 || !ViewCamera)
            { Fail("Missing collision root, route, or player camera"); return; }
            gameObject.AddComponent<Chapter01RuntimeDiagnostics>().Initialize(this);
            controller.enabled = false;
            transform.position = Route[StartSegment] + Vector3.up * .15f;
            Physics.SyncTransforms();
            // One spawn placement only. Walking itself always uses gravity + CharacterController.Move.
            if (GroundRay(Route[StartSegment] + Vector3.up * 4, 8, out var initialHit))
                transform.position = initialHit.point + Vector3.up * .08f;
            controller.enabled = true;
            var forward = Route[StartSegment + 1] - Route[StartSegment]; forward.y = 0;
            yaw = Quaternion.LookRotation(forward.normalized).eulerAngles.y;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            ViewCamera.transform.localPosition = Vector3.up * EyeHeight;
            ViewCamera.transform.localRotation = Quaternion.Euler(2, 0, 0);
            ViewCamera.enabled = true;
            if (Mode == "visual") gameObject.AddComponent<Chapter01VisualCapture>().Initialize(this);
            SetPhase(Automated ? "warmup" : "manual");
            lastProgressPosition = transform.position;
            lastProgressTime = Time.realtimeSinceStartupAsDouble;
        }

        public void SetPhase(string phase)
        {
            Phase = phase; PhaseStarted = Time.realtimeSinceStartupAsDouble;
            Debug.Log("Chapter01 route phase=" + phase + " segment=" + Segment);
        }

        void Update()
        {
            if (Finished || !controller) return;
            if (Phase == "warmup" && Time.realtimeSinceStartupAsDouble - PhaseStarted >= WarmupSeconds)
            {
                if (Screen.width != 1920 || Screen.height != 1080)
                { Fail("Actual render resolution is " + Screen.width + "x" + Screen.height + ", expected 1920x1080"); return; }
                SetPhase(Mode == "visual" ? "visual" : "measurement");
                lastProgressPosition = transform.position; lastProgressTime = Time.realtimeSinceStartupAsDouble;
            }
            if (Phase == "manual")
            {
                if (Input.GetMouseButtonDown(0)) Cursor.lockState = CursorLockMode.Locked;
                if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    yaw += Input.GetAxis("Mouse X") * 2;
                    pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2, -75, 75);
                    transform.rotation = Quaternion.Euler(0, yaw, 0);
                    ViewCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                }
            }
            if (Automated && Time.realtimeSinceStartupAsDouble - started > 1200) Fail("Run exceeded 20 minute safety limit");
        }

        void FixedUpdate()
        {
            if (!controller || Finished || !controller.enabled || Phase == "drain") return;
            var previous = transform.position;
            Vector3 horizontal = Vector3.zero;
            bool walking = Phase == "measurement" || Phase == "visual";
            if (walking && !CapturePaused)
            {
                Vector3 to = Route[Segment + 1] - transform.position; to.y = 0;
                if (to.magnitude <= .10f)
                {
                    if (Mathf.Abs(transform.position.y - Route[Segment + 1].y) > 1.25f)
                    { Fail("Reached waypoint horizontally at incorrect elevation"); return; }
                    if (Segment >= EndSegment) { StartCoroutine(Complete()); return; }
                    Segment++;
                    to = Route[Segment + 1] - transform.position; to.y = 0;
                }
                horizontal = to.normalized * Mathf.Min(Mathf.Min(WalkSpeed * Time.fixedDeltaTime, to.magnitude), Mathf.Max(0, MovementBudgetBeforeCapture));
                if (to.sqrMagnitude > .0001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 100 * Time.fixedDeltaTime);
            }
            else if (Phase == "manual")
            {
                var input = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")), 1);
                horizontal = transform.TransformDirection(input) * WalkSpeed * (Input.GetKey(KeyCode.LeftShift) ? 1.6f : 1) * Time.fixedDeltaTime;
                if (controller.isGrounded && Input.GetKey(KeyCode.Space)) verticalSpeed = 4.5f;
            }
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -3;
            verticalSpeed = Mathf.Max(-30, verticalSpeed - 20 * Time.fixedDeltaTime);
            controller.Move(horizontal + Vector3.up * verticalSpeed * Time.fixedDeltaTime);
            float moved = Vector3.Distance(previous, transform.position);
            TravelledMeters += moved;
            if (walking && !CapturePaused && !float.IsPositiveInfinity(MovementBudgetBeforeCapture))
                MovementBudgetBeforeCapture = Mathf.Max(0, MovementBudgetBeforeCapture - moved);
            ProbeGround();
            if (!walking || CapturePaused) return;
            ungroundedSeconds = Grounded || (GroundGap >= -.12f && GroundGap < .25f) ? 0 : ungroundedSeconds + Time.fixedDeltaTime;
            if (ungroundedSeconds > 2 || HeightError < -2.5f || RouteDeviation > 2)
            { Fail("Ground contact / route corridor failure"); return; }
            if (Time.realtimeSinceStartupAsDouble - lastProgressTime >= 2)
            {
                stuckSeconds = Vector3.Distance(lastProgressPosition, transform.position) < .2f ? stuckSeconds + 2 : 0;
                lastProgressPosition = transform.position; lastProgressTime = Time.realtimeSinceStartupAsDouble;
                if (stuckSeconds >= 8) Fail("Controller blocked for 8 seconds; no automatic teleport used");
            }
        }

        bool GroundRay(Vector3 origin, float distance, out RaycastHit nearest)
        {
            nearest = default; float closest = float.PositiveInfinity;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (groundHits[i].collider && groundHits[i].transform.IsChildOf(collisionRoot) && groundHits[i].distance < closest)
                { nearest = groundHits[i]; closest = nearest.distance; }
            return closest < float.PositiveInfinity;
        }

        void ProbeGround()
        {
            if (GroundRay(transform.position + Vector3.up * 1.2f, 4, out var hit))
            { GroundGap = transform.position.y - hit.point.y; GroundCollider = hit.collider.name; }
            else { GroundGap = -999; GroundCollider = ""; }
            if (Route.Length < 2) return;
            Vector3 a = Route[Segment], b = Route[Segment + 1], p = transform.position;
            Vector3 direction = b - a; direction.y = 0;
            Vector3 delta = p - a; delta.y = 0;
            float t = Mathf.Clamp01(Vector3.Dot(delta, direction) / Mathf.Max(.0001f, direction.sqrMagnitude));
            Vector3 closest = Vector3.Lerp(a, b, t);
            HeightError = p.y - closest.y;
            p.y = closest.y; RouteDeviation = Vector3.Distance(p, closest);
        }

        public void Fail(string reason)
        {
            if (completed) return;
            Failure = reason; Debug.LogError("Chapter01 validation failed: " + reason);
            StartCoroutine(Complete());
        }

        IEnumerator Complete()
        {
            if (completed) yield break;
            completed = true; SetPhase("drain");
            yield return new WaitForSecondsRealtime(2);
            SetPhase(string.IsNullOrEmpty(Failure) ? "done" : "failed");
            recorder?.Finish();
            if (QuitAfterRun && !Application.isEditor) Application.Quit(string.IsNullOrEmpty(Failure) ? 0 : 2);
        }
    }
}
