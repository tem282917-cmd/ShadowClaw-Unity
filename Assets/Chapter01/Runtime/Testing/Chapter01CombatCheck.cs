using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Chapter01.Rendering;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Opt-in player acceptance check. Uses the same shot, movement, light clock,
    // pause commands and restart entry point as the playable encounter.
    public static class Chapter01CombatCheck
    {
        [Serializable] sealed class Report
        {
            public string scene, timestampUtc, result, failure;
            public Vector3 playerStart, demonStart, combatTestStart;
            public float floorHeight, shadowDamage;
            public int checks;
            public List<string> passed = new List<string>();
            public List<string> shadowShotDiagnostics = new List<string>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-check-chapter01") < 0) return;
            var lab = UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
            if (!lab)
            {
                Debug.LogError("CHAPTER01_COMBAT_CHECK_FAIL: Main scene has no ShadowLab encounter.");
                Application.Quit(1);
                return;
            }
            lab.StartCoroutine(Check(lab));
        }

        static IEnumerator Check(ShadowLab lab)
        {
            var report = new Report { scene = lab.gameObject.scene.name, timestampUtc = DateTime.UtcNow.ToString("o") };
            string folder = Argument("-chapter01-evidence");
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            // Allow renderer/particle startup without advancing the encounter.
            lab.Developer.SetFrozen(true);
            for (int frame = 0; frame < 3; ++frame) yield return null;
            try
            {
                lab.RestartEncounter();
                Exercise(lab, report, folder);
                report.result = "PASS";
                Debug.Log("CHAPTER01_COMBAT_CHECK_PASS checks=" + report.checks);
            }
            catch (Exception exception)
            {
                report.result = "FAIL";
                report.failure = exception.ToString();
                Debug.LogError("CHAPTER01_COMBAT_CHECK_FAIL: " + exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                lab.Developer.ClearPauseReasons();
                if (!string.IsNullOrEmpty(folder))
                    File.WriteAllText(Path.Combine(folder, "combat-check.json"), JsonUtility.ToJson(report, true));
            }
            Application.Quit(report.result == "PASS" ? 0 : 1);
        }

        static void Exercise(ShadowLab lab, Report report, string folder)
        {
            void Require(bool condition, string description)
            {
                if (!condition) throw new InvalidOperationException(description);
                report.passed.Add(description); ++report.checks;
                Debug.Log("PASS " + description);
            }
            var hall = lab.GetComponent<IronHallEncounter>();
            var eye = lab.Eye;
            Require(hall && report.scene == "Chapter01_Main", "Chapter01_Main is the loaded combat scene");
            Require(eye && eye.enabled && Camera.allCamerasCount == 1 &&
                lab.transform.Find("Receiver") == null, "one gameplay camera; no generated legacy room");
            Require(eye.GetComponent<Chapter01NightPostProcess>() && eye.GetComponent<AudioListener>() &&
                eye.GetComponent<AudioListener>().enabled, "gameplay camera retains authored night grade and audio listener");
            Require(lab.UI && lab.UI.CurrentView == "battle" && lab.Weapon != null &&
                eye.GetComponentInChildren<HunterWeaponView>() && lab.GetComponentInChildren<ShadowDemonView>(),
                "combat UI, crossbow and animated demon are installed");
            Require(!UnityEngine.Object.FindObjectsByType<Chapter01PlayerRoute>(FindObjectsSortMode.None)
                .Any(route => route.enabled), "route demonstration controller does not drive gameplay");
            var hunter = lab.transform.Find("Hunter capsule").GetComponent<CharacterController>();
            var demon = lab.transform.Find("Demon capsule").GetComponent<CharacterController>();
            Require(hunter && demon && ActiveBodies() == 2, "exactly two active combat character controllers");
            report.playerStart = hunter.transform.position;
            report.demonStart = demon.transform.position;
            report.floorHeight = hall.SurfaceHeight(report.playerStart.x, report.playerStart.z);
            Require(Mathf.Abs(report.playerStart.y - report.floorHeight) < .15f &&
                Mathf.Abs(report.demonStart.y - hall.SurfaceHeight(report.demonStart.x, report.demonStart.z)) < .15f &&
                Mathf.Abs(eye.transform.position.y - report.playerStart.y - 1.65f) < .05f,
                "spawned hunter, demon and camera use the authored floor elevation");
            Require(hall.StartWithExploration && !lab.EncounterStarted &&
                Vector2.Distance(new Vector2(report.playerStart.x, report.playerStart.z),
                    new Vector2(hall.HunterStart.x, hall.HunterStart.z)) < .05f && report.floorHeight < 10 &&
                !hall.PlayArea.Contains(new Vector2(report.playerStart.x, report.playerStart.z)),
                "new hunt starts at the mountain foot with the encounter dormant");
            Require(hall.GalleryLights.Length == 6 && hall.GalleryLights.All(light => light) &&
                hall.GalleryLights.Distinct().Count() == 6 && lab.LitLamps == 6 && LitLights() == 6 && lab.ShadowCount == 0,
                "six authored lamps stay lit during exploration without a combat shadow");
            Capture(eye, folder, "00-mountain-start");

            void Tick(float dt = .02f) => lab.SendMessage("Tick", dt);
            int LitLights() => hall.GalleryLights.Count(light => light.enabled);
            void Ready()
            {
                for (int i = 0; i < 150 && !lab.Finished; ++i)
                {
                    if (lab.Weapon.Phase == HunterCrossbow.Motion.Ready)
                    {
                        if (lab.Weapon.Rounds > 0) return;
                        lab.Weapon.Request(lab.Weapon.Loaded);
                    }
                    Tick();
                }
                throw new InvalidOperationException("Crossbow did not return to a loaded ready state.");
            }
            void LampShot(int slot)
            {
                Ready(); eye.transform.LookAt(hall.WickPosition(slot)); lab.Shoot();
                Require(hall.GalleryLights[slot].enabled, "real arrow relights authored lamp " + slot + ": " + lab.LastShot);
            }
            void ShadowShot()
            {
                Ready();
                Vector2 target = lab.DemonShadowCenter;
                Vector3 aimedPoint = new Vector3(target.x, hall.SurfaceHeight(target.x, target.y) + .015f, target.y);
                if (lab.LitLamps > 0)
                {
                    // The elevated scene's rendered decal sits above the physical floor.
                    // Resolve its exact mesh height at the advertised X/Z instead of aiming
                    // underneath an edge pixel and crossing outside its coverage on the way down.
                    var body = lab.ShadowState.Witch.Body;
                    var vertical = new Ray(aimedPoint + Vector3.up * 20, Vector3.down);
                    float distance = body != null ? body.HitDistance(vertical) : float.PositiveInfinity;
                    Require(body != null && body.Contains(target) && !float.IsInfinity(distance) && !float.IsNaN(distance),
                        "lit shadow aim has visible coverage and an exact rendered mesh height");
                    aimedPoint = vertical.GetPoint(distance);
                }
                eye.transform.LookAt(aimedPoint);
                DiagnoseShadowShot(lab, hall, report, aimedPoint);
                if (lab.LitLamps > 0)
                {
                    Ray aimRay = eye.ViewportPointToRay(new Vector3(.5f, .5f, 0));
                    float distance = lab.ShadowState.Witch.HitDistance(aimRay);
                    Require(!float.IsInfinity(distance) && !float.IsNaN(distance),
                        "actual camera aim intersects the same visible elevated shadow mesh");
                }
                Capture(eye, folder, "shadow-aim-" + report.shadowShotDiagnostics.Count.ToString("00"));
                lab.Shoot();
                string result = "SHADOW_SHOT_RESULT " + lab.LastShot + " damage=" + lab.Damage;
                report.shadowShotDiagnostics.Add(result); Debug.Log(result);
            }
            void Until(Func<bool> condition, string description, int frames = 500)
            {
                for (int frame = 0; frame < frames && !condition() && !lab.Finished; ++frame) Tick();
                Require(condition(), description);
            }
            void Reset()
            {
                lab.RestartEncounter(); Physics.SyncTransforms();
                EnterCombatForTest();
            }

            void EnterCombatForTest()
            {
                // Explicit test setup only. The separate approach check walks the real route
                // without teleporting; this keeps combat regressions independently repeatable.
                Vector3 position = hall.DemonStart + new Vector3(-.81f, 0, 2.21f);
                position.y = hall.SurfaceHeight(position.x, position.z);
                Require(hall.PlayArea.Contains(new Vector2(position.x, position.z)), "combat test setup is inside the authored hall");
                hunter.enabled = false;
                hunter.transform.position = position + Vector3.up * .08f;
                hunter.enabled = true;
                Physics.SyncTransforms();
                hunter.Move(Vector3.down * .15f);
                lab.ShadowState.Player = hunter.transform.position;
                Tick();
                Require(lab.EncounterStarted && lab.LitLamps == 2 && LitLights() == 2 && lab.ShadowCount == 1,
                    "grounded hall entry activates two starting lamps and shadow combat");
                report.combatTestStart = hunter.transform.position;
                lab.RefreshView();
                eye.transform.LookAt(demon.transform.position + Vector3.up * .9f);
            }

            EnterCombatForTest();

            Capture(eye, folder, "01-battle-start");
            lab.UI.HandleKey(KeyCode.Escape);
            Vector3 paused = hunter.transform.position;
            int rounds = lab.Weapon.Rounds;
            lab.Walk(Vector3.right, .2f); lab.Jump(); lab.Shoot(); Tick(.5f);
            Require(lab.UI.CurrentView == "pause" && lab.Developer.Paused && Time.timeScale == 0 &&
                hunter.transform.position == paused && lab.Weapon.Rounds == rounds,
                "pause UI suspends movement, jump, fire and encounter clock");
            lab.UI.HandleKey(KeyCode.Escape);
            Require(lab.UI.CurrentView == "battle" && !lab.Developer.Paused && Time.timeScale > 0,
                "resume returns to the same combat session");

            foreach (int slot in new[] { 1, 2, 4, 5 }) LampShot(slot);
            Require(lab.LitLamps == 6 && LitLights() == 6 && lab.ShadowCount == 1,
                "all six authored lamps drive the existing shadow combat rules");
            ShadowShot();
            report.shadowDamage = lab.Damage;
            Require(Mathf.Approximately(lab.Damage, 10),
                "holy arrow hits the visible shadow on the elevated scene floor: " + lab.LastShot);
            lab.RefreshView(); Capture(eye, folder, "02-six-lights");

            Reset();
            lab.SendMessage("AgeLamps", 24.1f);
            Require(lab.LitLamps == 0 && LitLights() == 0 && lab.ShadowCount == 0,
                "natural expiry removes all six gameplay lights and enemy shadow");
            float darkDamage = lab.Damage;
            ShadowShot(); Tick(.02f);
            Require(Mathf.Approximately(darkDamage, lab.Damage), "darkness prevents shadow damage");
            Until(() => lab.BossAction == HuntBoss.Move.Pounce, "blackout starts a committed dark pounce");
            lab.RefreshView(); Capture(eye, folder, "03-dark-hunt");
            LampShot(3);
            Require(lab.BossAction == HuntBoss.Move.LightStagger && lab.LitLamps == 1 && lab.ShadowCount == 1,
                "relighting interrupts the committed pounce and restores vulnerability");
            var stopped = demon.transform.position;
            int health = lab.HunterHealth;
            for (int i = 0; i < 20; ++i) Tick();
            Require(lab.BossAction == HuntBoss.Move.LightStagger && demon.transform.position == stopped &&
                lab.HunterHealth == health, "relight counter window stops demon movement and damage");
            ShadowShot();
            Require(lab.Damage >= darkDamage + 10, "relit elevated shadow accepts damage: " + lab.LastShot);
            lab.RefreshView(); Capture(eye, folder, "04-relit-counter");

            Reset();
            Vector3 before = hunter.transform.position;
            Vector3 direction = before.x < lab.ShadowState.WalkBounds.center.x ? Vector3.right : Vector3.left;
            for (int i = 0; i < 12; ++i) { lab.Walk(direction, .02f); Tick(); }
            Vector3 after = hunter.transform.position;
            Require(Vector3.Distance(before, after) > .35f &&
                Mathf.Abs(after.y - hall.SurfaceHeight(after.x, after.z)) < .2f,
                "actual character controller moves across scene floor while retaining ground contact");
            float baseEye = eye.transform.position.y;
            lab.Jump(); Tick(.20f);
            Require(eye.transform.position.y > baseEye + .5f, "jump raises the camera above the elevated floor");
            Tick(.55f);
            Require(Mathf.Abs(eye.transform.position.y - baseEye) < .15f,
                "jump lands back on authored scene collision");

            // Freeze the selection of one original spell so both sides of its
            // enlarged boundary can be exercised at the actual hall elevation.
            var ai=(HuntBoss)typeof(ShadowLab).GetField("boss",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(lab);
            var fx=lab.GetComponentInChildren<BossSpectacle>();
            Vector3 center=lab.ShadowState.Enemy;
            Vector3 radial=Vector3.ProjectOnPlane(hunter.transform.position-center,Vector3.up).normalized;
            foreach(float radius in new[]{4.34f,4.26f}) {
                Vector3 p=center+radial*radius;p.y=hall.SurfaceHeight(p.x,p.z)+.03f;
                hunter.enabled=false;hunter.transform.position=p;hunter.enabled=true;Physics.SyncTransforms();
                lab.ShadowState.Player=p;
                typeof(HuntBoss).GetProperty("Destination").SetValue(ai,center);
                typeof(HuntBoss).GetMethod("Enter",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(ai,new object[]{HuntBoss.Move.Burst,.45f});
                Tick(.02f);
                var ring=fx.DangerOutline;
                Require(ring.enabled && Mathf.Abs(Vector3.ProjectOnPlane(ring.GetPosition(0)-center,Vector3.up).magnitude-4.3f)<.015f,
                    "rendered burst perimeter matches 4.3 metres at a hunter offset of "+radius);
                if(radius>4.3f) {eye.transform.LookAt(center+Vector3.up*.45f);Capture(eye,folder,"05-enlarged-burst");}
                int previous=lab.HunterHealth;Tick(.45f);
                Require((lab.HunterHealth<previous)==(radius<4.3f),"burst release respects the drawn edge at "+radius+" metres");
            }
            lab.UI.HandleKey(KeyCode.F5);
            Require(lab.Weapon.Rounds == 4 && lab.HunterHealth == 100 && lab.Damage == 0 &&
                lab.LitLamps == 6 && !lab.EncounterStarted && lab.ShadowCount == 0 &&
                Camera.allCamerasCount == 1 && ActiveBodies() == 2 &&
                Vector3.Distance(hunter.transform.position, report.playerStart) < .05f &&
                Vector3.Distance(demon.transform.position, report.demonStart) < .05f,
                "F5 returns to the mountain foot with dormant encounter, full ammunition and no duplicate controllers");
        }

        static int ActiveBodies() => UnityEngine.Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None)
            .Count(controller => controller.enabled);

        // Observation only: compare the historic floor offset with the actual rasterized mesh,
        // and report the player's competing silhouette without changing production hit rules.
        static void DiagnoseShadowShot(ShadowLab lab, IronHallEncounter hall, Report report, Vector3 aimedPoint)
        {
            var state = lab.ShadowState;
            var body = state.Witch.Body;
            void Log(string text) { report.shadowShotDiagnostics.Add(text); Debug.Log(text); }
            string Number(float value) => float.IsNaN(value) || float.IsInfinity(value) ? "none" :
                value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            string Point(Vector3 value) => value.ToString("F6");
            string Path(Transform value) {
                string result = value.name;
                while (value.parent != null) { value = value.parent; result = value.name + "/" + result; }
                return result;
            }
            Physics.SyncTransforms();
            Ray actual = lab.Eye.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            Log("SHADOW_SHOT_CONTEXT lit=" + lab.LitLamps + " shadowCount=" + lab.ShadowCount +
                " boss=" + lab.BossAction + " eye=" + Point(lab.Eye.transform.position) +
                " hunter=" + Point(state.PlayerBody) + " demon=" + Point(state.Enemy) +
                " commanded=" + Point(aimedPoint) + " ground=" + Number(hall.SurfaceHeight(aimedPoint.x, aimedPoint.z)) +
                " silhouetteScale=" + Number(state.SilhouetteScale) + " linearScale=" + Number(body != null ? body.LinearScale : 0) +
                " aimCovered=" + (body != null && body.Contains(new Vector2(aimedPoint.x, aimedPoint.z))) +
                " inkBounds=" + hall.InkBounds);
            void RayResult(string label, Ray ray) {
                float enemy = Mathf.Min(state.Witch.HitDistance(ray), state.Arm.HitDistance(ray));
                float hunter = state.Hunter.HitDistance(ray);
                Log("SHADOW_SHOT_RAY " + label + " origin=" + Point(ray.origin) + " direction=" + Point(ray.direction) +
                    " enemy=" + Number(enemy) + " hunter=" + Number(hunter) + " productionAccepts=" + (enemy < hunter) +
                    " enemyPoint=" + (float.IsInfinity(enemy) ? "none" : Point(ray.GetPoint(enemy))) +
                    " hunterPoint=" + (float.IsInfinity(hunter) ? "none" : Point(ray.GetPoint(hunter))));
            }
            RayResult("commanded-aim", actual);
            Vector3 legacyPoint = new Vector3(aimedPoint.x, hall.SurfaceHeight(aimedPoint.x, aimedPoint.z) + .015f, aimedPoint.z);
            RayResult("legacy-ground-plus-015", new Ray(lab.Eye.transform.position, (legacyPoint - lab.Eye.transform.position).normalized));
            var args = new object[] { actual, default(RaycastHit) };
            bool struck = (bool)typeof(ShadowLab).GetMethod("FirstShotSurface", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(lab, args);
            if (struck) {
                var hit = (RaycastHit)args[1];
                Log("SHADOW_SHOT_PHYSICS first=" + Path(hit.transform) + " point=" + Point(hit.point) +
                    " normal=" + Point(hit.normal) + " distance=" + Number(hit.distance) + " isGround=" + hall.IsGround(hit.collider));
            } else Log("SHADOW_SHOT_PHYSICS no accepted physical surface");
            if (body == null) return;
            var vertical = new Ray(new Vector3(aimedPoint.x, aimedPoint.y + 20, aimedPoint.z), Vector3.down);
            float meshDistance = body.HitDistance(vertical);
            RayResult("vertical-through-same-aim", vertical);
            if (float.IsInfinity(meshDistance)) {
                Log("SHADOW_SHOT_MESH no visible mesh at advertised aim");
                return;
            }
            Vector3 visiblePoint = vertical.GetPoint(meshDistance);
            var visibleRay = new Ray(lab.Eye.transform.position, (visiblePoint - lab.Eye.transform.position).normalized);
            RayResult("same-eye-to-actual-visible-mesh", visibleRay);
            float planarDistance = Mathf.Abs(actual.direction.y) > .00001f ?
                (visiblePoint.y - actual.origin.y) / actual.direction.y : float.PositiveInfinity;
            Vector3 planePoint = float.IsInfinity(planarDistance) ? Vector3.zero : actual.GetPoint(planarDistance);
            Log("SHADOW_SHOT_MESH visiblePoint=" + Point(visiblePoint) + " targetHeightError=" + Number(visiblePoint.y - aimedPoint.y) +
                " originalRayAtVisibleHeight=" + Point(planePoint) + " shiftedMaskCovered=" +
                body.Contains(new Vector2(planePoint.x, planePoint.z)));
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        internal static void Capture(Camera camera, string folder, string name)
        {
            if (string.IsNullOrEmpty(folder) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active; var oldTarget = camera.targetTexture; float oldAspect = camera.aspect;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.aspect = target.width / (float)target.height; camera.targetTexture = target;
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target); UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
