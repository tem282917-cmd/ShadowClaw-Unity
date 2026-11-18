using System;
using UnityEngine;

// The opt-in standalone check exercises the same camera, colliders and shot path as mouse input.
public static class WallSceneCheck
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-wall")<0) return;
        try {
            var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
            var eye=lab.GetComponentInChildren<Camera>();
            var hall=lab.GetComponent<IronHallEncounter>();
            Require(hall!=null && lab.gameObject.scene.name=="IronCrownHall" && Camera.allCamerasCount==1
                && !hall.PreviewCameras.activeSelf && lab.transform.Find("Receiver")==null,
                "first-person hunt starts in the authored hall");
            Require(GameObject.Find("Pointed stone vault").GetComponent<MeshFilter>().sharedMesh!=null,
                "vault mesh loaded in player");
            int GalleryCount() => Array.FindAll(hall.GalleryLights,light=>light.enabled).Length;
            void Ready() {
                for(int i=0;i<120 && !lab.Finished;i++) {
                    if(lab.Weapon.Phase==HunterCrossbow.Motion.Ready) {
                        if(lab.Weapon.Rounds>0) return;
                        lab.Weapon.Request(lab.Weapon.Loaded);
                    }
                    lab.SendMessage("Tick",.02f);
                }
                throw new InvalidOperationException("crossbow could not prepare a shot");
            }
            void ShootAt(Transform target) {
                Ready(); eye.transform.LookAt(target.position); lab.Shoot();
            }
            void ShootShadow() {
                Ready();
                var tile=lab.DemonShadowCenter;
                eye.transform.LookAt(new Vector3(tile.x,.015f,tile.y)); lab.Shoot();
            }
            void TickUntil(System.Func<bool> condition,int frames=1200) {
                for(int i=0;i<frames && !condition() && !lab.Finished;i++) lab.SendMessage("Tick",.02f);
                Require(condition(),"encounter reached requested light state");
            }
            Require(lab.LitLamps==2 && GalleryCount()==2 && lab.ShadowCount==1,"two side lights produce one damageable silhouette");
            var mesh=lab.transform.Find("Floor demon").GetComponent<MeshFilter>().sharedMesh;
            foreach(string name in new[] {"左中灯","右中灯","左后灯","右后灯"}) {
                ShootAt(lab.transform.Find(name));
            }
            Require(lab.LitLamps==6 && GalleryCount()==6 && lab.ShadowCount==1,"all six independent fixtures illuminate a single silhouette");
            float smallWidth=mesh.bounds.size.x;
            ShootShadow();
            Require(lab.Damage==10,"small shadow accepts centered shot");
            TickUntil(()=>lab.RitualTarget!=null);
            var first=lab.RitualTarget;
            ShootAt(first);
            Require(lab.RitualTarget==null && lab.LitLamps==6,"shooting a threatened lamp preserves bright state");
            lab.SendMessage("ResetLab"); // Observe the uninterrupted blackout sequence from full health.
            TickUntil(()=>lab.LitLamps==1);
            Require(lab.ShadowCount==1 && mesh.bounds.size.x>smallWidth,"one lamp leaves a larger single shadow");
            TickUntil(()=>lab.LitLamps==0);
            Require(lab.ShadowCount==0 && GalleryCount()==0 && !lab.transform.Find("Floor demon").GetComponent<MeshRenderer>().enabled,
                "darkness hides and removes enemy silhouette");
            float darkDamage=lab.Damage;
            ShootShadow();
            lab.SendMessage("Tick",.25f);
            Require(lab.Damage==darkDamage,"shadow shots and fire cannot hurt the enraged demon");
            Require(lab.BossAction==HuntBoss.Move.Pursue,"blackout starts the exclusive hunt");
            TickUntil(()=>lab.BossAction==HuntBoss.Move.Pounce);
            ShootAt(lab.transform.Find("右前灯"));
            Require(lab.LitLamps==1 && GalleryCount()==1 && lab.ShadowCount==1,"lighting either side immediately breaks darkness");
            var caster=lab.transform.Find("Caster");
            Vector3 paused=caster.position;
            int health=lab.HunterHealth;
            for(int i=0;i<20;i++) lab.SendMessage("Tick",.02f);
            Require(lab.BossAction==HuntBoss.Move.LightStagger && caster.position==paused && health==lab.HunterHealth,
                "relighting during committed pounce stops movement and damage through reload");
            ShootShadow();
            Require(lab.Damage>=darkDamage+10,"relit enemy becomes vulnerable: "+lab.LastShot+" eye="+eye.transform.position+" center="+lab.DemonShadowCenter);
            for(float waited=0;waited<HuntBoss.CounterWindow+.1f;waited+=.02f) lab.SendMessage("Tick",.02f);
            Require(lab.BossAction!=HuntBoss.Move.LightStagger,"counter window ends and hunt resumes in light");
            // Expire both starting wicks through their normal clock, with the hunter at full health.
            lab.SendMessage("ResetLab");
            lab.SendMessage("AgeLamps",24.1f);
            var demonBody=lab.transform.Find("Demon capsule");
            var hunterBody=lab.transform.Find("Hunter capsule");
            var side=Vector3.Cross(Vector3.up,hunterBody.position-demonBody.position).normalized;
            for(int tick=0;tick<150 && lab.BossAction!=HuntBoss.Move.Pounce;tick++) {
                lab.Walk(side,1f/120); lab.SendMessage("Tick",1f/120);
            }
            Require(lab.BossAction==HuntBoss.Move.Pounce,"uninterrupted darkness commits the first pounce");
            for(int tick=0;tick<100 && lab.BossAction==HuntBoss.Move.Pounce;tick++) {
                lab.Walk(side,1f/120); lab.SendMessage("Tick",1f/120);
            }
            Require(lab.BossAction==HuntBoss.Move.Pursue && lab.LitLamps==0 && lab.HunterHealth==100,
                "sidestepping the locked dark pounce avoids damage");
            for(int attack=0;attack<3;attack++) {
                float dt=attack==0 ? 1f/60 : 1f/240;
                int before=lab.HunterHealth;
                for(int tick=0;tick<500 && lab.BossAction!=HuntBoss.Move.Pounce;tick++) lab.SendMessage("Tick",dt);
                Require(lab.BossAction==HuntBoss.Move.Pounce,"dark hunt re-arms pounce "+(attack+1));
                for(int tick=0;tick<220 && !lab.Finished && lab.BossAction==HuntBoss.Move.Pounce;tick++)
                    lab.SendMessage("Tick",dt);
                Require(lab.LitLamps==0 && lab.HunterHealth==Mathf.Max(0,before-43),
                    "dark pounce "+(attack+1)+" inflicts one contact hit, HP "+before+" -> "+lab.HunterHealth);
            }
            lab.SendMessage("ResetLab");
            TickUntil(()=>lab.HunterHealth<100);
            Require(lab.LitLamps==2 && lab.HunterHealth==79,"first claw hits the stationary hunter's body");
            TickUntil(()=>lab.Finished,4000);
            Require(lab.HunterHealth==0,"idle hunter loses encounter");
            lab.SendMessage("ResetLab");
            float eyeHeight=eye.transform.position.y;
            lab.Jump(); lab.SendMessage("Tick",.20f);
            Require(eye.transform.position.y>eyeHeight+.5f,"jump lifts first-person camera");
            lab.Jump(); lab.SendMessage("Tick",.55f);
            Require(Mathf.Abs(eye.transform.position.y-eyeHeight)<.001f,"jump lands and cannot restart in air");
            lab.SendMessage("ResetLab");
            // Play the kill with real movement, magazines, elapsed reloads and changing sight lines.
            for(int i=0;i<4000 && !lab.Finished;i++) {
                float t=i*.02f;
                lab.Walk(new Vector3(Mathf.Cos(t*1.2f),0,-Mathf.Sin(t*1.2f)),.02f);
                lab.SendMessage("Tick",.02f);
                if(lab.Finished || lab.Weapon.Phase!=HunterCrossbow.Motion.Ready) continue;
                if(lab.Weapon.Rounds==0) { lab.Weapon.Request(lab.Weapon.Loaded); continue; }
                if(lab.LitLamps==0) eye.transform.LookAt(lab.transform.Find("右前灯").position);
                else if(lab.RitualTarget!=null && lab.LitLamps==1) eye.transform.LookAt(lab.RitualTarget.position);
                else {
                    Vector2 tile=lab.DemonShadowCenter;
                    eye.transform.LookAt(new Vector3(tile.x,.015f,tile.y));
                }
                lab.Shoot();
            }
            Require(lab.Finished && lab.Damage==120,"reset and victory through one shadow, damage="+lab.Damage+" HP="+lab.HunterHealth);
            Debug.Log("WALL_SCENE_CHECK_PASS");
            Application.Quit(0);
        } catch(Exception e) {
            Debug.LogException(e);
            Application.Quit(1);
        }
    }

    static void Require(bool value,string name)
    {
        if(!value) throw new InvalidOperationException(name);
        Debug.Log("PASS "+name);
    }
}
