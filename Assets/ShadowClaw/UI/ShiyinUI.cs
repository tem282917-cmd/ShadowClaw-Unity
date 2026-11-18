using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Page navigation, pause ownership, settings and live encounter bindings.
// Views are built by ShiyinUI.Views; combat remains exclusively in ShadowLab.
public sealed partial class ShiyinUI : MonoBehaviour {
    public ShadowLab Lab {get;private set;}
    public ShiyinSettings Settings {get;private set;}
    public string CurrentView {get;private set;}="menu";
    public string CurrentTab {get;private set;}="combat";
    public int TutorialStep {get;private set;}
    public float LoadingProgress {get;private set;}
    public string LoadingLabel {get;private set;}="正在准备狩猎";
    public string ConfirmationKey {get;private set;}="";
    public string ConfirmTitle {get;private set;}="";
    public string ConfirmBody {get;private set;}="";
    public string ConfirmActionLabel {get;private set;}="";
    public bool InputBlocked => !initialized || CurrentView!="battle" || ConfirmationKey.Length>0 ||
        Lab.Developer.Paused || Lab.VictoryPresentationActive || Time.frameCount<=inputResumeFrame;
    public ShiyinHudSnapshot Snapshot {get;private set;}
    public bool AutomatedCheck {get;private set;}
    struct NavigationEntry {public string View,Tab,Focus;public Vector2 Scroll;public bool HasScroll;}
    readonly Stack<NavigationEntry> history=new Stack<NavigationEntry>();
    bool initialized,sessionStarted,legacyCheck;
    int inputResumeFrame,previousLit,previousMode;
    int previousDisplayWidth,previousDisplayHeight;
    FullScreenMode previousWindowMode;
    HuntBoss.Move previousAction;
    float noticeUntil,toastUntil,hitUntil,displayDeadline;
    string noticeId="",noticeTitle="",noticeBody="",toast="",confirmationFocus="";
    int noticePriority;
    ShiyinAudio audioFeedback;
    Coroutine loading;
    string EncounterName => Lab.EncounterDisplayName;
    string EncounterEnglish => Lab.EncounterDisplayNameEnglish;

