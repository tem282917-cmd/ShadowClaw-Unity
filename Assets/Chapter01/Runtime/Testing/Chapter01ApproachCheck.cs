using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Opt-in validation of the actual mountain-to-hall controller route. The
    // supplied approved route is read-only input; no route point is a teleport.
    public static class Chapter01ApproachCheck
    {
        [Serializable] sealed class RouteFile
        {
            public string sourceSha256;
            public Vector3[] points;
        }
        [Serializable] sealed class Report
        {
            public string result, failure, timestampUtc, routePath, sourceSha256;
            public int routePoints, completedSegments, entrySegment = -1, ticks;
            public float simulatedSeconds, walkedMetres, maximumGroundGap, maximumRouteDeviation;
            public Vector3 spawn, position, entryPosition;
            public bool walkedWithoutTeleport, dormantDuringApproach, belowHallDoesNotTrigger, entryLatches;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-check-chapter01-approach") < 0) return;
            var report = new Report { timestampUtc = DateTime.UtcNow.ToString("o"), routePath = Argument("-chapter01-route") };
            string folder = Argument("-chapter01-evidence");
            ShadowLab lab = null;
            try
            {
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                lab = UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
                Require(lab && lab.gameObject.scene.name == "Chapter01_Main", "Chapter01_Main encounter is loaded");
                if (string.IsNullOrEmpty(report.routePath)) throw new ArgumentException("Supply -chapter01-route <approved castle-route.json>.");
                var route = JsonUtility.FromJson<RouteFile>(File.ReadAllText(report.routePath));
                Require(route != null && route.points != null && route.points.Length == 41, "approved 41-point route is available");
                report.routePoints = route.points.Length; report.sourceSha256 = route.sourceSha256;
                var hall = lab.GetComponent<IronHallEncounter>();
                var hunter = lab.transform.Find("Hunter capsule").GetComponent<CharacterController>();
                var demon = lab.transform.Find("Demon capsule");
                lab.RestartEncounter();
                report.spawn = hunter.transform.position;
                Require(hall.StartWithExploration && !lab.EncounterStarted &&
                    Vector2.Distance(new Vector2(report.spawn.x, report.spawn.z),
                        new Vector2(hall.HunterStart.x, hall.HunterStart.z)) < .05f &&
                    Vector2.Distance(new Vector2(report.spawn.x, report.spawn.z),
                        new Vector2(route.points[0].x, route.points[0].z)) < 10 &&
                    Mathf.Abs(report.spawn.y - hall.SurfaceHeight(report.spawn.x, report.spawn.z)) < .15f &&
                    !hall.PlayArea.Contains(new Vector2(report.spawn.x, report.spawn.z)),
                    "hunter begins grounded in the authored clearing near the first route waypoint");
                Vector3 routeFacing = route.points[0] - report.spawn; routeFacing.y = 0;
                if (routeFacing.sqrMagnitude < .01f) { routeFacing = route.points[1] - route.points[0]; routeFacing.y = 0; }
                Vector3 cameraFacing = lab.Eye.transform.forward; cameraFacing.y = 0;
                Require(Vector3.Dot(routeFacing.normalized, cameraFacing.normalized) > .98f,
                    "initial camera faces the first uphill route segment");

                var area = hall.BattleArea.bounds;
                var belowHall = new Vector3(area.center.x, area.min.y - 2, area.center.z);
                report.belowHallDoesNotTrigger = !hall.ShouldStartEncounter(belowHall);
                Require(report.belowHallDoesNotTrigger, "being beneath the hall footprint cannot start the encounter");
                Vector3 dormantDemon = demon.position;
                float dormantAction = lab.BossActionRemaining;
                float noGroundTime = 0;
                var groundHits = new RaycastHit[24];
                void Step(float dt)
                {
                    lab.SendMessage("Tick", dt); ++report.ticks; report.simulatedSeconds += dt;
                    report.position = hunter.transform.position;
                }
                void CheckDormant()
                {
                    if (lab.EncounterStarted) return;
                    RequireQuiet(lab.HunterHealth == 100 && lab.Damage == 0 && lab.LitLamps == 6 &&
                        lab.ShadowCount == 0 && hall.GalleryLights.All(light => light.enabled) &&
                        Vector3.Distance(demon.position, dormantDemon) < .001f &&
                        Mathf.Abs(lab.BossActionRemaining - dormantAction) < .001f,
                        "boss, health, lamps or action timer changed before hall entry");
                }
                // Longer than the original 24-second lamp life, before any route movement.
                for (int tick = 0; tick < 1300; ++tick) { Step(.02f); CheckDormant(); }
                Require(!lab.EncounterStarted && report.simulatedSeconds > 25,
                    "26 seconds at the mountain foot do not start combat or age its lamps");
                report.dormantDuringApproach = true;
                Chapter01CombatCheck.Capture(lab.Eye, folder, "approach-01-mountain-start");

                int goal = 0;
                float lastProgressTime = report.simulatedSeconds, stuckSeconds = 0;
                Vector3 lastProgressPosition = hunter.transform.position;
                while (!lab.EncounterStarted && goal < route.points.Length)
                {
                    if (report.simulatedSeconds > 1200) throw new InvalidOperationException("Approach exceeded 20 simulated minutes.");
                    Vector3 before = hunter.transform.position;
                    Vector3 to = route.points[goal] - before; to.y = 0;
                    if (to.magnitude <= .10f)
                    {
                        RequireQuiet(Mathf.Abs(before.y - route.points[goal].y) < .75f,
                            "waypoint reached at wrong elevation, target=" + route.points[goal]);
                        report.completedSegments = goal;
                        Debug.Log("APPROACH_WAYPOINT " + goal + " position=" + before + " simulated=" + report.simulatedSeconds.ToString("F1"));
                        ++goal;
                        continue;
                    }
                    float dt = Mathf.Min(.02f, to.magnitude / 3.15f);
                    lab.Walk(to.normalized, dt);
                    Step(dt);
                    Vector3 after = hunter.transform.position;
                    report.walkedMetres += Vector3.Distance(before, after);
                    if (lab.EncounterStarted)
                    {
                        report.entrySegment = goal - 1; report.entryPosition = after;
                        break;
                    }
                    CheckDormant();
                    Vector3 segmentStart = goal == 0 ? report.spawn : route.points[goal - 1];
                    Vector3 segment = route.points[goal] - segmentStart; segment.y = 0;
                    Vector3 fromStart = after - segmentStart; fromStart.y = 0;
                    float along = Mathf.Clamp01(Vector3.Dot(fromStart, segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
                    float deviation = (fromStart - along * segment).magnitude;
                    report.maximumRouteDeviation = Mathf.Max(report.maximumRouteDeviation, deviation);
                    RequireQuiet(deviation < 2, "controller left the approved route corridor");

                    float closest = float.PositiveInfinity;
                    int count = Physics.RaycastNonAlloc(after + Vector3.up * .8f, Vector3.down, groundHits, 3,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; ++i)
                        if (hall.IsGround(groundHits[i].collider)) closest = Mathf.Min(closest, groundHits[i].distance);
                    float gap = float.IsPositiveInfinity(closest) ? 999 : closest - .8f;
                    report.maximumGroundGap = Mathf.Max(report.maximumGroundGap, gap);
                    noGroundTime = hunter.isGrounded || (gap >= -.12f && gap < .30f) ? 0 : noGroundTime + dt;
                    RequireQuiet(noGroundTime < 1, "controller lost authored route ground contact");
                    if (report.simulatedSeconds - lastProgressTime >= 2)
                    {
                        stuckSeconds = Vector3.Distance(after, lastProgressPosition) < .2f ? stuckSeconds + 2 : 0;
                        lastProgressTime = report.simulatedSeconds; lastProgressPosition = after;
                        RequireQuiet(stuckSeconds < 8, "controller blocked for eight simulated seconds");
                    }
                }
                Require(lab.EncounterStarted && report.entrySegment >= 36 &&
                    hall.ShouldStartEncounter(report.entryPosition),
                    "walking the mountain stairs and terrain reaches the hall and starts combat");
                Require(report.walkedMetres > 500 && report.completedSegments >= 36 &&
                    lab.LitLamps == 2 && lab.ShadowCount == 1 && lab.HunterHealth == 100 && lab.Damage == 0,
                    "combat starts once after the full approach with fresh two-light state");
                report.walkedWithoutTeleport = true;
                // Remain in place briefly: entering again must not keep resetting the timers.
                float actionAtEntry = lab.BossActionRemaining;
                for (int tick = 0; tick < 15; ++tick) Step(.02f);
                report.entryLatches = lab.EncounterStarted && lab.BossActionRemaining < actionAtEntry - .1f;
                Require(report.entryLatches, "hall entry latches and combat time advances without repeated resets");
                lab.RefreshView();
                Chapter01CombatCheck.Capture(lab.Eye, folder, "approach-02-hall-entry");
                report.result = "PASS";
                Debug.Log("CHAPTER01_APPROACH_CHECK_PASS simulatedSeconds=" + report.simulatedSeconds.ToString("F1") +
                    " walkedMetres=" + report.walkedMetres.ToString("F1") + " entrySegment=" + report.entrySegment);
            }
            catch (Exception exception)
            {
                report.result = "FAIL";
                report.failure = exception.ToString();
                Debug.LogError("CHAPTER01_APPROACH_CHECK_FAIL completedSegments=" + report.completedSegments +
                    " position=" + report.position + " simulatedSeconds=" + report.simulatedSeconds.ToString("F2") + ": " + exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                if (lab) lab.Developer.ClearPauseReasons();
                if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder, "approach-check.json"), JsonUtility.ToJson(report, true));
            }
            Application.Quit(report.result == "PASS" ? 0 : 1);
        }

        static void Require(bool condition, string description)
        {
            RequireQuiet(condition, description); Debug.Log("PASS " + description);
        }
        static void RequireQuiet(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
        }
        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
