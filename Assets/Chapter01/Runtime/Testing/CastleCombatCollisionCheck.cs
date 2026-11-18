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
    // Explicit verification only. Uses the existing player, normal ShadowLab.Update,
    // live boss AI and real weapon. The only reset is the game's final F5-equivalent.
    [DefaultExecutionOrder(-100)]
    public sealed class CastleCombatCollisionCheck : MonoBehaviour
    {
        [Serializable] sealed class WalkResult
        {
            public string name, result;
            public Vector3 start, end;
            public float metres, statusEffectSeconds;
            public double seconds;
        }
        [Serializable] sealed class PillarResult
        {
            public string column;
            public Vector3 start, end, direction;
            public float movement;
            public string[] actualContacts;
            public CastleCombatContactEvidence[] contactDetails;
            public bool passed;
        }
        [Serializable] sealed class LampResult
        {
            public int slot;
            public string target, firstHit, shot;
            public Vector3 feet, eye, wick;
            public float remainingBefore, remainingAfter, protectedAfter;
            public bool actualWeaponReleased, passed;
            public List<string> viewAttempts = new List<string>();
        }
        [Serializable] sealed class EnemyResult
        {
            public string column, result = "NOT_RUN";
            public bool contactObserved, clearLineObserved, recovered;
            public double observationSeconds, blockedMovingSeconds;
            public float movedAfterPlayerReposition;
            public Vector3 beforeRelease, afterRelease;
            public string[] actualContacts;
            public CastleCombatContactEvidence[] contactDetails;
        }
        [Serializable] sealed class Capture
        {
            public string file, phase, kind = "Actual Player framebuffer, existing gameplay camera and UI";
            public Vector3 eye;
            public Quaternion rotation;
            public string weaponMotion;
            public float weaponPhaseProgress;
            public int weaponRounds;
        }
        [Serializable] sealed class Report
        {
            public string result = "RUNNING", failure, buildId, startedUtc, endedUtc, scene, cpu, gpu;
            public string runtimeAssemblySha256;
            public string control = "Real game Walk/Jump/Tick and live AI; existing F2 health lock enabled for collision testing. No tester teleport, AI pause, collision disabling, lamp-state injection or AI code changes. Final RestartEncounter is the normal F5 reset.";
            public string timing = "Real QPC Update intervals during walking, including active combat; screenshot/idle/shooting excluded. Instrumented verification, not an uncapped GPU/physical-display benchmark.";
            public int width, height, vsync, measuredFrames, unfocusedFrames;
            public bool existingHealthLockUsed, naturalCombatEntry, noTestTeleportBeforeRestart = true, rampUpAndDown, restartedAtMountain, aiAndCollidersRetained;
            public double measuredSeconds, averageFps;
            public Vector3 initialFeet, entryFeet, rampTopFeet, finalFeet;
            public List<WalkResult> walks = new List<WalkResult>();
            public List<PillarResult> pillars = new List<PillarResult>();
            public List<LampResult> lamps = new List<LampResult>();
            public EnemyResult enemy = new EnemyResult();
            public List<Capture> captures = new List<Capture>();
            public List<string> bossActionsObserved = new List<string>();
        }

        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo Yaw = typeof(ShadowLab).GetField("yaw", Private), Pitch = typeof(ShadowLab).GetField("pitch", Private);
        static readonly FieldInfo Lamps = typeof(ShadowLab).GetField("lamps", Private);
        static readonly PropertyInfo Automated = typeof(ShiyinUI).GetProperty("AutomatedCheck");
        readonly Report report = new Report();
        ShadowLab lab;
        IronHallEncounter hall;
        CastleWalkwayDescriptor descriptor;
        CharacterController player, enemy;
        CastleCombatContacts playerContacts, enemyContacts;
        Collider[] ground, originalObstacles;
        bool[] obstacleEnabled, obstacleActive;
        StreamWriter frames;
        string folder, phase = "startup";
        bool ready, finished, evidenceReady, oldAuto, oldLock, oldBackground, measuring, previousMeasuring, aiming;
        Vector3? goal;
        Vector3 push;
        double previousClock, heartbeat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-check-castle-combat") < 0) return;
            new GameObject("Castle combat collision verification").AddComponent<CastleCombatCollisionCheck>();
        }
        void Start() { StartCoroutine(Guard(Run())); }
        IEnumerator Guard(IEnumerator first)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(first);
            while (stack.Count > 0 && !finished)
            {
                bool next = false; object value = null; Exception error = null;
                try { next = stack.Peek().MoveNext(); if (next) value = stack.Peek().Current; }
                catch (Exception exception) { error = exception; }
                if (error != null) { Fail(error); yield break; }
                if (!next) { stack.Pop(); continue; }
                if (value is IEnumerator nested) { stack.Push(nested); continue; }
                yield return value;
            }
        }
        IEnumerator Run()
        {
            folder = Argument("-castle-combat-evidence");
            Require(!string.IsNullOrWhiteSpace(folder) && Path.IsPathRooted(folder), "Supply -castle-combat-evidence <fresh absolute folder>.");
            Directory.CreateDirectory(folder);
            Require(!File.Exists(Path.Combine(folder, "castle-combat-check.json")), "Existing run evidence must not be overwritten.");
            evidenceReady = true; report.startedUtc = DateTime.UtcNow.ToString("o");
            string assemblyPath = typeof(ShadowLab).Assembly.Location;
            Require(!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath), "Actual runtime gameplay assembly must exist on disk for binary-bound evidence.");
            using (var assemblyStream = File.OpenRead(assemblyPath))
            using (var sha = SHA256.Create())
                report.runtimeAssemblySha256 = BitConverter.ToString(sha.ComputeHash(assemblyStream)).Replace("-", "").ToLowerInvariant();
            descriptor = FindFirstObjectByType<CastleWalkwayDescriptor>(); lab = FindFirstObjectByType<ShadowLab>();
            Require(descriptor && lab && lab.UI && lab.Eye, "Current castle descriptor and real gameplay system exist.");
            report.buildId = descriptor.buildId; Require(!string.IsNullOrWhiteSpace(report.buildId), "Candidate build ID is present.");
            hall = lab.GetComponent<IronHallEncounter>();
            player = lab.transform.Find("Hunter capsule")?.GetComponent<CharacterController>();
            enemy = lab.transform.Find("Demon capsule")?.GetComponent<CharacterController>();
            Require(hall && player && enemy && hall.StartWithExploration && !lab.EncounterStarted, "Real player starts at original exploration spawn.");
            Require(descriptor.path.Length >= 2 && Horizontal(player.transform.position - descriptor.path[0]).magnitude < .35f, "Actual spawn matches the current route; tester does not reposition it.");
            ground = hall.GroundColliders.Where(x => x && x.enabled && x.gameObject.activeInHierarchy && !x.isTrigger).ToArray();
            originalObstacles = Resources.FindObjectsOfTypeAll<Collider>().Where(x => x.gameObject.scene == lab.gameObject.scene && x.name.StartsWith("CW_Obstacle_", StringComparison.Ordinal)).ToArray();
            obstacleEnabled = originalObstacles.Select(x => x.enabled).ToArray();
            obstacleActive = originalObstacles.Select(x => x.gameObject.activeInHierarchy).ToArray();
            foreach (int side in new[] { -1, 1 }) foreach (int z in new[] { 98, 113 }) Pillar(side, z);
            playerContacts = player.gameObject.AddComponent<CastleCombatContacts>();
            enemyContacts = enemy.gameObject.AddComponent<CastleCombatContacts>();
            oldAuto = lab.UI.AutomatedCheck; Automated.GetSetMethod(true).Invoke(lab.UI, new object[] { true });
            lab.Developer.ClearPauseReasons(); lab.SetMenuPresentation(false);
            oldLock = lab.Developer.HealthLocked;
            if (!oldLock) lab.Developer.HandleKey(KeyCode.F2);
            report.existingHealthLockUsed = lab.Developer.HealthLocked;
            Require(report.existingHealthLockUsed && !lab.Developer.Frozen, "Only existing F2 health lock is used; AI clock remains live.");
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
            report.initialFeet = player.transform.position; report.scene = lab.gameObject.scene.path;
            report.cpu = SystemInfo.processorType; report.gpu = SystemInfo.graphicsDeviceName;
            report.width = Screen.width; report.height = Screen.height; report.vsync = QualitySettings.vSyncCount;
            frames = new StreamWriter(Path.Combine(folder, "combat-real-frames.csv"), false);
            frames.WriteLine("frame,phase,qpc_interval_ms,measured,px,py,pz,ex,ey,ez,action,lit,health,pin,slow,enemy_contact");
            ready = true; heartbeat = Time.realtimeSinceStartupAsDouble + 10;
            yield return Rest(2, "warm-up");
            yield return CapturePlayer("00-crossbow-normal-hold");
            yield return Walk("mountain-to-entry", descriptor.path, false);
            Require(!lab.EncounterStarted, "Full approach finishes before the live encounter trigger.");
            var battle = hall.BattleArea.bounds;
            yield return Walk("natural-entry", new[] { Feet(0, battle.min.z + 2) }, true);
            Require(lab.EncounterStarted, "Normal grounded movement naturally starts the encounter.");
            report.naturalCombatEntry = true; report.entryFeet = player.transform.position;
            yield return CapturePlayer("01-natural-entry");

            // The columns are centred at x +/-13; the inner aisle at +/-10.8
            // passes their actual 2.2m bases without walking through their centres.
            foreach (int side in new[] { -1, 1 })
            {
                yield return Walk("aisle-" + side, new[] { Feet(0, 94), Feet(side * 10.8f, 94), Feet(side * 10.8f, 96), Feet(side * 10.8f, 115.5f) }, true);
                foreach (int z in new[] { 113, 98 }) yield return CheckPillar(side, z);
                yield return CapturePlayer("02-aisle-" + side);
                yield return Walk("aisle-return-" + side, new[] { Feet(side * 10.8f, 94), Feet(0, 94) }, true);
            }
            yield return ObserveEnemyAtPillar();

            // Six existing RoomSource sphere targets remain authoritative; no lamp
            // is switched on directly by this checker.
            var sources = (Array)Lamps.GetValue(lab);
            Require(sources.Length == 6, "Original six combat light targets remain present.");
            for (int i = 0; i < sources.Length; ++i) yield return ShootOriginalLamp(i, sources.GetValue(i));

            yield return Walk("ramp-centre-ascent", new[] { Feet(0, 110), Feet(0, 125), Feet(0, 132), Feet(0, 136), Feet(0, 140), Feet(0, 143) }, true);
            report.rampTopFeet = player.transform.position;
            yield return CapturePlayer("05-apse-ramp-top");
            yield return Walk("ramp-centre-descent", new[] { Feet(0, 140), Feet(0, 136), Feet(0, 132), Feet(0, 125) }, true);
            report.rampUpAndDown = true;
            VerifyLiveSystems();

            phase = "normal-F5-restart"; measuring = false; goal = null; push = Vector3.zero;
            lab.RestartEncounter(); // Public production reset; same ResetLab path as F5.
            yield return Rest(1, phase);
            report.restartedAtMountain = !lab.EncounterStarted && player.isGrounded && lab.HunterHealth == 100 && lab.Damage == 0 &&
                Horizontal(player.transform.position - hall.HunterStart).magnitude < .12f && Mathf.Abs(player.transform.position.y - report.initialFeet.y) < .2f;
            Require(report.restartedAtMountain, "Normal F5-equivalent restart restores the original mountain spawn and exploration state.");
            if (lab.Developer.HealthLocked != oldLock) lab.Developer.HandleKey(KeyCode.F2);
            yield return CapturePlayer("06-normal-restart-mountain");
            VerifyLiveSystems();
            Require(!report.enemy.contactObserved || report.enemy.clearLineObserved, "Column contact occurred but an unobstructed recovery condition was not established; do not call this an AI recovery pass.");
            Require(report.enemy.result != "PERSISTENT_BLOCK_AFTER_CLEAR_LINE", "Existing AI remains blocked after the player leaves column cover; see enemy evidence.");
            report.result = "PASS"; Finish();
        }

        void Update()
        {
            if (!ready || finished) return;
            try
            {
                double now = Chapter01NativeClock.Timestamp() / (double)Chapter01NativeClock.Frequency;
                double interval = previousClock > 0 ? now - previousClock : 0;
                bool count = measuring && previousMeasuring && interval > 0;
                if (count) { ++report.measuredFrames; report.measuredSeconds += interval; if (!Application.isFocused) ++report.unfocusedFrames; }
                previousClock = now; previousMeasuring = measuring;
                if (lab.EncounterStarted && !report.bossActionsObserved.Contains(lab.BossAction.ToString())) report.bossActionsObserved.Add(lab.BossAction.ToString());
                frames.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F6},{3},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10},{11},{12},{13:F4},{14:F4},{15}",
                    Time.frameCount, phase, interval * 1000, count ? 1 : 0, player.transform.position.x, player.transform.position.y, player.transform.position.z,
                    enemy.transform.position.x, enemy.transform.position.y, enemy.transform.position.z, lab.BossAction, lab.LitLamps, lab.HunterHealth,
                    lab.PinRemaining, lab.SlowRemaining, enemyContacts.LatestName(.2)));
                Vector3 direction = push;
                if (goal.HasValue)
                {
                    var delta = Horizontal(goal.Value - player.transform.position);
                    direction = delta.normalized * Mathf.Min(1, delta.magnitude / Mathf.Max(.001f, lab.WalkSpeed * Time.deltaTime));
                }
                if (direction.sqrMagnitude > .00001f)
                {
                    if (!aiming) { Yaw.SetValue(lab, Quaternion.LookRotation(direction).eulerAngles.y); Pitch.SetValue(lab, 5f); }
                    lab.Walk(direction, Time.deltaTime);
                }
                if (Time.realtimeSinceStartupAsDouble >= heartbeat)
                {
                    heartbeat += 10; frames.Flush(); Save();
                    Debug.Log("CASTLE_COMBAT_HEARTBEAT phase=" + phase + " feet=" + player.transform.position.ToString("F2") + " enemy=" + enemy.transform.position.ToString("F2") + " action=" + lab.BossAction);
                }
            }
            catch (Exception e) { Fail(e); }
        }
        IEnumerator Walk(string name, Vector3[] points, bool allowCombat)
        {
            phase = name; measuring = true; aiming = false;
            var result = new WalkResult { name = name, result = "RUNNING", start = player.transform.position }; report.walks.Add(result);
            double began = Time.realtimeSinceStartupAsDouble;
            foreach (var target in points)
            {
                goal = target; Vector3 progress = player.transform.position, previous = progress;
                double progressAt = Time.realtimeSinceStartupAsDouble, started = progressAt;
                while (Horizontal(target - player.transform.position).magnitude > .12f)
                {
                    Require(!lab.Developer.Paused && !lab.Finished && (!lab.EncounterStarted || allowCombat), "Normal game clock and expected encounter state remain active.");
                    ValidateFloor();
                    result.metres += Vector3.Distance(previous, player.transform.position); previous = player.transform.position;
                    if (lab.PinRemaining > 0 || lab.SlowRemaining > 0) result.statusEffectSeconds += Time.deltaTime;
                    if (lab.PinRemaining > 0) { progressAt = Time.realtimeSinceStartupAsDouble; progress = player.transform.position; }
                    if (Time.realtimeSinceStartupAsDouble - progressAt >= 5)
                    {
                        Require(Horizontal(player.transform.position - progress).magnitude > .08f,
                            "Player blocked for five unpinned seconds in " + phase + "; contacts=" + string.Join(";", playerContacts.RecentNames(1)));
                        progressAt = Time.realtimeSinceStartupAsDouble; progress = player.transform.position;
                    }
                    Require(Time.realtimeSinceStartupAsDouble - started < 120, "Single movement target exceeded real-time budget: " + target);
                    yield return null;
                }
                goal = null; Require(Mathf.Abs(player.transform.position.y - target.y) <= .6f, "Walk target reached on correct actual floor: " + target);
            }
            goal = null; measuring = false; result.end = player.transform.position; result.seconds = Time.realtimeSinceStartupAsDouble - began; result.result = "PASS";
        }
        IEnumerator CheckPillar(int side, int z)
        {
            var colliders = Pillar(side, z);
            Vector3 approach = Feet(side * 10.8f, z);
            yield return Walk("column-approach-" + side + "-" + z, new[] { Feet(side * 10.8f, z) }, true);
            phase = "column-push-" + side + "-" + z; measuring = false;
            Vector3 direction = Vector3.right * side; var start = player.transform.position;
            var hits = colliders.Select(c => { bool hit = c.Raycast(new Ray(start + Vector3.up * .85f, direction), out var h, 4); return (hit, h); })
                .Where(x => x.hit).OrderBy(x => x.h.distance).ToArray();
            Require(hits.Length > 0, "Actual authored column face lies ahead.");
            float allowed = hits[0].h.distance - player.radius;
            var result = new PillarResult { column = hits[0].h.collider.name, direction = direction, start = start }; report.pillars.Add(result);
            playerContacts.Clear(); double began = Time.realtimeSinceStartupAsDouble; push = direction;
            while (Time.realtimeSinceStartupAsDouble - began < 1.4)
            {
                result.movement = Mathf.Max(result.movement, Vector3.Dot(player.transform.position - start, direction));
                Require(result.movement <= allowed + .14f, "Player crossed a physical nave column."); ValidateFloor(); yield return null;
            }
            push = Vector3.zero; result.end = player.transform.position; result.actualContacts = playerContacts.RecentNames(2);
            result.contactDetails = playerContacts.RecentEvidence(2);
            result.passed = result.actualContacts.Any(n => colliders.Any(c => c.name == n)) && result.movement <= allowed + .14f;
            Require(result.passed, "Real player capsule made physical contact with nave column.");
            yield return Walk("column-step-away-" + side + "-" + z, new[] { approach }, true);
        }

        IEnumerator ObserveEnemyAtPillar()
        {
            var column = Pillar(1, 113); report.enemy.column = string.Join(";", column.Select(x => x.name));
            yield return Walk("column-cover-lure", new[] { Feet(0, 110.5f), Feet(10.8f, 110.5f), Feet(14.9f, 110.5f), Feet(14.9f, 113) }, true);
            enemyContacts.Clear(); phase = "enemy-live-column-observation"; measuring = false;
            double began = Time.realtimeSinceStartupAsDouble; Vector3 previous = enemy.transform.position;
            while (Time.realtimeSinceStartupAsDouble - began < 12)
            {
                bool contact = enemyContacts.RecentNames(.2).Any(n => column.Any(c => c.name == n));
                report.enemy.contactObserved |= contact;
                if (contact && MovingAction(lab.BossAction) && Horizontal(enemy.transform.position - previous).magnitude < .008f)
                    report.enemy.blockedMovingSeconds += Time.deltaTime;
                previous = enemy.transform.position; yield return null;
            }
            report.enemy.observationSeconds = Time.realtimeSinceStartupAsDouble - began;
            report.enemy.actualContacts = enemyContacts.RecentNames(30); report.enemy.beforeRelease = enemy.transform.position;
            report.enemy.contactDetails = enemyContacts.RecentEvidence(30);
            yield return CapturePlayer("03-enemy-column-cover");
            yield return Walk("leave-column-cover", new[] { Feet(14.9f, 110.5f), Feet(10.8f, 110.5f) }, true);
            var clearCandidates = new[] { 108f, 118f, 102f, 124f }.Select(z => Feet(8.5f, z))
                .Where(p => !ColumnBlocks(column, enemy.transform.position, p)).OrderBy(p => Horizontal(p - player.transform.position).sqrMagnitude).ToArray();
            Require(clearCandidates.Length > 0, "A supported recovery position with clear column sight line is available.");
            var recoveryPoint = clearCandidates[0];
            yield return Walk("clear-column-sight-line", new[] { Feet(10.8f, recoveryPoint.z), recoveryPoint }, true);
            phase = "enemy-live-clear-line-recovery"; measuring = false; began = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - began < 12)
            {
                bool blocked = ColumnBlocks(column, enemy.transform.position, player.transform.position);
                report.enemy.contactObserved |= enemyContacts.RecentNames(.2).Any(n => column.Any(c => c.name == n));
                report.enemy.clearLineObserved |= !blocked;
                report.enemy.movedAfterPlayerReposition = Mathf.Max(report.enemy.movedAfterPlayerReposition, Horizontal(enemy.transform.position - report.enemy.beforeRelease).magnitude);
                report.enemy.recovered |= !blocked && report.enemy.movedAfterPlayerReposition > 1;
                yield return null;
            }
            report.enemy.afterRelease = enemy.transform.position;
            report.enemy.result = !report.enemy.contactObserved ? "NO_COLUMN_BLOCK_OBSERVED" : !report.enemy.clearLineObserved ? "OCCLUSION_NOT_CLEARED" :
                report.enemy.recovered ? "RECOVERED_AFTER_PLAYER_REPOSITION" : "PERSISTENT_BLOCK_AFTER_CLEAR_LINE";
            if (report.enemy.result == "PERSISTENT_BLOCK_AFTER_CLEAR_LINE") Debug.LogError("CASTLE_COMBAT_EXISTING_AI_COLUMN_BLOCK: preserved live evidence; no AI change attempted.");
            yield return CapturePlayer("03-enemy-after-leaving-cover");
            yield return Walk("return-centre-after-cover", new[] { Feet(8.5f, 110), Feet(0, 110) }, true);
        }

        IEnumerator ShootOriginalLamp(int slot, object source)
        {
            var type = source.GetType(); var target = (Collider)type.GetField("Target").GetValue(source);
            Vector3 wick = hall.WickPosition(slot); float side = Mathf.Sign(wick.x);
            var result = new LampResult { slot = slot, target = target.name, wick = wick }; report.lamps.Add(result);
            yield return Walk("lamp-bay-" + slot, new[] { Feet(0, 110), Feet(side * 10.5f, 110) }, true);
            bool clear = false;
            foreach (float offset in new[] { 2.8f, -2.8f, 4.5f, -4.5f })
            {
                float z = Mathf.Clamp(wick.z + offset, hall.BattleArea.bounds.min.z + 2, hall.BattleArea.bounds.max.z - 2);
                yield return Walk("lamp-eye-position-" + slot, new[] { Feet(side * 10.5f, z), Feet(side * 11, z) }, true);
                Aim(target.bounds.center); yield return null;
                Ray ray = lab.Eye.ViewportPointToRay(new Vector3(.5f, .5f, 0));
                bool struck = Physics.Raycast(ray, out var h, 40, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                result.firstHit = struck ? h.collider.name : "MISS";
                result.viewAttempts.Add("feet=" + player.transform.position.ToString("F4") + "; firstHit=" + result.firstHit);
                clear = struck && h.collider == target;
                if (clear) break;
                yield return Walk("lamp-view-adjust-" + slot, new[] { Feet(side * 10.5f, z) }, true);
            }
            result.feet = player.transform.position; result.eye = lab.Eye.transform.position;
            if (!clear) yield return CapturePlayer("FAIL-lamp-" + slot);
            Require(clear, "No reachable original eye ray reaches lamp " + slot + "; first blocker=" + result.firstHit);
            phase = "actual-lamp-shot-" + slot; measuring = false; Aim(target.bounds.center);
            if (lab.Weapon.Rounds == 0) lab.Weapon.Request(lab.Weapon.Loaded);
            double began = Time.realtimeSinceStartupAsDouble;
            while (lab.Weapon.Phase != HunterCrossbow.Motion.Ready || lab.Weapon.Rounds == 0)
            { Require(Time.realtimeSinceStartupAsDouble - began < 4, "Existing crossbow finishes normal reload/cycle."); yield return null; }
            if (slot == 0)
            {
                Aim(target.bounds.center);
                yield return CapturePlayer("04-crossbow-aim-original-lamp");
            }
            Aim(target.bounds.center); lab.RefreshView();
            var aimRay = lab.Eye.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            bool hit = Physics.Raycast(aimRay, out var hitInfo, 40, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            result.firstHit = hit ? hitInfo.collider.name : "MISS";
            Require(hit && hitInfo.collider == target, "Real weapon's first ray hit is the original lamp Target.");
            result.remainingBefore = (float)type.GetField("Remaining").GetValue(source);
            int rounds = lab.Weapon.Rounds; lab.Shoot(); yield return null;
            result.actualWeaponReleased = lab.Weapon.Rounds == rounds - 1; result.shot = lab.LastShot;
            result.remainingAfter = (float)type.GetField("Remaining").GetValue(source); result.protectedAfter = (float)type.GetField("Protected").GetValue(source);
            result.passed = result.actualWeaponReleased && result.remainingAfter > 22 && result.protectedAfter > 2;
            Require(result.passed, "Original ShootLamp interaction responds to a real arrow, without test state injection.");
            yield return CapturePlayer("04-lamp-" + slot);
            if (slot == 0) yield return CaptureNormalReload();
            yield return Walk("lamp-return-to-centre-" + slot, new[] { Feet(side * 10.5f, player.transform.position.z), Feet(side * 10.5f, 110), Feet(0, 110) }, true);
        }

        Collider[] Pillar(int side, int z)
        {
            var result = originalObstacles.Where(c => c.enabled && c.name.Contains("Interior_Column_") &&
                Mathf.Abs(c.bounds.center.x - side * 13) < .5f && Mathf.Abs(c.bounds.center.z - z) < .5f &&
                c.bounds.min.y < 69.6f && c.bounds.max.y > 68.4f).ToArray();
            Require(result.Length > 0, "New physical nave column exists at " + side * 13 + "," + z); return result;
        }
        Vector3 Feet(float x, float z)
        {
            float y = float.NegativeInfinity;
            foreach (var c in ground)
            {
                var b = c.bounds; if (x < b.min.x || x > b.max.x || z < b.min.z || z > b.max.z) continue;
                if (c.Raycast(new Ray(new Vector3(x, b.max.y + .5f, z), Vector3.down), out var hit, b.size.y + 1) && hit.normal.y > .6f) y = Mathf.Max(y, hit.point.y);
            }
            Require(!float.IsNegativeInfinity(y), "Authored combat floor exists at " + x + "," + z);
            return new Vector3(x, y + .02f, z);
        }
        void ValidateFloor()
        {
            Vector3 p = player.transform.position;
            var hits = Physics.RaycastAll(p + Vector3.up * .35f, Vector3.down, 3, ~0, QueryTriggerInteraction.Ignore)
                .Where(x => ground.Contains(x.collider)).OrderBy(x => x.distance).ToArray();
            Require(hits.Length > 0 && p.y - hits[0].point.y >= -.16f && p.y - hits[0].point.y <= .65f,
                "Real player remains supported by the original/generated ground: " + phase + " " + p);
        }
        void VerifyLiveSystems()
        {
            Require(lab.enabled && enemy.enabled && player.enabled && !lab.Developer.Paused && !lab.Developer.Frozen && Time.timeScale > 0 && report.bossActionsObserved.Count >= 2,
                "Player, AI clock and real controllers remain enabled; multiple live AI actions were observed.");
            for (int i = 0; i < originalObstacles.Length; ++i) Require(originalObstacles[i] && originalObstacles[i].enabled == obstacleEnabled[i] &&
                originalObstacles[i].gameObject.activeInHierarchy == obstacleActive[i], "New obstacle active/enabled state was not changed by the test.");
            report.aiAndCollidersRetained = true;
        }
        void Aim(Vector3 point)
        {
            aiming = true; var rotation = Quaternion.LookRotation(point - lab.Eye.transform.position);
            Yaw.SetValue(lab, rotation.eulerAngles.y); Pitch.SetValue(lab, Mathf.DeltaAngle(0, rotation.eulerAngles.x)); lab.RefreshView();
        }
        IEnumerator Rest(float duration, string name)
        {
            phase = name; measuring = false; goal = null; push = Vector3.zero;
            double began = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - began < duration) yield return null;
        }
        IEnumerator CapturePlayer(string name)
        {
            measuring = previousMeasuring = false; goal = null; push = Vector3.zero; phase = "capture-" + name;
            yield return null; yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture(); string file = name + ".png";
            File.WriteAllBytes(Path.Combine(folder, file), texture.EncodeToPNG()); Destroy(texture);
            report.captures.Add(new Capture { file = file, phase = phase, eye = lab.Eye.transform.position, rotation = lab.Eye.transform.rotation,
                weaponMotion = lab.Weapon.Phase.ToString(), weaponPhaseProgress = lab.Weapon.PhaseProgress, weaponRounds = lab.Weapon.Rounds });
            yield return null; yield return null;
        }
        IEnumerator CaptureNormalReload()
        {
            phase = "normal-weapon-reload-visual-check"; measuring = false;
            double began = Time.realtimeSinceStartupAsDouble;
            while (lab.Weapon.Phase != HunterCrossbow.Motion.Ready)
            { Require(Time.realtimeSinceStartupAsDouble - began < 3, "Existing shot cycle reaches Ready before normal reload."); yield return null; }
            Require(lab.Weapon.Rounds < HunterCrossbow.Capacity, "A real fired bolt creates space for normal R-equivalent reload.");
            lab.Weapon.Request(lab.Weapon.Loaded);
            began = Time.realtimeSinceStartupAsDouble;
            while (lab.Weapon.Phase != HunterCrossbow.Motion.Load || lab.Weapon.PhaseProgress < .3f)
            { Require(Time.realtimeSinceStartupAsDouble - began < 3, "Normal reload reaches its visible loading phase."); yield return null; }
            yield return CapturePlayer("04-crossbow-normal-reload-middle");
            began = Time.realtimeSinceStartupAsDouble;
            while (lab.Weapon.Phase != HunterCrossbow.Motion.Ready)
            { Require(Time.realtimeSinceStartupAsDouble - began < 3, "Normal reload finishes after visual capture."); yield return null; }
            Require(lab.Weapon.Rounds == HunterCrossbow.Capacity, "Existing reload refills the weapon normally.");
        }
        void Fail(Exception e)
        {
            if (finished) return;
            report.result = "FAIL"; report.failure = e.ToString(); Debug.LogException(e); Finish();
        }
        void Finish()
        {
            if (finished) return; finished = true; goal = null; push = Vector3.zero; measuring = false;
            report.endedUtc = DateTime.UtcNow.ToString("o"); if (player) report.finalFeet = player.transform.position;
            frames?.Flush(); frames?.Dispose(); frames = null; Save();
            if (ready)
            {
                if (lab.Developer.HealthLocked != oldLock) lab.Developer.HandleKey(KeyCode.F2);
                Automated.GetSetMethod(true).Invoke(lab.UI, new object[] { oldAuto }); Application.runInBackground = oldBackground;
            }
            Debug.Log("CASTLE_COMBAT_COLLISION_CHECK_" + report.result + " build=" + report.buildId + " lamps=" + report.lamps.Count + " enemy=" + report.enemy.result);
            if (!Application.isEditor) Application.Quit(report.result == "PASS" ? 0 : 1);
        }
        void Save()
        {
            if (!evidenceReady) return;
            report.averageFps = report.measuredSeconds > 0 ? report.measuredFrames / report.measuredSeconds : 0;
            File.WriteAllText(Path.Combine(folder, "castle-combat-check.json"), JsonUtility.ToJson(report, true));
        }
        void OnApplicationQuit()
        {
            if (finished) return;
            report.result = "CANCELLED"; report.failure = "Player exited before completion; partial evidence only.";
            report.endedUtc = DateTime.UtcNow.ToString("o"); if (player) report.finalFeet = player.transform.position;
            frames?.Flush(); frames?.Dispose(); frames = null; finished = true; Save();
        }
        static bool MovingAction(HuntBoss.Move action) => action == HuntBoss.Move.Stalk || action == HuntBoss.Move.Pursue || action == HuntBoss.Move.Pounce || action == HuntBoss.Move.Lunge;
        static bool ColumnBlocks(Collider[] column, Vector3 fromFeet, Vector3 toFeet)
        {
            var delta = toFeet - fromFeet;
            return column.Any(c => c.Raycast(new Ray(fromFeet + Vector3.up * .9f, delta.normalized), out _, delta.magnitude));
        }
        static Vector3 Horizontal(Vector3 p) => new Vector3(p.x, 0, p.z);
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static string Argument(string name) { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    }

    public sealed class CastleCombatContacts : MonoBehaviour
    {
        readonly Dictionary<string, double> times = new Dictionary<string, double>();
        readonly Dictionary<string, CastleCombatContactEvidence> details = new Dictionary<string, CastleCombatContactEvidence>();
        public void Clear() { times.Clear(); details.Clear(); }
        public string[] RecentNames(double seconds) => times.Where(x => Time.realtimeSinceStartupAsDouble - x.Value <= seconds).Select(x => x.Key).ToArray();
        public CastleCombatContactEvidence[] RecentEvidence(double seconds) => RecentNames(seconds).Select(n => details[n]).ToArray();
        public string LatestName(double seconds) => string.Join(";", RecentNames(seconds));
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider.name.StartsWith("CW_Obstacle_", StringComparison.Ordinal))
            {
                times[hit.collider.name] = Time.realtimeSinceStartupAsDouble;
                details[hit.collider.name] = new CastleCombatContactEvidence { collider = hit.collider.name, point = hit.point, normal = hit.normal, realtime = Time.realtimeSinceStartupAsDouble };
            }
        }
    }
    [Serializable] public sealed class CastleCombatContactEvidence
    {
        public string collider;
        public Vector3 point, normal;
        public double realtime;
    }
}
