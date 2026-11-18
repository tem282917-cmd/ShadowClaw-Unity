using System;
using UnityEngine;

public sealed class ShadowDemonView : MonoBehaviour
{
    public AnimationClip[] Clips;
    AnimationClip active;
    float age;
    Renderer[] surfaces;
    Color[] originalColors;
    Material[] silk;
    MaterialPropertyBlock tint;
    float surfaceReadability = 1f, rimReadability = 1f;
    Vector3 lastPosition;
    Transform head;
    Light moonSource;
    bool dying;
    Vector3 deathOrigin;
    Quaternion deathFacing;
    Transform[] deathBones;
    Quaternion[] deathStartPose, deathStaggerPose;
    [Tooltip("Peak linear scale of the imported smoke. The chapel guardian uses 3.")]
    [Min(1)] public float DeathMistScale = 12;
    ImportedEnemyEffect deathMist;
    ParticleSystemRenderer[] deathMistRenderers;
    MaterialPropertyBlock deathMistTint;
    Material deathMistMaterial;
    float deathMistAge;
    public bool DeathPlaying => dying;
    public float DeathPoseProgress { get; private set; }
    public Vector3 DeathFocusPoint => head!=null ? Vector3.Lerp(transform.position,head.position,.78f) : transform.position+Vector3.up;
    static readonly int WorldToHead = Shader.PropertyToID("_WorldToBellHead");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CheckPlayer()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-bell-demon")<0)return;
        try
        {
            var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
            var view=lab.GetComponentInChildren<ShadowDemonView>();
            if(!view || view.Clips.Length!=8 || lab.gameObject.scene.name!="IronCrownHall")
                throw new InvalidOperationException("Character or animations missing in the combat scene");
            var mesh=view.GetComponentInChildren<SkinnedMeshRenderer>();
            if(!mesh || !mesh.enabled || mesh.bones.Length!=19 || !mesh.sharedMaterial.GetTexture("_MetallicGlossMap"))
                throw new InvalidOperationException("Missing character geometry, skin or packed surface map");
            var args=Environment.GetCommandLineArgs();
            int evidence=Array.IndexOf(args,"-bell-evidence");
            if(evidence>=0) CheckCrystalEyes(view,lab.GetComponentInChildren<Camera>(),args[evidence+1]);
            var caster=lab.transform.Find("Caster");
            if(Array.Exists(caster.GetComponentsInChildren<Renderer>(),r=>r.enabled))
                throw new InvalidOperationException("Placeholder renderer still visible");
            var arm=Array.Find(mesh.bones,b=>b.name=="UpperArm.R");
            bool cast=false;
            for(int i=0;i<1200 && !lab.Finished && !cast;i++)
            {
                lab.SendMessage("Tick",.02f);
                cast=lab.BossAction==HuntBoss.Move.Claw && view.active && view.active.name=="Cast"
                     && Quaternion.Angle(arm.localRotation,Quaternion.identity)>15;
            }
            if(!cast)throw new InvalidOperationException("Did not observe the sleeve lift during a combat claw cast");
            var fx=lab.GetComponentInChildren<BossSpectacle>();
            foreach(var system in fx.GetComponentsInChildren<ParticleSystemRenderer>())
                if(!system.sharedMaterial.mainTexture || system.sharedMaterial.mainTexture.width!=512)
                    throw new InvalidOperationException("Missing particle artwork for "+system.name);
            if(mesh.sharedMaterial.shader.name!="ShadowClaw/BellSleeveSilk" || fx.BodyParticleCount==0)
                throw new InvalidOperationException("Boss rim material or body particles missing");
            if(!fx.DangerOutline.enabled || fx.AttackParticleCount==0)
                throw new InvalidOperationException("Claw lock is missing its traced edge or gathering sparks");
            for(int i=0;i<90 && fx.ImpactCount==0 && !lab.Finished;i++) lab.SendMessage("Tick",.02f);
            if(fx.ImpactCount==0 || fx.AttackParticleCount==0)
                throw new InvalidOperationException("First claw did not discharge particles on close");
            if(fx.transform.Find("Closing talons").GetComponent<ParticleSystem>().particleCount==0 ||
               fx.transform.Find("Spell drift").GetComponent<ParticleSystem>().particleCount==0)
                throw new InvalidOperationException("The claw impact did not release its slash and smoke");
            Debug.Log("PASS live Boss silk, attached wisps, locked footprint and impact discharge");
            Debug.Log("BELL_DEMON_PLAYER_PASS");
            Application.Quit(0);
        }
        catch(Exception e){Debug.LogException(e);Application.Quit(1);}
    }

    static void CheckCrystalEyes(ShadowDemonView view,Camera camera,string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        var position=camera.transform.position;
        var rotation=camera.transform.rotation;
        var oldTarget=camera.targetTexture;
        var oldActive=RenderTexture.active;
        var crossbow=camera.GetComponentInChildren<HunterWeaponView>().gameObject;
        bool weaponActive=crossbow.activeSelf;
        var lights=Array.FindAll(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None),light=>light.enabled);
        var target=RenderTexture.GetTemporary(1280,720,24);
        var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        var nearEye=view.head.TransformPoint(new Vector3(.033f,.175f,.102f));
        var farEye=view.head.TransformPoint(new Vector3(-.112f,.175f,.081f));
        var middle=(nearEye+farEye)*.5f;
        Vector3 towardsHunter=view.head.TransformDirection(new Vector3(-.23f,0,1).normalized);
        try
        {
            camera.targetTexture=target;
            void Shot(float distance,Vector3 aim,string name) {
                camera.transform.position=middle+towardsHunter*distance;
                camera.transform.LookAt(aim);
                camera.Render();
                RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
                image.Apply();
                if(name!=null) System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name+".png"),image.EncodeToPNG());
            }
            crossbow.SetActive(false);
            Shot(.93f,middle,"crystal-face");
            Shot(.43f,middle,"crystal-facets");
            var closeup=image.GetPixels32();
            foreach(var socket in new[]{nearEye,farEye}) {
                Vector3 projected=camera.WorldToScreenPoint(socket);
                int groove=0;
                for(int y=(int)projected.y-5;y<=(int)projected.y+5;y++)
                    for(int x=(int)projected.x-12;x<=(int)projected.x+12;x++) {
                        int i=y*target.width+x;
                        if(closeup[i].r<85 && closeup[i-8].r>128 && closeup[i+8].r>128) groove++;
                    }
                if(groove>3) throw new InvalidOperationException("A dark stripe remains across the crystal eye");
            }
            Debug.Log("PASS both ruby inlays have a continuous luminous centre");
            crossbow.SetActive(weaponActive);
            foreach(var light in lights) light.enabled=false;
            Vector3 readyAim=middle-Vector3.up*.56f+view.head.right*.27f;
            foreach(var mat in view.silk) mat.SetFloat("_CrystalEyes",0);
            Shot(4.5f,readyAim,"crystal-eyes-dark-control");
            var original=image.GetPixels32();
            foreach(var mat in view.silk) mat.SetFloat("_CrystalEyes",1);
            Shot(4.5f,readyAim,"crystal-eyes-dark");
            var crystal=image.GetPixels32();
            foreach(var socket in new[]{nearEye,farEye}) {
                Vector3 pixel=camera.WorldToScreenPoint(socket);
                int visible=0;
                for(int y=(int)pixel.y-5;y<=(int)pixel.y+5;y++)
                    for(int x=(int)pixel.x-8;x<=(int)pixel.x+8;x++) {
                        int i=y*target.width+x;
                        if(crystal[i].r-original[i].r>=32 && crystal[i].r>=105 && crystal[i].r-crystal[i].g>=32) visible++;
                    }
                if(visible<5) throw new InvalidOperationException("Unlit crystal socket at "+pixel+" changes only "+visible+" bright pixels");
                Debug.Log("PASS crystal socket emits across "+visible+" pixels at 4.5 metres with all scene lights off");
            }
            // Nearby skin, hair and armor should receive identical lighting in both renders.
            // This rules out a whole-face brightness change being counted as glowing eyes.
            Vector3 a=camera.WorldToScreenPoint(nearEye), b=camera.WorldToScreenPoint(farEye);
            int spills=0;
            for(int y=(int)Mathf.Min(a.y,b.y)-13;y<=(int)Mathf.Max(a.y,b.y)+13;y++)
                for(int x=(int)Mathf.Min(a.x,b.x)-16;x<=(int)Mathf.Max(a.x,b.x)+16;x++) {
                    if((Mathf.Abs(x-a.x)<=9 && Mathf.Abs(y-a.y)<=6) || (Mathf.Abs(x-b.x)<=9 && Mathf.Abs(y-b.y)<=6)) continue;
                    int i=y*target.width+x;
                    if(Mathf.Abs(crystal[i].r-original[i].r)>=32) spills++;
                }
            if(spills>3) throw new InvalidOperationException("Crystal inlays brighten "+spills+" control pixels outside the sockets");
            Debug.Log("PASS dark eye comparison matches cheek, brow and hair controls ("+spills+" changed pixels)");
        }
        finally {
            foreach(var mat in view.silk) mat.SetFloat("_CrystalEyes",1);
            foreach(var light in lights) light.enabled=true;
            crossbow.SetActive(weaponActive);
            camera.transform.SetPositionAndRotation(position,rotation);
            camera.targetTexture=oldTarget;
            RenderTexture.active=oldActive;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.Destroy(image);
        }
    }

    public static ShadowDemonView Attach(Transform caster)
    {
        var prefab = Resources.Load<GameObject>("ShadowDemon/Character");
        if (!prefab || !prefab.GetComponent<ShadowDemonView>()) return null;
        var view = Instantiate(prefab, caster.parent, false).GetComponent<ShadowDemonView>();
        // Put the crown and shoulders at the height of the original first-person body blocker.
        view.transform.localScale*=1.18f;
        var hall=caster.parent.GetComponent<IronHallEncounter>();
        if(hall!=null && hall.Moonlight!=null) {
            view.moonSource=hall.Moonlight;
            foreach(var material in view.silk) material.SetVector("_MoonOrigin",view.MoonOrigin(caster.position));
        }
        foreach (var placeholder in caster.GetComponentsInChildren<Renderer>())
            placeholder.enabled = false; // The original colliders still intercept bolts hitting the body.
        return view;
    }

    public void ResetPose(Vector3 position)
    {
        ResetDeath();
        age = 0;
        active = null;
        lastPosition = position;
    }

    // The pose owns this renderer's property block. Keep the readability gains
    // here so animation, attack cues and the death sample cannot overwrite them.
    public void SetReadability(float surfaceGain, float rimGain)
    {
        surfaceReadability = Mathf.Max(0, surfaceGain);
        rimReadability = Mathf.Max(0, rimGain);
    }

    static Color GainRgb(Color color, float gain)
    {
        return new Color(color.r * gain, color.g * gain, color.b * gain, color.a);
    }

    void ApplySurfaceTint(int index, Color color, Color spirit)
    {
        tint.SetColor("_Color", GainRgb(color, surfaceReadability));
        tint.SetColor("_Spirit", GainRgb(spirit, rimReadability));
        surfaces[index].SetPropertyBlock(tint);
    }

    public void BeginDeath()
    {
        if (dying) return;
        dying = true; DeathPoseProgress = 0;
        deathOrigin = transform.position; deathFacing = transform.rotation;
        deathBones = ((SkinnedMeshRenderer)surfaces[0]).bones;
        deathStartPose = new Quaternion[deathBones.Length];
        deathStaggerPose = new Quaternion[deathBones.Length];
        for (int i = 0; i < deathBones.Length; ++i) deathStartPose[i] = deathBones[i].localRotation;
        var stagger = Array.Find(Clips, clip => clip != null && clip.name == "Stagger");
        if (stagger != null) stagger.SampleAnimation(gameObject, stagger.length * .55f);
        for (int i = 0; i < deathBones.Length; ++i) {
            deathStaggerPose[i] = deathBones[i].localRotation;
            deathBones[i].localRotation = deathStartPose[i];
        }
        SampleDeath(0);
    }

    public void SampleDeath(float progress)
    {
        if (!dying) return;
        DeathPoseProgress = Mathf.Clamp01(progress);
        float recoil = Mathf.SmoothStep(0, 1, DeathPoseProgress / .25f);
        float collapse = Mathf.SmoothStep(0, 1, (DeathPoseProgress - .22f) / .7f);
        for (int i = 0; i < deathBones.Length; ++i) {
            var bone = deathBones[i];
            Quaternion pose = Quaternion.Slerp(deathStartPose[i], deathStaggerPose[i], recoil);
            Vector3 fall = bone.name == "Head" ? new Vector3(34, 0, -10) :
                bone.name == "Chest" ? new Vector3(24, 0, 8) :
                bone.name == "UpperArm.L" ? new Vector3(18, 0, -42) :
                bone.name == "UpperArm.R" ? new Vector3(18, 0, 42) : Vector3.zero;
            bone.localRotation = pose * Quaternion.Euler(fall * collapse);
        }
        transform.SetPositionAndRotation(deathOrigin + deathFacing * new Vector3(.14f * collapse, .06f * collapse, .25f * collapse),
            deathFacing * Quaternion.Euler(78 * collapse, 0, -12 * collapse));
        tint.Clear(); tint.SetMatrix(WorldToHead, head.worldToLocalMatrix);
        tint.SetFloat("_CrystalEyes", 1 - collapse);
        float dissolve=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.42f,.89f,DeathPoseProgress));
        tint.SetFloat("_MistDissolve",dissolve);
        if (moonSource != null) tint.SetVector("_MoonOrigin", MoonOrigin(deathOrigin));
        for (int i = 0; i < surfaces.Length; ++i) {
            surfaces[i].enabled=dissolve<1;
            ApplySurfaceTint(i,
                Color.Lerp(originalColors[i], originalColors[i] * .42f, collapse),
                Color.Lerp(new Color(.49f, .62f, .85f), new Color(.08f, .09f, .13f), collapse));
        }
    }

    // Advance from the same absolute, unscaled clock as the ending subtitles.
    // The cloud stays at the place of death as the bones collapse beneath it.
    public void SampleDeathMist(float elapsed,float duration,float fade,bool onStage)
    {
        if (!dying || !onStage) { ClearDeathMist(); return; }
        float age=Mathf.Max(0,elapsed-duration*.14f);
        if (age<=deathMistAge) return;
        if (deathMist==null) {
            var prefab=Resources.Load<GameObject>(MinorEnemyVisuals.ImportedEffects+"vfx_Smoke_01");
            if (!prefab) return;
            deathMist=new ImportedEnemyEffect(prefab,transform.parent,deathOrigin+Vector3.up*.85f,false);
            deathMist.Transform.name="Shadow demon death mist";
            deathMist.Transform.rotation=Quaternion.Euler(-90,0,0);
            deathMist.DisableNativeShadows();
            foreach(var particle in deathMist.Transform.GetComponentsInChildren<ParticleSystem>()) {
                var main=particle.main;
                main.simulationSpace=ParticleSystemSimulationSpace.Local;
                main.startSpeedMultiplier*=.4f;
            }
            deathMistRenderers=deathMist.Transform.GetComponentsInChildren<ParticleSystemRenderer>();
            var smokeShader=Resources.Load<Shader>("DeathMist");
            if(smokeShader!=null && deathMistRenderers.Length>0) {
                deathMistMaterial=new Material(smokeShader) {mainTexture=deathMistRenderers[0].sharedMaterial.mainTexture};
                foreach(var renderer in deathMistRenderers)renderer.sharedMaterial=deathMistMaterial;
            }
            deathMistTint=new MaterialPropertyBlock();
        }
        float bloom=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.14f,.75f,elapsed/duration));
        deathMist.SetScale(Mathf.Lerp(2,DeathMistScale,bloom));
        deathMistTint.SetColor("_Color",new Color(.17f,.15f,.2f,.82f*(1-fade)));
        foreach(var renderer in deathMistRenderers)renderer.SetPropertyBlock(deathMistTint);
        deathMist.Tick(age-deathMistAge);
        deathMistAge=age;
    }

    void ClearDeathMist() {
        if(deathMist==null)return;
        deathMist.StopAndClear();
        deathMist.SetVisible(false);
        deathMist.Dispose();
        if(deathMistMaterial!=null)Destroy(deathMistMaterial);
        deathMistMaterial=null;
        deathMist=null;deathMistAge=0;
    }

    public void ResetDeath()
    {
        ClearDeathMist();
        if (!dying) return;
        dying = false; DeathPoseProgress = 0;
        transform.SetPositionAndRotation(deathOrigin, deathFacing);
        if (deathBones != null) for (int i = 0; i < deathBones.Length; ++i)
            if (deathBones[i] != null) deathBones[i].localRotation = deathStartPose[i];
        tint.Clear(); tint.SetMatrix(WorldToHead, head.worldToLocalMatrix);
        if (moonSource != null) tint.SetVector("_MoonOrigin", MoonOrigin(deathOrigin));
        for (int i = 0; i < surfaces.Length; ++i) {
            surfaces[i].enabled=true;
            ApplySurfaceTint(i, originalColors[i], new Color(.49f, .62f, .85f));
        }
    }

    Vector3 MoonOrigin(Vector3 receiver) {
        // The original hall uses a point at its moon window. A directional moon
        // has no meaningful position; place a distant proxy along its incoming
        // direction so the rim remains correct at any authored world elevation.
        return moonSource.type==LightType.Directional
            ? receiver-moonSource.transform.forward*10000f : moonSource.transform.position;
    }

    void Awake()
    {
        surfaces = GetComponentsInChildren<SkinnedMeshRenderer>();
        head=Array.Find(((SkinnedMeshRenderer)surfaces[0]).bones,b=>b.name=="Head");
        silk=new Material[surfaces.Length];
        var shader=Resources.Load<Shader>("BellSleeveSilk");
        originalColors = new Color[surfaces.Length];
        tint = new MaterialPropertyBlock();
        for (int i = 0; i < surfaces.Length; i++)
        {
            var original=surfaces[i].sharedMaterial;
            originalColors[i]=original.color;
            silk[i]=new Material(shader);
            silk[i].SetTexture("_MainTex",original.mainTexture);
            silk[i].SetTexture("_MetallicGlossMap",original.GetTexture("_MetallicGlossMap"));
            silk[i].color=originalColors[i];
            surfaces[i].sharedMaterial=silk[i];
        }
    }

    void OnDestroy() {
        ClearDeathMist();
        if(silk!=null) foreach(var mat in silk) if(mat) Destroy(mat);
    }

    public void SyncPose(Transform caster, HuntBoss boss, ClawState claw, float stun, float dt)
    {
        if (dying) return;
        transform.SetPositionAndRotation(caster.position, caster.rotation);
        bool moving = dt > 0 && (caster.position-lastPosition).sqrMagnitude > .000001f;
        lastPosition = caster.position;
        string motion;
        switch (boss.Action)
        {
            case HuntBoss.Move.Claw:
            case HuntBoss.Move.MarkFloor:
            case HuntBoss.Move.Rift:
            case HuntBoss.Move.Burst:
            case HuntBoss.Move.SleeveTell:
            case HuntBoss.Move.SleeveSweep:
            case HuntBoss.Move.BellTell:
            case HuntBoss.Move.BellWaves: motion = "Cast"; break;
            case HuntBoss.Move.Ritual: motion = "Ritual"; break;
            case HuntBoss.Move.LungeTell: motion = "Windup"; break;
            case HuntBoss.Move.Lunge:
            case HuntBoss.Move.Pounce: motion = "Lunge"; break;
            case HuntBoss.Move.Pursue: motion = "Pursue"; break;
            case HuntBoss.Move.LightStagger: motion = "Stagger"; break;
            case HuntBoss.Move.Recover: motion = stun > 0 ? "Stagger" : "Idle"; break;
            default: motion = moving ? "Stalk" : "Idle"; break;
        }
        if (dt == 0 && active != null && boss.Action == HuntBoss.Move.Stalk) motion = active.name;
        var clip = Array.Find(Clips, c => c != null && c.name == motion);
        if (clip == null) return;
        if (active != clip) { active = clip; age = 0; }
        age += dt;
        float time = clip.wrapMode == WrapMode.Loop ? Mathf.Repeat(age,clip.length) : Mathf.Min(age,clip.length);
        if (boss.Action == HuntBoss.Move.Claw && claw.Active)
            time = clip.length * Mathf.Clamp01(claw.Age/ClawState.EndTime);
        clip.SampleAnimation(gameObject,time);
        tint.SetMatrix(WorldToHead,head.worldToLocalMatrix);
        if(moonSource!=null)tint.SetVector("_MoonOrigin",MoonOrigin(caster.position));
        Color cue = boss.Action == HuntBoss.Move.LightStagger ? new Color(1,1,.65f) :
                    boss.Enraged ? new Color(1,.08f,.04f) :
                    boss.Action == HuntBoss.Move.LungeTell ? new Color(1,.3f,.08f) :
                    stun > 0 ? Color.cyan : Color.clear;
        for (int i=0;i<surfaces.Length;i++)
        {
            var rim=boss.Enraged ? new Color(.85f,.22f,.30f) : boss.Action==HuntBoss.Move.LightStagger ?
                new Color(.52f,.82f,.95f) : new Color(.49f,.62f,.85f);
            ApplySurfaceTint(i, cue.a==0 ? originalColors[i] : Color.Lerp(originalColors[i],cue,.26f), rim);
        }
    }
}

