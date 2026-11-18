using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Opt-in player integration check. It operates the shipped uGUI controls and commands;
// controlled combat fixtures are recorded separately from physical-input/manual play.
public sealed class ShiyinUICheck : MonoBehaviour {
    [Serializable] sealed class Evidence {
        public string name, file;
        public int width, height, sampledBrightPixels;
    }
    [Serializable] sealed class Report {
        public string status="RUNNING", startedUtc, completedUtc, unity, device, error;
        public bool batchMode;
        public List<string> checks=new List<string>();
        public List<string> fixtures=new List<string>();
        public List<string> limitations=new List<string>();
        public List<Evidence> screenshots=new List<Evidence>();
    }
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    readonly Report report=new Report();
    ShadowLab lab;
    ShiyinUI ui;
    string folder;
    Color32[] visibleHud;
    int originalWidth, originalHeight;
    int battleStarts;
    float watchdogDeadline;
    bool completed;
    double dimBorderLuminance;
    readonly List<string> unexpectedErrors=new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-ui")<0)return;
        Application.runInBackground=true;
        new GameObject("Shiyin UI acceptance check").AddComponent<ShiyinUICheck>();
    }

    IEnumerator Start() {
        var args=Environment.GetCommandLineArgs();
        int option=Array.IndexOf(args,"-ui-evidence");
        folder=option>=0 && option+1<args.Length ? Path.GetFullPath(args[option+1]) :
            Path.Combine(Application.persistentDataPath,"Shiyin-UI-evidence");
        Directory.CreateDirectory(folder);
        report.startedUtc=DateTime.UtcNow.ToString("o");
        report.unity=Application.unityVersion;
        report.device=SystemInfo.graphicsDeviceType+" / "+SystemInfo.graphicsDeviceName;
        report.batchMode=Application.isBatchMode;
        report.limitations.Add("Buttons use their actual onClick listeners; keys use the same public HandleKey command entry. Physical OS keyboard/mouse injection is a separate manual acceptance step.");
        report.limitations.Add("The explicit -check-ui mode isolates OS input and window-focus changes, and enables runInBackground. Actual pages, pause reasons, scaled time and live encounter Tick remain active; the UI is not bypassed.");
        watchdogDeadline=Time.realtimeSinceStartup+180;
        Application.logMessageReceived+=ObserveLog;
        // Flatten nested routines so their failures also produce a report and nonzero exit.
        var stack=new Stack<IEnumerator>();stack.Push(Exercise());
        Exception failure=null;
        while(stack.Count>0) {
            object yielded=null;bool more=false;
            try { more=stack.Peek().MoveNext();if(more)yielded=stack.Peek().Current; }
            catch(Exception e) { failure=e;break; }
            if(!more) { stack.Pop();continue; }
            if(yielded is IEnumerator nested) { stack.Push(nested);continue; }
            yield return yielded;
        }
        if(failure!=null && ui!=null) {
            yield return new WaitForEndOfFrame();
            Texture2D failedImage=null;
            try {
                failedImage=ScreenCapture.CaptureScreenshotAsTexture();
                if(failedImage!=null) {
                    File.WriteAllBytes(Path.Combine(folder,"failure-state.png"),failedImage.EncodeToPNG());
                    report.screenshots.Add(new Evidence{name="failure-state",file="failure-state.png",width=failedImage.width,height=failedImage.height});
                }
            } catch(Exception captureFailure) {report.limitations.Add("Failure screenshot unavailable: "+captureFailure.Message);}
            finally {if(failedImage!=null)Destroy(failedImage);}
        }
        Finish(failure);
    }
    void Update() {
        if(completed || watchdogDeadline<=0 || Time.realtimeSinceStartup<watchdogDeadline)return;
        StopAllCoroutines();
        Finish(new TimeoutException("UI acceptance exceeded 180 real seconds. A rendered-frame wait may not be supported by this launch mode; no uncompleted checks are claimed."));
    }
    void Finish(Exception failure) {
        if(completed)return;
        completed=true;
        Application.logMessageReceived-=ObserveLog;
        if(failure==null && unexpectedErrors.Count>0)
            failure=new InvalidOperationException("Unexpected player errors: "+string.Join(" | ",unexpectedErrors));
        report.status=failure==null ? "PASS" : "FAIL";
        report.error=failure?.ToString();
        report.completedUtc=DateTime.UtcNow.ToString("o");
        File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(report,true));
        if(failure==null)Debug.Log("SHIYIN_UI_PLAYER_PASS "+folder);
        else Debug.LogException(failure);
        Application.Quit(failure==null ? 0 : 1);
    }
    void ObserveLog(string condition,string trace,LogType type) {
        if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)
            unexpectedErrors.Add(condition);
    }
    void Require(bool condition,string message) {
        if(!condition)throw new InvalidOperationException(message);
        report.checks.Add(message);Debug.Log("PASS UI "+message);
    }
    static object Read(object target,string name) {
        var type=target.GetType();
        var property=type.GetProperty(name,Members);
        return property!=null ? property.GetValue(target) : type.GetField(name,Members)?.GetValue(target);
    }
    static void Invoke(object target,string name,params object[] args) {
        var method=target.GetType().GetMethod(name,Members);
        if(method==null)throw new MissingMethodException(target.GetType().Name,name);
        method.Invoke(target,args);
    }
    float Preference(string name)=>Convert.ToSingle(Read(ui.Settings,name));
    void Click(string action) {
        var button=ui.GetControl(action) as Button;
        Require(button!=null && button.gameObject.activeInHierarchy && button.IsInteractable(),"active button "+action);
        ui.FocusControl(action);
        button.onClick.Invoke();
    }
    void Key(KeyCode key) { Require(ui.HandleKey(key),"handled key "+key); }
    IEnumerator Frames(int count=2) { for(int i=0;i<count;i++)yield return null; }
    IEnumerator Until(Func<bool> condition,string description,float timeout=8) {
        float end=Time.realtimeSinceStartup+timeout;
        while(!condition() && Time.realtimeSinceStartup<end)yield return null;
        Require(condition(),description);
    }
    IEnumerator View(string name) {
        yield return Until(()=>ui.CurrentView==name,"view "+name);
        yield return Frames();
    }
    IEnumerator Capture(string name,bool savePixels=false) {
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        var image=ScreenCapture.CaptureScreenshotAsTexture();
        Require(image!=null && image.width>0 && image.height>0,"screen capture "+name);
        try {
            var pixels=image.GetPixels32();int varied=0;
            for(int i=0;i<pixels.Length;i+=31)if(pixels[i].r>35 || pixels[i].g>35 || pixels[i].b>35)varied++;
            File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());
            report.screenshots.Add(new Evidence {name=name,file=name+".png",width=image.width,height=image.height,sampledBrightPixels=varied});
            Require(varied>8,"rendered content in "+name+" (bright samples "+varied+")");
            if(name=="brightness-low")dimBorderLuminance=BorderLuminance(pixels,image.width,image.height);
            if(name=="brightness-high") {
                double lit=BorderLuminance(pixels,image.width,image.height);
                Require(lit>dimBorderLuminance+.35 && lit>dimBorderLuminance*1.03,
                    "brightness slider changes actual rendered scene outside UI panels (luminance "+dimBorderLuminance.ToString("F2")+" -> "+lit.ToString("F2")+")");
            }
            if(savePixels)visibleHud=pixels;
            if(name=="hud-hidden" && visibleHud!=null) {
                Require(visibleHud.Length==pixels.Length,"HUD comparison resolution is unchanged");
                int changed=0;
                for(int i=0;i<pixels.Length;i++) {
                    var a=pixels[i];var b=visibleHud[i];
                    if(Mathf.Abs(a.r-b.r)>8 || Mathf.Abs(a.g-b.g)>8 || Mathf.Abs(a.b-b.b)>8)changed++;
                }
                Require(changed>150,"screen capture includes Overlay Canvas: hidden HUD changes "+changed+" pixels in a frozen scene");
                visibleHud=null;
            }
        } finally { Destroy(image); }
    }
    static double BorderLuminance(Color32[] pixels,int width,int height) {
        double sum=0;int count=0;
        // The settings panel is centered. These strips sample only its visible scene
        // backdrop, avoiding unchanged UI text and panel fills in the comparison.
        for(int y=height/5;y<height*4/5;y+=3)
            for(int x=width/25;x<width*11/100;x+=3)
                foreach(int sx in new[]{x,width-1-x}) {
                    var c=pixels[y*width+sx];sum+=.2126*c.r+.7152*c.g+.0722*c.b;count++;
                }
        return count>0 ? sum/count : 0;
    }
    object FirstLamp()=>((Array)Read(lab,"lamps")).GetValue(0);
    float LampRemaining()=>Convert.ToSingle(Read(FirstLamp(),"Remaining"));
    HuntBoss Boss()=>(HuntBoss)Read(lab,"boss");
    WallCombat Combat()=>(WallCombat)Read(lab,"combat");
    void DamageHunter(int amount) {
        Boss().Hurt(amount);Invoke(lab,"CheckEnd");
    }
    IEnumerator Ready() {
        yield return Until(()=>lab.Weapon.Phase==HunterCrossbow.Motion.Ready,"weapon returns to Ready",6);
    }
    IEnumerator StartBattle() {
        Click("start");
        if(battleStarts++==0 && ui.CurrentView=="loading")yield return Capture("loading");
        yield return Until(()=>ui.CurrentView=="onboarding" || ui.CurrentView=="battle","start reaches onboarding or battle");
        if(ui.CurrentView=="onboarding") {
            yield return Capture("onboarding");
            int steps=0;
            while(ui.CurrentView=="onboarding" && steps++<4) { Click("next-tutorial");yield return Frames(); }
        }
        yield return View("battle");
        Require(!ui.InputBlocked && Time.timeScale>0,"battle enables gameplay and time");
    }
    IEnumerator Exercise() {
        yield return Until(()=>UnityEngine.Rendering.SplashScreen.isFinished,"Unity startup splash has finished",8);
        yield return Frames(6);
        lab=FindFirstObjectByType<ShadowLab>();
        Require(lab!=null,"authored encounter exists");
        yield return Until(()=>lab.UI!=null,"native Shiyin UI attached");
        ui=lab.UI;
        originalWidth=Screen.width;originalHeight=Screen.height;
        Require(ui.Settings.PreferencePrefix!=ShiyinSettings.DefaultPreferencePrefix,"acceptance preferences are isolated from player preferences");
        Require(ui.UiCanvas!=null && ui.UiCanvas.renderMode==RenderMode.ScreenSpaceOverlay,"native overlay Canvas exists");
        Require(EventSystem.current!=null,"uGUI EventSystem exists");
        yield return View("menu");
        Require(ui.InputBlocked && Time.timeScale==0,"initial menu pauses actual encounter");
        yield return Capture("menu");
        Click("exit");yield return Frames();yield return Capture("confirm-exit");
        Require(ui.ConfirmationKey=="exit","exit requires an explicit confirmation");
        Click("cancel");yield return View("menu");

        Click("manual");yield return View("manual");
        yield return Capture("manual-combat");
        Click("manual:arrows");yield return Frames();yield return Capture("manual-arrows");
        Click("manual:lamps");yield return Frames();yield return Capture("manual-lamps");
        var manualScroll=ui.UiCanvas.GetComponentInChildren<ScrollRect>();
        Require(manualScroll!=null && manualScroll.content.rect.height>manualScroll.viewport.rect.height,"manual has real scrollable content");
        manualScroll.verticalNormalizedPosition=.23f;yield return Frames();float rememberedScroll=manualScroll.verticalNormalizedPosition;
        Click("tutorial");yield return View("onboarding");Click("skip-tutorial");yield return View("manual");
        Require(ui.CurrentTab=="lamps" && Mathf.Abs(ui.UiCanvas.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition-rememberedScroll)<.01f,
            "tutorial returns to the originating manual tab and scroll position");
        Require(EventSystem.current.currentSelectedGameObject==ui.GetControl("tutorial").gameObject,"subpage return restores originating keyboard focus");
        Click("back");yield return View("menu");
        Click("settings");yield return View("settings");
        yield return Capture("settings-interface");
        float oldScale=Preference("TextScale");Click("setting:TextScale");yield return Frames();
        Require(!Mathf.Approximately(oldScale,Preference("TextScale")),"native text-size control updates real preferences");
        var contrast=ui.GetControl("setting:Contrast") as Toggle;
        Require(contrast!=null,"contrast is a native Toggle");bool oldContrast=contrast.isOn;contrast.isOn=!oldContrast;
        yield return Frames();Require((Preference("Contrast")>0)!=oldContrast,"native contrast toggle updates real preference");
        var reduced=ui.GetControl("setting:ReducedMotion") as Toggle;
        Require(reduced!=null,"reduced motion uses a native Toggle");reduced.isOn=true;yield return Frames();
        foreach(var selectable in ui.UiCanvas.GetComponentsInChildren<Selectable>()) {
            Require(selectable.colors.fadeDuration==0,"reduced motion immediately removes transition for "+selectable.name);
            if(selectable is Toggle toggle)Require(toggle.toggleTransition==Toggle.ToggleTransition.None,"reduced motion removes toggle fade for "+toggle.name);
        }
        reduced.isOn=false;yield return Frames();
        Require(ui.GetControl("defaults").colors.fadeDuration>0,"disabling reduced motion immediately restores control transitions");
        foreach(var tab in new[]{"audio","display","controls"}) {
            Click("settings:"+tab);yield return Frames();
            Require(ui.CurrentTab==tab,"settings tab "+tab);
            if(tab=="audio") {
                var master=ui.GetControl("setting:Master") as Slider;
                Require(master!=null,"master volume uses native Slider");master.value=.42f;yield return Frames();
                Require(Mathf.Abs(AudioListener.volume-.42f)<.02f,"master slider updates real AudioListener volume");
            }
            if(tab=="display") {
                var fov=ui.GetControl("setting:Fov") as Slider;
                Require(fov!=null,"field of view uses native Slider");fov.value=80;yield return Frames();
                Require(Mathf.Abs(lab.GetComponentInChildren<Camera>().fieldOfView-80)<.02f,"FOV slider updates real gameplay camera");
                yield return BrightnessCheck();
                yield return DisplayRollbackCheck();
            }
            yield return Capture("settings-"+tab);
        }
        var sensitivity=ui.GetControl("setting:Sensitivity") as Slider;
        Require(sensitivity!=null,"sensitivity uses native Slider");
        float newSensitivity=Mathf.Lerp(sensitivity.minValue,sensitivity.maxValue,.68f);
        sensitivity.value=newSensitivity;yield return Frames();
        Require(Mathf.Abs(Preference("Sensitivity")-newSensitivity)<.02f,"sensitivity control saves real value");
        var persisted=new ShiyinSettings(ui.Settings.PreferencePrefix);persisted.Load();
        Require(Mathf.Abs(persisted.Sensitivity-newSensitivity)<.02f,"UI preference persists through an independent settings reload");
        Click("defaults");yield return Frames();
        Require(!string.IsNullOrEmpty(ui.ConfirmationKey),"restore defaults asks for confirmation");
        Require(EventSystem.current.currentSelectedGameObject==ui.GetControl("cancel").gameObject,"confirmation defaults to Cancel");
        Click("cancel");yield return Frames();
        Require(Mathf.Abs(Preference("Sensitivity")-newSensitivity)<.02f,"cancel defaults preserves preference");
        Click("defaults");yield return Frames();Click("confirm");yield return Frames();
        Require(string.IsNullOrEmpty(ui.ConfirmationKey),"confirmed defaults close dialog");
        Require(Mathf.Abs(lab.GetComponentInChildren<Camera>().fieldOfView-72)<.02f && Mathf.Abs(AudioListener.volume-.8f)<.02f,
            "restore defaults reapplies actual camera and audio");
        Click("back");yield return View("menu");
        yield return StartBattle();
        Require(lab.HunterHealth==100 && lab.Weapon.Rounds==4 && lab.Weapon.Loaded==Bolt.Holy,"real initial health and shared magazine");
        yield return Capture("battle");

        // Real developer lock prevents unrelated AI damage during navigation checks.
        Key(KeyCode.F2);Require(lab.Developer.HealthLocked,"QA navigation enables real health lock");
        Key(KeyCode.Escape);yield return View("pause");
        Require(ui.InputBlocked && Time.timeScale==0,"Escape pauses gameplay and scaled clock");
        float wick=LampRemaining();double clock=Time.timeAsDouble;
        var camera=lab.GetComponentInChildren<Camera>();Vector3 position=camera.transform.position;
        int rounds=lab.Weapon.Rounds;
        yield return new WaitForSecondsRealtime(.3f);
        Require(Time.timeAsDouble==clock && Mathf.Approximately(LampRemaining(),wick) && camera.transform.position==position,"pause stops lamp lifetime, scaled clock and camera");
        yield return Capture("pause");
        Click("settings");yield return View("settings");Click("settings:controls");yield return Frames();
        Click("back");yield return View("pause");
        Require(Time.timeScale==0 && lab.Weapon.Rounds==rounds,"settings returns to pause without firing");
        Click("retry");yield return Frames();yield return Capture("confirm-retry");
        Require(!string.IsNullOrEmpty(ui.ConfirmationKey),"ongoing retry requires confirmation");
        Click("cancel");yield return Frames();
        Require(ui.CurrentView=="pause" && lab.Weapon.Rounds==rounds && Mathf.Approximately(LampRemaining(),wick),"cancel retry preserves encounter");
        Click("menu");yield return Frames();yield return Capture("confirm-menu");Click("cancel");yield return Frames();
        Click("developer");yield return View("developer");yield return Capture("developer");Click("back");yield return View("pause");
        Click("back");yield return View("battle");

        Key(KeyCode.Tab);yield return View("manual");
        Require(ui.CurrentTab=="arrows" && Time.timeScale==0 && lab.Weapon.Rounds==rounds,"weapon field notes pause without firing");
        yield return Capture("weapon-notes");ui.Dispatch("select:1");ui.HandleKey(KeyCode.Alpha3);
        Require(lab.Weapon.Target==Bolt.Holy && lab.Weapon.Loaded==Bolt.Holy && ui.GetControl("select:1")==null,"retired quiver actions cannot equip another main arrow");
        Key(KeyCode.Escape);yield return View("battle");yield return Ready();
        for(int shot=0;shot<4;shot++) { lab.Shoot();yield return Ready(); }
        Require(lab.Weapon.Rounds==0,"four actual shots empty shared magazine");
        yield return Capture("empty");Key(KeyCode.R);yield return Frames();
        Require(lab.Weapon.Phase!=HunterCrossbow.Motion.Ready,"R starts real reload");
        yield return Capture("reloading");yield return Ready();

        report.fixtures.Add("Darkness: invoked existing AgeLamps(25), then relit an actual room source through existing ShootLamp. No UI-only state was assigned.");
        Invoke(lab,"AgeLamps",25f);yield return Frames();
        Require(lab.LitLamps==0 && Boss().Enraged,"fixture reaches real darkness and boss immunity");
        yield return Capture("dark");Key(KeyCode.Tab);yield return View("manual");Key(KeyCode.Escape);yield return View("battle");
        Require(lab.LitLamps==0 && Boss().Enraged,"weapon notes preserve dark encounter");
        Key(KeyCode.Escape);yield return View("pause");Click("settings");yield return View("settings");Click("back");yield return View("pause");Click("back");yield return View("battle");
        Require(lab.LitLamps==0 && Boss().Enraged,"settings round trip preserves dark encounter");
        Invoke(lab,"ShootLamp",FirstLamp());yield return Frames();
        Require(lab.LitLamps==1 && lab.BossAction==HuntBoss.Move.LightStagger,"real relight clears immunity and starts light stagger");
        yield return Capture("relit");

        Key(KeyCode.F2);Require(!lab.Developer.HealthLocked,"health lock can be disabled");
        report.fixtures.Add("Low-health/defeat: called existing HuntBoss.Hurt with controlled damage and existing ShadowLab.CheckEnd; health and result fields were not assigned.");
        DamageHunter(45);int hurtHealth=lab.HunterHealth;
        Require(hurtHealth>0 && hurtHealth<100,"controlled real damage creates injured hunter");
        Key(KeyCode.F2);DamageHunter(10);
        Require(lab.HunterHealth==hurtHealth,"F2 locks current health without healing");
        Key(KeyCode.F1);yield return Frames();
        Require(lab.Developer.Frozen && Time.timeScale==0,"F1 freezes actual simulation");
        position=camera.transform.position;rounds=lab.Weapon.Rounds;clock=Time.timeAsDouble;
        lab.Walk(Vector3.right,.1f);lab.Jump();lab.Shoot();
        yield return new WaitForSecondsRealtime(.2f);
        Require(camera.transform.position==position && lab.Weapon.Rounds==rounds && Time.timeAsDouble==clock,"frozen movement, shooting and clock remain blocked");
        yield return Capture("frozen",true);Key(KeyCode.F3);yield return Frames();
        Require(lab.Developer.HudHidden,"F3 hides UI while frozen");yield return Capture("hud-hidden");
        Key(KeyCode.Escape);yield return View("pause");
        Require(!lab.Developer.HudHidden,"Escape restores access to hidden UI");
        Click("settings");yield return View("settings");Click("back");yield return View("pause");Click("back");yield return View("battle");
        Require(lab.Developer.Frozen && Time.timeScale==0,"closing menus cannot release F1 freeze");
        Key(KeyCode.F5);yield return View("battle");
        Require(!lab.Developer.Frozen && lab.Developer.HealthLocked && lab.HunterHealth==100 &&
            lab.Weapon.Loaded==Bolt.Holy && lab.Weapon.Rounds==4 && Time.timeScale>0,"F5 resets live encounter, releases freeze and retains health lock");

        Key(KeyCode.F2);DamageHunter(999);yield return View("defeat");
        Require(lab.Finished && lab.HunterHealth==0 && ui.InputBlocked,"real lethal damage opens defeat");
        yield return Capture("defeat");Click("result-retry");yield return View("battle");
        Require(!lab.Finished && lab.HunterHealth==100 && lab.Weapon.Rounds==4 && string.IsNullOrEmpty(ui.ConfirmationKey),"defeat retry resets without redundant confirmation");
        report.fixtures.Add("Victory: called existing WallCombat.Shoot(Holy, hit=true) 12 times and existing CheckEnd. This is a controlled hit fixture, not an aim/accuracy or full Boss-play claim.");
        for(int hit=0;hit<12;hit++)Combat().Shoot(Bolt.Holy,Vector2.zero,true,true);
        Invoke(lab,"CheckEnd");yield return View("ending");
        Require(lab.Finished && lab.Damage>=HuntBoss.DemonHealth && lab.HunterHealth>0 && lab.VictoryPresentationActive,"real combat damage starts the continuous ending");
        yield return Capture("victory-fall");
        yield return Until(()=>lab.VictoryPhase==VictoryPresentationState.Stage.Story,"victory reaches the black-screen story",lab.VictoryStoryStart+5);
        yield return Capture("victory-story");
        yield return Until(()=>ui.CurrentView=="menu"&&!lab.VictoryPresentationActive,"ending reveals the interactive homepage",
            lab.VictoryPageDuration(0)+lab.VictoryPageDuration(1)+lab.VictoryMenuRevealSeconds+5);
        yield return Capture("victory-home");

        yield return LayoutMatrix();
        Screen.SetResolution(originalWidth,originalHeight,FullScreenMode.Windowed);
        ui.SetPreference("TextScale",1);yield return Frames(3);
        Require(unexpectedErrors.Count==0,"no unexpected player exceptions during UI acceptance");
    }

    IEnumerator BrightnessCheck() {
        var camera=lab.GetComponentInChildren<Camera>();
        var grade=camera.GetComponent<ShiyinCameraGrade>();
        Require(grade!=null && grade.enabled && grade.ShaderAvailable,"brightness shader is present and supported in the Windows player");
        var lights=FindObjectsByType<Light>(FindObjectsSortMode.None);
        var intensities=new float[lights.Length];for(int i=0;i<lights.Length;i++)intensities[i]=lights[i].intensity;
        Vector3 projection=lab.GameplayLight;
        var slider=ui.GetControl("setting:Brightness") as Slider;
        Require(slider!=null,"brightness uses a native Slider");
        slider.value=.7f;yield return Frames(3);
        Require(Mathf.Abs(grade.Brightness-.7f)<.001f,"brightness control updates the real camera image effect");
        yield return Capture("brightness-low");
        slider.value=1.3f;yield return Frames(3);yield return Capture("brightness-high");
        for(int i=0;i<lights.Length;i++)Require(Mathf.Approximately(intensities[i],lights[i].intensity),"brightness leaves physical Light intensity unchanged: "+lights[i].name);
        Require(lab.GameplayLight==projection,"brightness leaves gameplay shadow projection source unchanged");
        slider.value=1;yield return Frames();
    }
    IEnumerator DisplayRollbackCheck() {
        int previous=ui.Settings.DisplayMode;
        FullScreenMode previousActual=Screen.fullScreenMode;
        int width=Screen.width,height=Screen.height;
        Click("setting:DisplayMode");yield return Frames();
        Require(ui.ConfirmationKey=="display" && ui.Settings.DisplayMode!=previous,"display control previews a different mode and requests confirmation");
        yield return Until(()=>Screen.fullScreenMode==ui.Settings.RequestedDisplayMode,"display preview changes the actual player presentation",6);
        yield return Capture("confirm-display");Click("cancel");yield return Frames();
        Require(ui.Settings.DisplayMode==previous && string.IsNullOrEmpty(ui.ConfirmationKey),"cancel display preview restores preference");
        yield return Until(()=>Screen.fullScreenMode==previousActual && Screen.width==width && Screen.height==height,
            "cancel display preview restores actual window mode and resolution",6);
        Click("setting:DisplayMode");
        float began=Time.realtimeSinceStartup;double scaled=Time.timeAsDouble;
        yield return new WaitForSecondsRealtime(14f);
        Require(ui.ConfirmationKey=="display","unconfirmed display preview remains pending before 15 seconds");
        yield return Until(()=>string.IsNullOrEmpty(ui.ConfirmationKey),"display preview automatically rolls back after 15 real seconds",4);
        Require(Time.realtimeSinceStartup-began>=14.9f && Time.timeAsDouble==scaled,"display timeout uses real time while gameplay is paused");
        Require(ui.Settings.DisplayMode==previous,"display timeout restores previous preference");
        yield return Until(()=>Screen.fullScreenMode==previousActual && Screen.width==width && Screen.height==height,
            "display timeout restores actual window mode and resolution",6);
        var persisted=new ShiyinSettings(ui.Settings.PreferencePrefix);persisted.Load();
        Require(persisted.DisplayMode==previous,"rolled back display preference persists");
        Screen.SetResolution(width,height,previousActual);yield return Frames(4);
    }

    IEnumerator LayoutMatrix() {
        ui.SetPreference("Tutorial",0);
        var sizes=new[]{new Vector2Int(1920,1080),new Vector2Int(1440,1080),new Vector2Int(1280,720),new Vector2Int(2560,1080)};
        foreach(var size in sizes) {
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return Frames(8);
            if(Screen.width!=size.x || Screen.height!=size.y) {
                report.limitations.Add("OS/player did not provide requested layout "+size.x+"x"+size.y+"; actual "+Screen.width+"x"+Screen.height+". This resolution is not claimed verified.");
                continue;
            }
            foreach(float scale in new[]{1f,1.15f,1.3f}) {
                ui.SetPreference("TextScale",scale);yield return Frames();
                string tag=size.x+"x"+size.y+"-"+Mathf.RoundToInt(scale*100);
                CheckBounds("menu "+tag);
                Click("settings");yield return View("settings");CheckBounds("settings "+tag);
                if(scale==1.3f)yield return Capture("layout-settings-"+tag);
                Click("settings:controls");yield return Frames();CheckBounds("controls "+tag);Click("back");yield return View("menu");
                Click("manual");yield return View("manual");
                foreach(string tab in new[]{"combat","arrows","lamps"}) { Click("manual:"+tab);yield return Frames();CheckBounds("manual-"+tab+" "+tag); }
                Click("back");yield return View("menu");yield return StartBattle();
                CheckBounds("battle "+tag);if(scale==1.3f)yield return Capture("layout-battle-"+tag);
                Key(KeyCode.Tab);yield return View("manual");CheckBounds("weapon-notes "+tag);yield return Capture("layout-weapon-notes-"+tag);
                Key(KeyCode.Escape);yield return View("battle");Key(KeyCode.Escape);yield return View("pause");CheckBounds("pause "+tag);
                Click("retry");yield return Frames();CheckBounds("confirm "+tag);Click("cancel");yield return Frames();
                Click("menu");yield return Frames();Click("confirm");yield return View("menu");
            }
        }
    }
    void CheckBounds(string context) {
        Canvas.ForceUpdateCanvases();int count=0;
        var corners=new Vector3[4];
        foreach(var text in ui.UiCanvas.GetComponentsInChildren<Text>()) {
            if(!text.enabled || !text.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(text.text))continue;
            bool hidden=false;
            foreach(var group in text.GetComponentsInParent<CanvasGroup>())if(group.alpha<.01f)hidden=true;
            if(hidden)continue;
            // Scrolled content can intentionally extend outside its viewport. Validate its
            // viewport rather than reporting offscreen rows as clipped interface text.
            var mask=text.GetComponentInParent<RectMask2D>();
            var rect=mask!=null ? mask.rectTransform : text.rectTransform;
            rect.GetWorldCorners(corners);
            string label=text.transform.parent.name+" / "+text.text.Replace('\n',' ');
            foreach(var point in corners) {
                if(point.x < -3 || point.y < -3 || point.x>Screen.width+3 || point.y>Screen.height+3)
                    throw new InvalidOperationException(context+" outside screen: "+label+" at "+point);
            }
            if(!text.resizeTextForBestFit && text.horizontalOverflow==HorizontalWrapMode.Wrap && text.verticalOverflow==VerticalWrapMode.Truncate)
                if(text.preferredHeight>text.rectTransform.rect.height+4)
                    throw new InvalidOperationException(context+" truncated text: "+label);
            var local=text.rectTransform.rect;
            var vertices=text.cachedTextGenerator.verts;
            float units=1/text.pixelsPerUnit;
            for(int vertex=0;vertex<vertices.Count-4;vertex++) {
                var point=vertices[vertex].position*units;
                if(point.x<local.xMin-4 || point.x>local.xMax+4 || point.y<local.yMin-4 || point.y>local.yMax+4)
                    throw new InvalidOperationException(context+" text exceeds its assigned box: "+label);
            }
            count++;
        }
        Require(count>0,context+" verified "+count+" visible text boxes");
    }
}
