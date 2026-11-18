using System;
using System.IO;
using UnityEngine;

// Opt-in Windows player acceptance check. Drives the same movement, light and combat code as play.
public static class HallPlayCheck {
    static void Require(bool ok,string text) {
        if(!ok) throw new InvalidOperationException(text);
        Debug.Log("PASS "+text);
    }
    static void CheckLanternCages(IronHallEncounter hall,string phase) {
        var overrides=new MaterialPropertyBlock();
        int tint=Shader.PropertyToID("_Color");
        foreach(var cage in hall.Lanterns) {
            var mesh=cage.GetComponent<MeshFilter>().sharedMesh;
            cage.GetPropertyBlock(overrides);
            var material=cage.sharedMaterial;
            var color=overrides.HasColor(tint) ? overrides.GetColor(tint) : material.GetColor(tint);
            // The Standard cutout shader multiplies pane texture alpha by tint alpha.
            // Even the solid frame texels disappear when this falls below the cutoff.
            if(!cage.gameObject.activeInHierarchy || !cage.enabled || mesh==null || mesh.vertexCount==0 ||
               color.a<=material.GetFloat("_Cutoff"))
                throw new InvalidOperationException(phase+" hid the frame of "+cage.name);
        }
        Debug.Log("PASS all six authored lantern meshes retain their solid cage in "+phase);
    }
    static void CheckFlameMarker(Transform fixture,bool dormant,string phase) {
        var flame=fixture.GetComponentInChildren<LanternFire>();
        Require(flame!=null && flame.GetComponentsInChildren<Light>(true).Length==0,
            phase+" uses a flame sprite without any illumination source");
        var properties=new MaterialPropertyBlock();
        foreach(var stream in flame.GetComponentsInChildren<ParticleSystem>()) {
            var renderer=stream.GetComponent<ParticleSystemRenderer>();
            renderer.GetPropertyBlock(properties);
            Require(stream.particleCount>0 && renderer.enabled &&
                properties.GetFloat("_Dormant")== (dormant ? 1 : 0) && properties.GetFloat("_Power")>0,
                phase+(dormant ? " displays the violet unlit marker" : " displays the warm lit flame")+" on "+stream.name);
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() {
        var args=Environment.GetCommandLineArgs();
        if(Array.IndexOf(args,"-check-throne")<0) return;
        try {
            var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
            var hall=lab.GetComponent<IronHallEncounter>();
            var eye=lab.GetComponentInChildren<Camera>();
            var body=lab.transform.Find("Hunter capsule");
            var evidence=Array.IndexOf(args,"-hall-evidence");
            Color32[] Capture(string name,Vector3 from,Vector3 target,bool sample=false) {
                if(evidence<0) return null;
                string folder=args[evidence+1];
                Directory.CreateDirectory(folder);
                var position=eye.transform.position; var rotation=eye.transform.rotation;
                var old=RenderTexture.active;
                bool frame=name.StartsWith("combat-");
                var buffer=RenderTexture.GetTemporary(frame?960:1280,frame?540:720,24);
                eye.transform.position=from; eye.transform.LookAt(target);
                eye.targetTexture=buffer; eye.Render(); RenderTexture.active=buffer;
                var image=new Texture2D(buffer.width,buffer.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,buffer.width,buffer.height),0,0); image.Apply();
                if(name.Length>0)
                    File.WriteAllBytes(Path.Combine(folder,name+(frame?".jpg":".png")),frame?image.EncodeToJPG(89):image.EncodeToPNG());
                var pixels=sample ? image.GetPixels32() : null;
                eye.targetTexture=null; RenderTexture.active=old; RenderTexture.ReleaseTemporary(buffer);
                UnityEngine.Object.Destroy(image); eye.transform.SetPositionAndRotation(position,rotation);
                return pixels;
            }
            void Reset() => lab.SendMessage("ResetLab");
            void Tick() => lab.SendMessage("Tick",.02f);
            void WalkTo(float x,float z) {
                Vector3 goal=new Vector3(x,0,z);
                float last=body.position.y, maxStep=0;
                for(int i=0;i<550;i++) {
                    Vector3 delta=goal-body.position; delta.y=0;
                    if(delta.magnitude<.075f) {
                        Require(maxStep<.18f,"continuous walk to "+goal+" maximum height step "+maxStep.ToString("F3"));
                        return;
                    }
                    lab.Walk(delta.normalized,.02f); Tick();
                    maxStep=Mathf.Max(maxStep,Mathf.Abs(body.position.y-last)); last=body.position.y;
                    if(lab.Finished) break;
                }
                throw new InvalidOperationException("route to "+goal+" stopped at "+body.position);
            }
            Require(lab.gameObject.scene.name=="IronCrownHall" && hall.RaisedSurfaces.Length==3,
                "Iron Crown Hall with two stair slopes and dais loaded");
            WalkTo(0,-6.75f);
            Require(Mathf.Abs(body.position.y-1.05f)<.045f,"front ascent onto dais at "+body.position);
            WalkTo(0,-3.08f);
            Require(Mathf.Abs(body.position.y)<.045f,"descent to paving at "+body.position);
            foreach(float side in new[]{-1f,1f}) {
                Reset();
                WalkTo(side*4.45f,1.1f);
                WalkTo(side*4.45f,-7.4f);
                Require(Mathf.Abs(body.position.y)<.05f,"ground-level bypass at "+body.position);
            }
            Reset();
            Vector3 entrance=new Vector3(-1.35f,2.34f,8.48f);
            var pane=GameObject.Find("Moon window glazing").GetComponent<MeshFilter>();
            var roof=GameObject.Find("Pointed stone vault").GetComponent<MeshFilter>();
            Vector3 paneCenter=pane.GetComponent<Renderer>().bounds.center;
            Vector3 vaultCenter=roof.GetComponent<Renderer>().bounds.center;
            Require(Vector2.Distance(new Vector2(paneCenter.x,paneCenter.z),
                new Vector2(vaultCenter.x,vaultCenter.z))<.015f,"oculus is centered on both axes of the vault");
            var probe=roof.gameObject.AddComponent<MeshCollider>();
            var glass=pane.gameObject.AddComponent<MeshCollider>();
            probe.sharedMesh=roof.sharedMesh; glass.sharedMesh=pane.sharedMesh;
            var rib=GameObject.Find("Transverse rib 3").AddComponent<MeshCollider>();
            rib.sharedMesh=rib.GetComponent<MeshFilter>().sharedMesh;
            bool backfacePolicy=Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces=true; Physics.SyncTransforms();
            bool circularOpening=true;
            Vector3 below=new Vector3(paneCenter.x,2,paneCenter.z);
            for(int i=0;i<24;i++) {
                float angle=(i+.3f)*Mathf.PI/12;
                Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Ray inner=new Ray(below+radial*1.38f,Vector3.up);
                Ray outer=new Ray(below+radial*1.62f,Vector3.up);
                circularOpening &= glass.Raycast(inner,out _,15) && !probe.Raycast(inner,out _,15)
                    && !rib.Raycast(inner,out _,15) && probe.Raycast(outer,out _,15) && !glass.Raycast(outer,out _,15);
            }
            Require(circularOpening && !rib.Raycast(new Ray(below,Vector3.up),out _,15),
                "round skylight clear through the roof and central rib with intact masonry all around");
            Require(probe.Raycast(new Ray(new Vector3(4.13f,2,-2.56f),Vector3.up),out _,15),
                "former off-center window is solid stone");
            Physics.queriesHitBackfaces=backfacePolicy;
            UnityEngine.Object.DestroyImmediate(glass); UnityEngine.Object.DestroyImmediate(probe);
            UnityEngine.Object.DestroyImmediate(rib);
            var moon=hall.Moonlight;
            var shaft=GameObject.Find("Moonshaft air").transform;
            Require(moon.enabled && moon.type==LightType.Spot && Vector3.Angle(moon.transform.forward,Vector3.down)<.01f
                && Vector3.Distance(moon.transform.position,paneCenter)<.2f
                && Vector3.Distance(shaft.TransformPoint(Vector3.back*.5f),paneCenter)<.02f
                && Vector3.Distance(shaft.TransformPoint(Vector3.forward*.5f),new Vector3(paneCenter.x,0,paneCenter.z))<.03f,
                "moonlight and visible shaft descend vertically from circular glazing to the middle of the floor");
            var fixedLights=moon.transform.parent.GetComponentsInChildren<Light>(true);
            Require(hall.GalleryLights.Length==6 && fixedLights.Length==7 &&
                Array.TrueForAll(fixedLights,light=>light==moon || Array.IndexOf(hall.GalleryLights,light)>=0),
                "the overhead oculus and six gallery lanterns are the only fixed light sources");
            if(evidence>=0) {
                var air=shaft.GetComponent<Renderer>();
                var bow=eye.GetComponentInChildren<HunterWeaponView>().gameObject;
                bool held=bow.activeSelf;
                bow.SetActive(false);
                try {
                    foreach(int side in new[]{-1,1}) {
                        // Look outward from beyond the full footprint of the shaft. No part of this
                        // wall view should change when we toggle the oculus and its scattering together.
                        var from=new Vector3(side*3.55f,2.4f,2.5f);
                        var at=new Vector3(side*6.5f,2.6f,2.5f);
                        var lit=Capture(side<0 ? "west-wall" : "east-wall",from,at,true);
                        moon.enabled=false;air.enabled=false;
                        var control=Capture("",from,at,true);
                        moon.enabled=true;air.enabled=true;
                        int altered=0;
                        for(int i=0;i<lit.Length;i++) {
                            if(Math.Abs(lit[i].r-control[i].r)>3 || Math.Abs(lit[i].g-control[i].g)>3 ||
                               Math.Abs(lit[i].b-control[i].b)>3) altered++;
                        }
                        Require(altered<25,"oculus on/off changes "+altered+" pixels on gallery wall "+side);
                    }
                } finally { moon.enabled=true; air.enabled=true; bow.SetActive(held); }
            }
            Capture("moon-window",new Vector3(.5f,4.1f,1.8f),paneCenter);
            Capture("moon-shaft",new Vector3(3.25f,1.9f,8.2f),new Vector3(0,5.3f,0));
            Capture("throne",new Vector3(.92f,1.88f,-2.8f),new Vector3(0,2.2f,-8.55f));
            Capture("spawn",eye.transform.position,eye.transform.position+eye.transform.forward);
            Capture("entrance",entrance,new Vector3(0,2.7f,-7.4f));
            var aimFixture=lab.transform.Find("左中灯");
            CheckLanternCages(hall,"initial mixed lit/dark state");
            CheckFlameMarker(aimFixture,true,"initially unlit lantern");
            var aimVolume=aimFixture.GetComponent<SphereCollider>();
            Require(aimVolume.isTrigger && aimVolume.radius>=.45f,"lantern aim volume is enlarged and does not block movement");
            Vector3 towards=(aimFixture.position-eye.transform.position).normalized;
            Vector3 offset=Vector3.Cross(Vector3.up,towards).normalized*.37f;
            eye.transform.LookAt(aimFixture.position+offset);
            lab.Shoot();
            Require(lab.LitLamps==3 && hall.GalleryLights[1].enabled,"a shot wide of the old wick relights the gallery lantern");
            CheckFlameMarker(aimFixture,false,"arrow relight");
            CheckLanternCages(hall,"arrow relight");
            Reset();
            var lamp=hall.GalleryLights[0]; var flame=lab.transform.Find("左前灯/Flame");
            var flames=flame.GetComponentInChildren<ParticleSystem>();
            Require(flames.particleCount>0,"lit gallery fixture emits real flame particles");
            var animation=flames.textureSheetAnimation;
            Require(flames.GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture.name=="NyrthosFire" &&
                animation.enabled && animation.numTilesX*animation.numTilesY==40 && animation.fps==25,
                "the wick plays the imported 40-frame fire animation");
            float intensity=lamp.intensity, range=lamp.range, size=flame.localScale.y;
            Vector3 wallView=new Vector3(2.5f,2.15f,-.9f);
            void LightShot(string name) => Capture(name,wallView,lamp.transform.position);
            LightShot("strong");
            // Advance only the actual lamp clock to isolate its natural lifetime from Boss hits.
            void Age(float seconds) {
                for(int i=0;i<Mathf.RoundToInt(seconds*50);i++) {
                    lab.SendMessage("AgeLamps",.02f);
                    lab.SendMessage("DrawLamps");
                }
            }
            Age(11.2f);
            Require(lamp.intensity<intensity && lamp.intensity>.55f*intensity,"smooth strong-to-medium transition");
            Age(2);
            Require(Mathf.Abs(lamp.intensity/intensity-.55f)<.01f && lamp.range<range,"medium light at 55 percent");
            LightShot("medium");
            Age(7.2f);
            Require(Mathf.Abs(lamp.intensity/intensity-.25f)<.01f && flame.localScale.y<size &&
                lab.ShadowCount==1 && lamp.enabled,"weak light at 25 percent keeps projection and smaller flame");
            LightShot("weak");
            var shadow=lab.DemonShadowCenter;
            eye.transform.LookAt(new Vector3(shadow.x,.015f,shadow.y)); lab.Shoot();
            Require(lab.Damage==10,"weak light projection takes a live crossbow hit");
            Age(3.5f); Require(lamp.enabled && lab.ShadowCount==1,"weak light remains through 23.9 seconds");
            Age(.12f); Require(!lamp.enabled && lab.ShadowCount==0 && lab.LitLamps==0,"natural expiration after 24 seconds");
            CheckFlameMarker(flame.parent,true,"natural expiration");
            CheckLanternCages(hall,"natural extinction");
            LightShot("out");
            if(evidence>=0) {
                // Same camera and simulation tick, only the extinguished ironwork changes.
                var cage=hall.Lanterns[0];
                var center=cage.bounds.center;
                var from=center+new Vector3(-Mathf.Sign(center.x)*1.48f,.10f,-.63f);
                var viewmodel=eye.GetComponentInChildren<HunterWeaponView>().gameObject;
                bool wasActive=viewmodel.activeSelf;
                viewmodel.SetActive(false);
                var ironwork=Capture("extinguished-lantern",from,center,true);
                cage.enabled=false;
                var bareWall=Capture("",from,center,true);
                cage.enabled=true;
                viewmodel.SetActive(wasActive);
                int visiblePixels=0, highlightedPixels=0;
                for(int i=0;i<ironwork.Length;i++) {
                    var a=ironwork[i]; var b=bareWall[i];
                    if(Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Max(Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b)))>=3)
                        visiblePixels++;
                    int ironLuma=(a.r*54+a.g*183+a.b*19)/256;
                    int wallLuma=(b.r*54+b.g*183+b.b*19)/256;
                    if(ironLuma>=24 && ironLuma-wallLuma>=9) highlightedPixels++;
                }
                Require(visiblePixels>500,"extinguished lantern contributes "+visiblePixels+" visible pixels against the bare wall");
                Require(highlightedPixels>500,"unlit ironwork has "+highlightedPixels+" legible highlight pixels above its dark mount");
            }
            Reset();
            for(int i=0;i<450 && lab.RitualTarget==null;i++) Tick();
            var target=lab.RitualTarget;
            Require(target!=null,"Boss starts a lamp ritual");
            var soot=lab.transform.Find(target.name+" extinguish").GetComponent<LineRenderer>();
            var light=Array.Find(hall.GalleryLights,l=>Vector3.Distance(l.transform.position,target.position)<1);
            float startReach=Vector3.Distance(soot.GetPosition(0),soot.GetPosition(1));
            Vector3 ritualView=target.position+new Vector3(-Mathf.Sign(target.position.x)*3.3f,-.45f,1.9f);
            Capture("ritual",ritualView,target.position);
            float before=light.intensity;
            for(int i=0;i<150 && light.enabled;i++) { before=light.intensity; Tick(); }
            Require(!light.enabled && before>intensity*.95f && lab.LitLamps==1 && soot.enabled &&
                Vector3.Distance(soot.GetPosition(0),soot.GetPosition(1))<startReach,
                "Boss soot contracts and a strong lamp goes out on the hit tick");
            CheckLanternCages(hall,"Boss extinction");
            CheckFlameMarker(target,true,"Boss extinction");
            Capture("ritual-hit",ritualView,target.position);
            // Light the four dark fixtures via real shots. Watch individual lamps age
            // while the hunter moves and the Boss is free to extinguish other fixtures.
            Reset();
            // A 24 s lighting run must outlive a lethal combination of Boss attacks.
            // F2 affects only the hunter's HP; all attacks and lamp rituals still resolve.
            lab.Developer.HandleKey(KeyCode.F2);
            string[] fixtures={"左前灯","左中灯","左后灯","右前灯","右中灯","右后灯"};
            var litAt=new float[]{0,-1,-1,0,-1,-1};
            var weakSeen=new bool[6];
            var forciblyHit=new bool[6];
            var wasLit=new bool[6];
            bool natural=false;
            for(int frame=0;frame<1400 && !lab.Finished && !natural;frame++) {
                float elapsed=frame*.02f;
                if(lab.Weapon.Rounds>0 && lab.Weapon.Phase==HunterCrossbow.Motion.Ready) {
                    int next=Array.FindIndex(litAt,t=>t<0);
                    if(next>=0) {
                        eye.transform.LookAt(lab.transform.Find(fixtures[next]).position);
                        lab.Shoot();
                        if(hall.GalleryLights[next].enabled) litAt[next]=elapsed;
                    }
                }
                lab.Walk(new Vector3(Mathf.Cos(elapsed*.65f),0,-Mathf.Sin(elapsed*.65f)),.02f);
                var threat=lab.RitualTarget;
                for(int i=0;i<wasLit.Length;i++) wasLit[i]=hall.GalleryLights[i].enabled;
                Tick();
                for(int i=0;i<6;i++) {
                    if(litAt[i]<0) continue;
                    var fixture=hall.GalleryLights[i];
                    if(fixture.enabled && fixture.intensity<=.26f*intensity) weakSeen[i]=true;
                    if(!wasLit[i] || fixture.enabled) continue;
                    if(threat!=null && threat.name==fixtures[i]) forciblyHit[i]=true;
                    float age=elapsed+.02f-litAt[i];
                    if(!forciblyHit[i] && weakSeen[i] && Mathf.Abs(age-24f)<=.021f) {
                        Debug.Log("Live natural expiry "+fixtures[i]+" at "+age.ToString("F2")+" seconds");
                        natural=true;
                    }
                }
            }
            lab.Developer.HandleKey(KeyCode.F2);
            Require(Array.Exists(forciblyHit,hit=>hit),"Boss extinguishes another lamp during the natural lifetime test");
            Require(natural,"a lamp completes its natural lifetime during a live fight");
            if(evidence>=0 && Array.IndexOf(args,"-record-combat")>=0) {
                Reset();
                var spectacle=lab.GetComponentInChildren<BossSpectacle>();
                bool charging=false;
                for(int frame=0;frame<67;frame++) {
                    lab.SendMessage("Tick",1f/24);
                    charging|=spectacle.DangerOutline.enabled && spectacle.AttackParticleCount>0;
                    Capture("combat-"+frame.ToString("D3"),new Vector3(-3.45f,2.75f,6.15f),new Vector3(-.25f,.68f,2.7f));
                }
                Require(charging && spectacle.ImpactCount>0,"recorded real claw wind-up, boundary and release");
            }
            Debug.Log("THRONE_PLAYER_PASS");
            Application.Quit(0);
        } catch(Exception e) { Debug.LogException(e); Application.Quit(1); }
    }
}