    public void Initialize(ShadowLab lab) {
        Lab=lab;
        LoadingLabel="准备"+EncounterName;
        var args=Environment.GetCommandLineArgs();
        bool qa=Array.IndexOf(args,"-check-ui")>=0;
        AutomatedCheck=qa;
        foreach(var arg in args)if(arg.StartsWith("-check-",StringComparison.Ordinal)&&arg!="-check-ui")legacyCheck=true;
        Settings=new ShiyinSettings(qa ? "Shiyin.QA" : "Shiyin");
        if(qa)Settings.ResetDefaults();else Settings.Load();
        // Unity's launch arguments and the current window take precedence over saved display mode.
        Settings.Apply(Lab.Eye,!qa&&!legacyCheck);
        if(ShiyinSettings.HasCommandLineDisplayOverride())
            Settings.DisplayMode=Screen.fullScreenMode==FullScreenMode.Windowed?2:Screen.fullScreenMode==FullScreenMode.FullScreenWindow?1:0;
        audioFeedback=ShiyinAudio.Ensure(Lab.Eye);
        if(FindFirstObjectByType<EventSystem>()==null) {
            var events=new GameObject("Shiyin EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            events.transform.SetParent(transform,false);
        }
        BuildViews();
        initialized=true;
        Lab.ShotResolved+=OnShotResolved;
        previousLit=Lab.LitLamps;previousAction=Lab.BossAction;
        // The narration acceptance follows the same entry path as a new player.
        // Keep unattended focus handling but start at the normal title screen.
        sessionStarted=legacyCheck && Array.IndexOf(args,"-check-chapter01-narration")<0;
        bool opening=!sessionStarted && !qa && lab.gameObject.scene.name=="Chapter01_Main";
        SetView(sessionStarted ? "battle" : opening ? "prologue" : "menu");
        if(opening)BuildPrologue();
        RefreshLiveHud();
    }
    string FocusedAction() => EventSystem.current!=null && EventSystem.current.currentSelectedGameObject!=null
        ? EventSystem.current.currentSelectedGameObject.name : "";
    void SetView(string view,string focus=null) {
        CurrentView=view;
        ApplySessionState();
        ShowView(view);
        if(!string.IsNullOrEmpty(focus))FocusControl(focus);
        inputResumeFrame=Time.frameCount+1;
    }
    void ApplySessionState() {
        bool menu=CurrentView!="battle" || ConfirmationKey.Length>0;
        Lab.Developer.SetPauseReason("ui",menu);
        bool titleBackdrop=!sessionStarted || CurrentView=="menu" || CurrentView=="victory";
        Lab.SetMenuPresentation(titleBackdrop);
        audioFeedback?.MusicDirector?.SetTitlePage(CurrentView!="prologue" && (!sessionStarted || CurrentView=="menu"));
        Cursor.lockState=menu||legacyCheck ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible=CurrentView!="prologue" && (menu||legacyCheck);
        audioFeedback?.SetPaused(menu || Lab.Developer.Frozen);
        SetUiVisible(!Lab.Developer.HudHidden);
    }
    void Open(string page,string tab=null) {
        var scroll=UiCanvas.GetComponentInChildren<ScrollRect>();
        history.Push(new NavigationEntry{View=CurrentView,Tab=CurrentTab,Focus=FocusedAction(),HasScroll=scroll!=null,
            Scroll=scroll!=null?scroll.normalizedPosition:Vector2.up});
        if(tab!=null)CurrentTab=tab;
        Lab.Developer.SetHudHidden(false);
        SetView(page);
    }
    void Back() {
        if(ConfirmationKey.Length>0){CancelConfirmation();return;}
        if(history.Count>0) {
            var origin=history.Pop();CurrentTab=origin.Tab;
            SetView(origin.View,origin.Focus);
            if(origin.HasScroll) {
                Canvas.ForceUpdateCanvases();
                var scroll=UiCanvas.GetComponentInChildren<ScrollRect>();
                if(scroll!=null)scroll.normalizedPosition=origin.Scroll;
            }
        } else SetView(sessionStarted ? "battle" : "menu");
    }
    void ResetNotices() {
        noticeId=noticeTitle=noticeBody=toast="";
        noticeUntil=toastUntil=hitUntil=0;noticePriority=0;
        previousLit=Lab.LitLamps;previousAction=Lab.BossAction;
    }
    internal void ShowGateRetry() {
        ResetNotices();relicFeedback="";
        toast="已在雾门外苏醒 · 穿过白雾再次挑战";toastUntil=Time.time+3;
    }
    void StartNewHunt() {
        if(loading!=null)return;
        ConfirmationKey="";history.Clear();sessionStarted=true;
        Lab.Developer.SetHudHidden(false);
        LoadingProgress=0;LoadingLabel="准备"+EncounterName;
        SetView("loading");
        loading=StartCoroutine(LoadEncounter());
    }
    IEnumerator LoadEncounter() {
        // Display actual preparation phases. No simulated percentage or artificial wait.
        LoadingProgress=0;LoadingLabel="准备"+EncounterName;RebuildCurrentView();
        yield return null;
        LoadingProgress=.35f;LoadingLabel="复位灯火与武器";
        Lab.RestartEncounter();ResetNotices();
        LoadingProgress=1;LoadingLabel="狩猎准备完成";RebuildCurrentView();
        yield return null;
        TutorialStep=0;loading=null;
        SetView(Settings.Tutorial ? "onboarding" : "battle");
    }
    void RestartNow() {
        if(loading!=null){StopCoroutine(loading);loading=null;}
        ConfirmationKey="";history.Clear();sessionStarted=true;
        Lab.RestartEncounter();ResetNotices();
        SetView("battle");
    }
    void ReturnToMenu() {
        if(loading!=null){StopCoroutine(loading);loading=null;}
        Lab.RestoreExterior();
        Lab.CancelVictoryPresentation();
        Lab.CancelHolyKnifeThrow(false);
        audioFeedback?.ResetCombatEffects();
        history.Clear();ConfirmationKey="";sessionStarted=false;
        Lab.Developer.SetFrozen(false);Lab.Developer.SetHudHidden(false);
        ResetNotices();SetView("menu");
    }
    void RequestConfirmation(string key) {
        confirmationFocus=FocusedAction();ConfirmationKey=key;
        switch(key) {
            case "retry":ConfirmTitle="重新挑战？";ConfirmBody="本次狩猎将从起点重新开始。生命与弹匣恢复初始状态，锁血开关会保留。";ConfirmActionLabel="重新挑战";break;
            case "menu":ConfirmTitle="返回主菜单？";ConfirmBody=$"本次狩猎将结束。再次进入{EncounterName}时会开始新的挑战。";ConfirmActionLabel="返回主菜单";break;
            case "exit":ConfirmTitle=$"离开{EncounterName}？";ConfirmBody="离开后，本次狩猎将结束。";ConfirmActionLabel="离开游戏";break;
            case "defaults":ConfirmTitle="恢复默认设置？";ConfirmBody="界面、声音、画面与操作偏好将恢复初始值。";ConfirmActionLabel="恢复默认";break;
            case "display":ConfirmTitle="保留显示设置？";ConfirmBody="若 15 秒内没有确认，将自动恢复原来的显示模式。";ConfirmActionLabel="保留设置";displayDeadline=Time.realtimeSinceStartup+15;break;
        }
        ApplySessionState();RebuildCurrentView();FocusControl("cancel");
    }
    void CancelConfirmation() {
        if(ConfirmationKey=="display") {
            Settings.DisplayMode=previousMode;Settings.Apply(Lab.Eye,false);
            if(!Application.isEditor)Screen.SetResolution(previousDisplayWidth,previousDisplayHeight,previousWindowMode);
            Settings.Save();
        }
        ConfirmationKey="";ApplySessionState();RebuildCurrentView();FocusControl(confirmationFocus);
    }
    void ConfirmAction() {
        string key=ConfirmationKey;ConfirmationKey="";
        switch(key) {
            case "retry":RestartNow();break;
            case "menu":ReturnToMenu();break;
            case "exit":Settings.Save();Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying=false;
#endif
                break;
            case "defaults":Settings.ResetDefaults();Settings.Save();Settings.Apply(Lab.Eye,true);ApplySessionState();RebuildCurrentView();FocusControl("defaults");break;
            case "display":Settings.Save();ApplySessionState();RebuildCurrentView();FocusControl("setting:DisplayMode");break;
        }
    }
    public void Dispatch(string action) {
        if(CurrentView=="prologue")return;
        if(!initialized)return;
        if(Lab.VictoryPresentationActive)return;
        audioFeedback?.PlayUiConfirm();
        if(action.StartsWith("select:",StringComparison.Ordinal)) {
            // Historical quiver actions cannot equip removed arrow types.
            return;
        }
        if(action.StartsWith("manual:",StringComparison.Ordinal)){CurrentTab=action.Substring(7);RebuildCurrentView();FocusControl(action);return;}
        if(action.StartsWith("settings:",StringComparison.Ordinal)){CurrentTab=action.Substring(9);RebuildCurrentView();FocusControl(action);return;}
        if(action.StartsWith("dev:",StringComparison.Ordinal)) {
            string feature=action.Substring(4);
            if(feature=="restart")RestartNow();
            else {HandleKey(feature=="freeze"?KeyCode.F1:feature=="lock"?KeyCode.F2:KeyCode.F3);if(CurrentView=="developer")RebuildCurrentView();}
            return;
        }
        switch(action) {
            case "start":StartNewHunt();break;
            case "back":case "cancel":Back();break;
            case "retry":RequestConfirmation("retry");break;
            case "menu":RequestConfirmation("menu");break;
            case "exit":RequestConfirmation("exit");break;
            case "confirm":ConfirmAction();break;
            case "manual":Open("manual","combat");break;
            case "settings":Open("settings","interface");break;
            case "developer":Open("developer");break;
            case "defaults":RequestConfirmation("defaults");break;
            case "tutorial":TutorialStep=0;Open("onboarding");break;
            case "next-tutorial":if(TutorialStep<2){TutorialStep++;RebuildCurrentView();FocusControl("next-tutorial");}else FinishTutorial();break;
            case "skip-tutorial":FinishTutorial();break;
            case "result-retry":RestartNow();break;
            case "result-menu":ReturnToMenu();break;
        }
    }
    void FinishTutorial() {if(history.Count>0)Back();else SetView("battle");}
    public void SetPreference(string key,float value) {
        if(!initialized)return;
        bool rebuild=false;
        switch(key) {
            case "TextScale":Settings.TextScale=value;rebuild=true;break;
            case "Contrast":Settings.Contrast=value>.5f;break;
            case "ReducedMotion":Settings.ReducedMotion=value>.5f;break;
            case "Tutorial":Settings.Tutorial=value>.5f;break;
            case "Master":Settings.Master=value;break;
            case "Effects":Settings.Effects=value;break;
            case "Music":Settings.Music=value;break;
            case "Narration":Settings.Narration=value;break;
            case "NarrationSubtitles":Settings.NarrationSubtitles=value>.5f;break;
            case "Brightness":Settings.Brightness=value;break;
            case "DisplayMode":
                previousMode=Settings.DisplayMode;
                if(previousMode==Mathf.RoundToInt(value))return;
                previousWindowMode=Screen.fullScreenMode;previousDisplayWidth=Screen.width;previousDisplayHeight=Screen.height;
                Settings.DisplayMode=Mathf.RoundToInt(value);Settings.Apply(Lab.Eye,true);RequestConfirmation("display");return;
            case "Fov":Settings.Fov=value;break;
            case "Sensitivity":Settings.Sensitivity=value;break;
            case "InvertY":Settings.InvertY=value>.5f;break;
            default:return;
        }
        Settings.Save();Settings.Apply(Lab.Eye,false);
        if(rebuild){RebuildCurrentView();FocusControl("setting:"+key);}
        RefreshLiveHud();
    }
    public bool HandleKey(KeyCode key) {
        if(!initialized)return false;
        if(key==KeyCode.F5){RestartNow();return true;}
        if(Lab.VictoryPresentationActive)return true;
        if(key==KeyCode.F1||key==KeyCode.F2||key==KeyCode.F3) {
            Lab.Developer.HandleKey(key);
            ApplySessionState();RefreshLiveHud();return true;
        }
        if(key==KeyCode.Escape) {
            if(Lab.Developer.HudHidden) {
                Lab.Developer.SetHudHidden(false);
                if(CurrentView=="battle")Open("pause");else {ApplySessionState();RebuildCurrentView();}
                return true;
            }
            if(ConfirmationKey.Length>0){CancelConfirmation();return true;}
            if(CurrentView=="battle")Open("pause");
            else if(CurrentView=="onboarding")FinishTutorial();
            else if(CurrentView!="menu"&&CurrentView!="loading"&&CurrentView!="defeat"&&CurrentView!="victory")Back();
            return true;
        }
        if(Lab.Developer.HudHidden&&CurrentView!="battle")return true;
        if(ConfirmationKey.Length>0)return true;
        if(key==KeyCode.Tab) {
            if(CurrentView=="battle")Open("manual","arrows");
            else if(CurrentView=="arrows")Back();
            else MoveFocus(Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift));
            return true;
        }
        if(key>=KeyCode.Alpha1&&key<=KeyCode.Alpha4&&CurrentView=="arrows") {
            Dispatch("select:"+((int)key-(int)KeyCode.Alpha1));return true;
        }
        if(key==KeyCode.R&&(CurrentView=="defeat"||CurrentView=="victory")){RestartNow();return true;}
        if(CurrentView=="battle"&&key==KeyCode.R) {
            if(!InputBlocked) {
                Lab.Weapon.Request(Bolt.Holy);
                RefreshLiveHud();
            }
            return true;
        }
        return false;
    }
    void MoveFocus(bool reverse) {
        var selected=EventSystem.current!=null ? EventSystem.current.currentSelectedGameObject : null;
        var current=selected!=null ? selected.GetComponent<Selectable>() : null;
        var next=current!=null ? (reverse?current.FindSelectableOnUp():current.FindSelectableOnDown()) : null;
        if(next==null) {
            foreach(var selectable in UiCanvas.GetComponentsInChildren<Selectable>()) {
                if(!selectable.IsActive()||!selectable.IsInteractable())continue;
                next=selectable;if(!reverse)break;
            }
        }
        if(next!=null)next.Select();
    }
    public bool PollInput() {
        if(CurrentView=="prologue")return true;
        if(legacyCheck)return false;
        if(AutomatedCheck)return InputBlocked;
        foreach(var key in new[]{KeyCode.F1,KeyCode.F2,KeyCode.F3,KeyCode.F5,KeyCode.Escape,KeyCode.Tab})
            if(Input.GetKeyDown(key)){HandleKey(key);return true;}
        if(CurrentView=="arrows")for(int i=0;i<4;i++)if(Input.GetKeyDown(KeyCode.Alpha1+i)){Dispatch("select:"+i);return true;}
        if((CurrentView=="defeat"||CurrentView=="victory")&&Input.GetKeyDown(KeyCode.R)){RestartNow();return true;}
        if(CurrentView=="battle") {
            if(Input.GetKeyDown(KeyCode.R)){HandleKey(KeyCode.R);return true;}
        }
        if(InputBlocked)return true;
        if(Cursor.lockState!=CursorLockMode.Locked&&!legacyCheck){Open("pause");return true;}
        return false;
    }
    void OnApplicationFocus(bool focused) {
        if(!initialized||legacyCheck||AutomatedCheck)return;
        if(!focused&&CurrentView=="battle")Open("pause");
    }
    void LateUpdate() {
        if(!initialized)return;
        if(CurrentView=="prologue"){AdvancePrologue(Time.unscaledDeltaTime);return;}
        RefreshVictorySequence();
        if(Lab.VictoryPresentationActive)return;
        if(legacyCheck){RefreshLiveHud();return;}
        if(ConfirmationKey=="display"&&Time.realtimeSinceStartup>=displayDeadline)CancelConfirmation();
        if(sessionStarted&&Lab.Finished&&Lab.HunterHealth==0&&CurrentView!="defeat"&&CurrentView!="loading") {
            history.Clear();ConfirmationKey="";noticeId=noticeTitle=noticeBody=toast="";
            Lab.Developer.SetHudHidden(false);
            SetView("defeat");
        }
        if(sessionStarted&&!Lab.Finished&&Lab.EncounterStarted) {
            if(previousLit>0&&Lab.LitLamps==0)Notice("dark","暗猎开始","射亮任意壁灯，打断追击",4,100);
            if(previousLit==0&&Lab.LitLamps>0)Notice("relit","灯火复明","敌影重新显形 · 抓住反击时机",2,90);
            if(previousAction!=HuntBoss.Move.Ritual&&Lab.BossAction==HuntBoss.Move.Ritual)
                Notice("ritual","影魔正在熄灯","射中正在熄灭的灯芯，打断仪式",2,70);
            if(noticeId=="ritual"&&Lab.BossAction!=HuntBoss.Move.Ritual)noticeUntil=0;
        }
        previousLit=Lab.LitLamps;previousAction=Lab.BossAction;
        RefreshLiveHud();
    }
    void Notice(string id,string title,string body,float seconds,int priority) {
        if(Time.time<noticeUntil&&priority<noticePriority)return;
        noticeId=id;noticeTitle=title;noticeBody=body;noticeUntil=Time.time+seconds;noticePriority=priority;
    }
    void OnShotResolved(string feedback) {
        if(TryShowAcolyteProtectionFeedback(feedback))return;
        if(feedback.Contains("HIT")||feedback=="SHADOW BURNING"||feedback=="射影命中")hitUntil=Time.time+.18f;
        if(feedback=="HOLY HIT  +10"||feedback=="射影命中")toast="命中敌影";
        else if(feedback=="灯火已复明")toast=feedback;
        else if(feedback=="SHADOW BURNING" || feedback.Contains("地影燃烧"))toast="敌影燃烧";
        else if(feedback=="FIRE PLACED")toast="火焰已落下";
        else if(feedback=="STORM ANCHOR PLACED")toast="雷电锚点已落下";
        else if(feedback=="BODY BLOCKED")toast="本体挡住了弩箭";
        else if(feedback.Contains("已守住"))toast="熄灯仪式已打断";
        else if(feedback.Contains("已加固"))toast="灯火已加固";
        else if(feedback.Contains("已点燃"))toast="灯火重新点燃";
        else if(feedback.Contains("暗猎已打断"))toast="暗猎已打断";
        else toast="";
        toastUntil=Time.time+1.4f;
    }
    internal void ShowRelicFeedback(string message) {
        relicFeedback=message;relicFeedbackUntil=Time.time+3.5f;
    }
    string relicFeedback;
    float relicFeedbackUntil;
    void RefreshLiveHud() {
        if(!initialized||Lab.Weapon==null)return;
        var w=Lab.Weapon;
        var guardian=Lab.ActiveGuardian;
        string bossStatus="",bossIcon="";
        if(Lab.EncounterStarted) {
            if(Lab.LitLamps==0){bossStatus="暗猎 · 免疫伤害";bossIcon="shield";}
            else if(Lab.StunRemaining>0){bossStatus="麻痹  "+Lab.StunRemaining.ToString("F1")+"s";bossIcon="storm";}
            else if(Lab.BossAction==HuntBoss.Move.LightStagger){bossStatus="畏光硬直  "+Lab.BossActionRemaining.ToString("F1")+"s";bossIcon="light";}
            else bossStatus=Lab.CurrentBossTell;
        }
        if(guardian!=null) { bossStatus=guardian.Tell;bossIcon=guardian.LitLinkedLamps==0?"shield":""; }
        string[] names={"圣水","圣水","圣水","圣水"};
        string weaponLabel="R 装填    E 圣火飞刀";
        float progress=0;
        bool busy=w.Phase!=HunterCrossbow.Motion.Ready;
        switch(w.Phase) {
            case HunterCrossbow.Motion.Cycle:weaponLabel="上弦中";progress=w.PhaseProgress;break;
            case HunterCrossbow.Motion.Lower:weaponLabel="收弩 · "+names[(int)w.Target]+"箭";progress=w.PhaseProgress*.16f;break;
            case HunterCrossbow.Motion.Load:weaponLabel="装入 "+names[(int)w.Target]+"箭";progress=.16f+w.PhaseProgress*.63f;break;
            case HunterCrossbow.Motion.Raise:weaponLabel="举弩中";progress=.79f+w.PhaseProgress*.21f;break;
            default:if(w.Rounds==0)weaponLabel="空匣 · R 装填";break;
        }
        var snapshot=new ShiyinHudSnapshot {
            Health=Lab.HunterHealth,MaxHealth=100,BossHealth01=guardian!=null ? guardian.HealthFraction : Mathf.Clamp01(1-Lab.Damage/HuntBoss.DemonHealth),EncounterStarted=Lab.EncounterStarted || guardian!=null,
            BossName=guardian!=null ? "守灯者 · 锁灯巨像" : "钟袖影魔 · 古堡之主",
            BossStatus=bossStatus,BossStatusIcon=bossIcon,Loaded=w.Loaded,Target=w.Target,Queued=w.Queued,Rounds=w.Rounds,
            WeaponLabel=weaponLabel,WeaponBusy=busy,WeaponProgress=progress,
            HolyKnives=Lab.HolyKnifeCount,HolyKnifeCapacity=Lab.HolyKnifeCapacity,LitLamps=guardian!=null?guardian.LitLinkedLamps:Lab.LitLamps,LampCapacity=guardian!=null?0:HuntBoss.LampCapacity,
            HasGuardianRelic=Lab.HasGuardianRelic,GuardianRelicCharges=Lab.GuardianRelicCharges,GuardianRelicRelights=Lab.GuardianRelicRelights,
            Condition=Lab.PinRemaining>0?"定身":Lab.SlowRemaining>0 || Lab.ContactSlowed?"减速":Lab.Sprinting?(Lab.CombatActive?"疾跑 · 消耗体力":"疾跑 · 无限"):"",
            ConditionTime=Lab.PinRemaining>0?Lab.PinRemaining:Lab.SlowRemaining,
            NoticeTitle=Lab.EncounterStarted&&Time.time<noticeUntil?noticeTitle:"",NoticeBody=Lab.EncounterStarted&&Time.time<noticeUntil?noticeBody:"",
            CombatGuides=Lab.ActiveCombatGuides,
            DangerNotice=noticePriority>=85,Toast=(!Lab.HasGuardianRelic || Lab.GuardianRelicCharges==0 ? Lab.FirstBossWoundHint : null) ??
                (Time.time<relicFeedbackUntil && (Lab.HasGuardianRelic || Lab.GuardianRelicDrop!=null)
                    ? relicFeedback : Time.time<toastUntil?toast:""),Hit=!Settings.ReducedMotion&&Time.time<hitUntil,
            Recovering=Lab.Recovering,Sprinting=Lab.Sprinting,CombatActive=Lab.CombatActive,
            SprintStamina01=Lab.SprintStamina01,SprintExhausted=Lab.SprintExhausted,
            RecoveryStatus=Lab.HunterHealth>0&&Lab.HunterHealth<100&&!Lab.Developer.HealthLocked&&!Lab.Finished ?
                (Lab.Developer.Paused ? "回血已暂停" : Lab.Recovering ? "呼吸回血中" : "距回血 "+Lab.RecoveryRemaining.ToString("0.0")+"s") : "",
            LowHealth=Lab.HunterHealth<=25,Frozen=Lab.Developer.Frozen,HealthLocked=Lab.Developer.HealthLocked,Hidden=Lab.Developer.HudHidden
        };
        Snapshot=snapshot;RefreshHud(snapshot);SetUiVisible(!snapshot.Hidden);
        audioFeedback?.SetPaused(Lab.Developer.Paused);
    }
    void OnDestroy() {
        if(Lab!=null){Lab.ShotResolved-=OnShotResolved;Lab.Developer.SetPauseReason("ui",false);}
    }
}
