using System;
using System.Collections;
using UnityEngine;

public static class DeveloperControlsCheck {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Start() {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-developer")<0)return;
        var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
        lab.StartCoroutine(CheckPlayer(lab));
    }
    static IEnumerator CheckPlayer(ShadowLab lab) {
        var steps=Exercise(lab);
        while(true) {
            bool more;
            try { more=steps.MoveNext(); }
            catch(Exception e) { Debug.LogException(e); Application.Quit(1);yield break; }
            if(!more) break;
            yield return steps.Current;
        }
        Debug.Log("DEVELOPER_PLAYER_PASS");Application.Quit(0);
    }
    static IEnumerator Exercise(ShadowLab lab) {
        var dev=lab.Developer;
        var eye=lab.GetComponentInChildren<Camera>();
        void Press(KeyCode key) => Require(dev.HandleKey(key),"registered "+key);
        void Tick() => lab.SendMessage("Tick",.02f);
        // The player normally presses F1 after entering a running encounter.
        // Let Unity finish its first light-probe and particle updates before sampling it.
        for(int i=0;i<4;i++) yield return null;
        Tick();
        Press(KeyCode.F1);
        var position=eye.transform.position;
        int rounds=lab.Weapon.Rounds;
        var grab=RenderTexture.GetTemporary(480,270,24);
        var tex=new Texture2D(grab.width,grab.height,TextureFormat.RGB24,false);
        Color32[] Pixels() {
            var old=RenderTexture.active;var target=eye.targetTexture;
            eye.targetTexture=grab;eye.Render();RenderTexture.active=grab;
            tex.ReadPixels(new Rect(0,0,grab.width,grab.height),0,0);tex.Apply();
            eye.targetTexture=target;RenderTexture.active=old;return tex.GetPixels32();
        }
        try {
            yield return null;
            var first=Pixels();
            double frozenTime=Time.timeAsDouble;
            var wick=lab.transform.Find("左前灯/Flame").GetComponentInChildren<ParticleSystem>();
            var particles=new ParticleSystem.Particle[wick.main.maxParticles];
            Require(wick.GetParticles(particles)>0,"lit wick is visible during freeze");
            float wickAge=particles[0].remainingLifetime;
            lab.Walk(Vector3.right,.1f);lab.Jump();lab.Shoot();Tick();
            Press(KeyCode.F3);
            Require(dev.HudHidden,"screenshot HUD toggle works during freeze");
            yield return new WaitForSecondsRealtime(.25f);
            var later=Pixels();
            int changed=0;
            for(int i=0;i<later.Length;i++) {
                // A D3D11 RGB24 readback can round a static highlight by one 8-bit level.
                var a=first[i];var b=later[i];
                if(Mathf.Abs(a.r-b.r)>1 || Mathf.Abs(a.g-b.g)>1 || Mathf.Abs(a.b-b.b)>1)changed++;
            }
            Require(dev.Frozen && Time.timeScale==0 && eye.transform.position==position && lab.Weapon.Rounds==rounds,
                "frozen play rejects movement, jumping and shooting");
            Require(wick.GetParticles(particles)>0 && particles[0].remainingLifetime==wickAge &&
                    Time.timeAsDouble==frozenTime,"scaled clock and active wick particle are frozen");
            Require(changed==0,"frozen scene image stays within one RGB level across rendered frames");
        } finally {
            RenderTexture.ReleaseTemporary(grab);
            UnityEngine.Object.Destroy(tex);
        }
        Press(KeyCode.F1);Press(KeyCode.F3);
        Require(!dev.HudHidden && !dev.Frozen && Time.timeScale==1,"time and HUD restore");
        lab.SendMessage("AgeLamps",25f);
        int health=lab.HunterHealth;
        for(int i=0;i<500 && lab.HunterHealth==health;i++) Tick();
        Require(lab.HunterHealth>0 && lab.HunterHealth<health,"unlocked hunter takes damage");
        health=lab.HunterHealth;
        Press(KeyCode.F2);
        int pounces=0;
        for(int i=0;i<600;i++) {
            var before=lab.BossAction;Tick();
            if(before==HuntBoss.Move.Pounce && lab.BossAction!=before)pounces++;
        }
        Require(pounces>=2 && lab.HunterHealth==health && !lab.Finished,"F2 keeps current hunter HP through repeated attacks");
        Press(KeyCode.F2);
        for(int i=0;i<500 && lab.HunterHealth==health;i++)Tick();
        Require(lab.HunterHealth<health,"damage resumes after F2");
        Press(KeyCode.F2);Press(KeyCode.F1);
        lab.SendMessage("ResetLab");
        Require(!dev.Frozen && dev.HealthLocked && lab.HunterHealth==100,"restart resumes time and retains the health-lock preference");
    }
    static void Require(bool ok,string message) {
        if(!ok)throw new InvalidOperationException(message);
        Debug.Log("PASS "+message);
    }
}
