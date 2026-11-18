using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using W = ShiyinWidgets;

// The native presentation layer. Gameplay and navigation remain in ShiyinUI.cs.
public sealed partial class ShiyinUI : MonoBehaviour
{
    public Canvas UiCanvas { get; private set; }
    RectTransform vRoot,vHud,vPage,vModal;
    readonly Dictionary<string,Selectable> vControls=new Dictionary<string,Selectable>();
    readonly List<KeyValuePair<Text,int>> vFonts=new List<KeyValuePair<Text,int>>();
    ShiyinHudSnapshot vSnapshot;
    Text vHealth,vAmmo,vArrowName,vWeaponLabel,vQueued,vBossName,vBossStatus,vCondition,vConditionTime,vNoticeTitle,vNoticeBody,vToast,vDev,vLowHealth;
    Text vStaminaValue,vStaminaState,vKnifeCount,vObjectiveTitle,vObjectiveBody,vObjectiveLamps;
    Text vCombatGuide;
    CombatGuidance displayedGuides;
    GameObject vRelic;
    Text vRelicCount,vRelicRecharge;
    RectTransform vHealthFill,vBossFill,vWeaponFill,vStaminaFill;
    Image vArrowIcon,vBossIcon,vHealthBack,vAmmoBack,vNoticeBack,vConditionIcon,vStaminaBack,vStaminaColor,vToastBack,vConditionBack,vKnifeBack;
    GameObject vConditionObject,vNoticeObject,vWeaponProgress,vToastObject,vHitObject,vBossObject,vExplorationObjective,vStaminaObject;
    Image[] vBolts,vKnifeMarks;
    float vAppliedScale=-1;
    bool vAppliedReducedMotion;
    static readonly Color[] vArrowColors={W.Hex("CBD6CB")};
    float VScale => Settings!=null?Mathf.Clamp(Settings.TextScale,1,1.3f):1;
    float VReferenceWidth => Screen.height>0?Screen.width*1080f/Screen.height:1920;
    int VSize(int size)=>Mathf.RoundToInt(size*VScale);
    Text VT(Transform p,string text,float x,float y,float w,float h,int size=20,Color? color=null,bool serif=false,TextAnchor alignment=TextAnchor.UpperLeft) {
        var t=W.Text(p,text,x,y,w,h,VSize(size),color??W.Ink,serif,alignment);vFonts.Add(new KeyValuePair<Text,int>(t,size));return t;
    }
    Button VB(Transform p,string action,string label,float x,float y,float w,float h=64,bool primary=false,bool danger=false,bool quiet=false,int size=20) {
        var b=W.Button(p,action,label,x,y,w,h,()=>Dispatch(action),VSize(size),primary,danger,quiet);vControls[action]=b;return b;
    }
    void VRule(Transform p,float x,float y,float width,float alpha=.25f)=>W.Fill(p,"Rule",x,y,width,1,W.Alpha(W.Silver,alpha));
    void VClear(ref RectTransform target) {if(target==null)return;target.gameObject.SetActive(false);Destroy(target.gameObject);target=null;}
    public void BuildViews() {
        if(UiCanvas!=null)return;
        vRoot=new GameObject("Shiyin UI",typeof(RectTransform)).GetComponent<RectTransform>();vRoot.SetParent(transform,false);
        UiCanvas=vRoot.gameObject.AddComponent<Canvas>();UiCanvas.renderMode=RenderMode.ScreenSpaceOverlay;UiCanvas.sortingOrder=200;
        var scaler=vRoot.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;scaler.matchWidthOrHeight=1f;
        vRoot.gameObject.AddComponent<GraphicRaycaster>();
        vHud=W.Stretch(vRoot,"Battle HUD");VBuildHud();vHud.gameObject.SetActive(false);
    }
    public void ShowView(string id) {
        BuildViews();vControls.Clear();VClear(ref vModal);VClear(ref vPage);
        vPage=W.Stretch(vRoot,"View "+id);
        // Translucent menu pages should not double-expose HUD text underneath them.
        vHud.gameObject.SetActive(id=="battle");
        switch(id) {
            case "prologue":
                VMenu();
                var group=vPage.gameObject.AddComponent<CanvasGroup>();
                group.interactable=false;group.blocksRaycasts=false;
                break;
            case "menu":VMenu();break;
            case "loading":VLoading();break;
            case "onboarding":VOnboarding();break;
            case "arrows":VArrows();break;
            case "pause":VPause();break;
            case "manual":VManual();break;
            case "settings":VSettings();break;
            case "developer":VDeveloper();break;
            case "defeat":VResult(false);break;
            case "victory":VResult(true);break;
        }
        if(!string.IsNullOrEmpty(ConfirmationKey))VConfirmation();
        VApplyFonts();VApplyMotion();RefreshHud(vSnapshot);
        if(!string.IsNullOrEmpty(ConfirmationKey))FocusControl("cancel");
        else {
            string focus=id=="menu"?"start":id=="onboarding"?"next-tutorial":id=="victory"||id=="defeat"?"result-retry":"back";
            FocusControl(focus);
        }
    }
    public void RebuildCurrentView()=>ShowView(CurrentView);
    public void SetUiVisible(bool visible){if(UiCanvas!=null)UiCanvas.enabled=visible;}
    public Selectable GetControl(string action) {
        return vControls.TryGetValue(action,out var c)&&c!=null&&c.gameObject.activeInHierarchy?c:null;
    }
    public void FocusControl(string action) {
        var c=GetControl(action);if(c!=null&&c.IsInteractable()&&EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(c.gameObject);
    }
    void VApplyFonts() {
        vFonts.RemoveAll(pair=>pair.Key==null);
        foreach(var pair in vFonts)pair.Key.fontSize=VSize(pair.Value);
        vAppliedScale=VScale;
    }
    void VApplyMotion() {
        if(UiCanvas==null)return;
        bool reduced=Settings!=null&&Settings.ReducedMotion;
        foreach(var selectable in UiCanvas.GetComponentsInChildren<Selectable>(true)) {
            var colors=selectable.colors;colors.fadeDuration=reduced?0f:.12f;selectable.colors=colors;
            if(selectable is Toggle toggle)toggle.toggleTransition=reduced?Toggle.ToggleTransition.None:Toggle.ToggleTransition.Fade;
        }
        vAppliedReducedMotion=reduced;
    }
    void VDim(float alpha=.38f) {W.Image(W.Stretch(vPage,"Scene scrim"),W.Hex("080C0E",alpha),true);}
    RectTransform VPanel(string name,float width=1456,float height=880,float x=0,float y=0) {
        var panel=W.Center(vPage,name,width,height,x,y);W.Image(panel,W.Alpha(W.Surface,.80f),true);
        VRule(panel,0,0,width,.55f);VRule(panel,0,height-1,width,.25f);W.Corners(panel,W.Alpha(W.Silver,.65f));
        float fit=Mathf.Min(1,(VReferenceWidth-80)/width);panel.localScale=Vector3.one*Mathf.Max(.5f,fit);return panel;
    }
    void VHeading(RectTransform p,string eyebrow,string title,float width=1456,bool back=true) {
        VT(p,eyebrow,48,35,width-300,25,16,W.Silver);VT(p,title,48,65,width-300,76,44,null,true);
        if(back)VB(p,"back","返回  Esc",width-214,48,166,58,false,false,true,18);
        VRule(p,0,160,width);
    }
    void VFooter(RectTransform p,string text,float width=1456,float height=880) {
        VRule(p,0,height-98,width);VT(p,text,48,height-75,width-96,62,16,W.Muted);
    }
    void VMenu() {
        W.Image(W.Stretch(vPage,"Menu tone"),W.Hex("080C0E",.28f),true);
        var left=W.Anchor(vPage,"Menu column",new Vector2(0,1),new Vector2(0,1),132,-75,630,960);
        W.Icon(left,"emblem",3,6,76,76,W.Silver);VRule(left,104,46,336,.35f);W.Diamond(left,"Menu covenant terminal",449,43,6,W.Silver);
        VT(left,Application.productName,0,148,880,160,64,null,true,TextAnchor.MiddleLeft);
        VT(left,EncounterName,0,320,620,36,24,W.Silver,true);
        VT(left,EncounterEnglish,0,360,620,35,18,W.Silver);
        VT(left,$"灯火将尽，影子醒来。\n踏入{EncounterName}，狩猎暗处之物。",0,414,590,100,20,W.Muted);
        VB(left,"start","开始狩猎    Enter",0,535,440,76,true,size:24);
        VB(left,"manual","猎人手册",0,619,440,72,quiet:true,size:24);
        VB(left,"settings","设置",0,699,440,72,quiet:true,size:24);
        VB(left,"exit","离开游戏",0,779,440,72,quiet:true,size:24);
        VRule(left,0,877,440);VT(left,EncounterName+" · 战斗演示",0,898,500,40,16,W.Muted);
        var right=W.Anchor(vPage,"Menu signature",new Vector2(1,0),new Vector2(1,0),-72,48,370,36);
        VT(right,"中文 / 键盘与鼠标",0,0,370,36,16,W.Muted,false,TextAnchor.MiddleRight);
    }
    void VLoading() {
        VDim(.78f);var group=W.Center(vPage,"Loading",740,460,0,40);
        W.Icon(group,"emblem",312,0,116,116,W.Silver);VT(group,EncounterEnglish,0,156,740,30,18,W.Silver,false,TextAnchor.MiddleCenter);
        VT(group,"步入"+EncounterName,0,215,740,75,44,null,true,TextAnchor.MiddleCenter);
        W.Fill(group,"Track",160,327,420,3,W.Alpha(W.Silver,.2f));W.Fill(group,"Progress",160,327,420*Mathf.Clamp01(LoadingProgress),3,W.Silver);
        VT(group,string.IsNullOrEmpty(LoadingLabel)?"正在准备狩猎":LoadingLabel,0,354,740,55,18,W.Muted,false,TextAnchor.MiddleCenter);
        var tip=W.Anchor(vPage,"Loading hint",new Vector2(.5f,0),new Vector2(.5f,0),0,80,960,110);
        VT(tip,"灯火微弱时，仍能照出敌影。\n看见火焰收小，就为下一箭寻找灯芯。",0,0,960,110,20,W.Muted,false,TextAnchor.MiddleCenter);
    }
    void VOnboarding() {
        var p=VPanel("First hunt",1310,430,0,-225);int step=Mathf.Clamp(TutorialStep,0,2);
        string[] icons={"crossbow","fire","bolt"},titles={"敌影，才是猎物","让圣火留在灯芯","圣水弩与圣火飞刀"};
        string[] body={"射击地面的怪物影子可以伤害影魔。它的攻击必须命中你的身体才会生效；移出地面预警圈或斩线来躲避。","灯完全熄灭后只剩紫焰。用圣水箭或圣火飞刀重新点亮灯芯，可获得一把飞刀，最多五把；射击已经亮着的灯不会增加飞刀。","左键发射圣水箭，R 装填；E 投掷圣火飞刀，点燃敌影或灯芯。Shift 疾跑：战斗消耗体力，脱战无限。5 秒未受伤后自动回血。"};
        if(!Lab.EncounterStarted) {
            titles[0]="先沿山路进入古堡";
            body[0]="WASD 移动，按住 Shift 疾跑，空格跳跃。脱战疾跑无限；进入战斗后留意左侧体力条。沿山路和阶梯进入王座厅，射击地面的敌影造成伤害。";
        }
        W.Icon(p,icons[step],44,82,80,90,W.Silver);VT(p,"猎人须知 · "+(step+1)+" / 3",158,37,500,36,16,W.Muted);
        VT(p,titles[step],158,90,1080,70,34,null,true);VT(p,body[step],158,177,1060,112,20,W.Muted);
        for(int i=0;i<3;i++){var d=W.Fill(p,"Tutorial step "+i,161+i*25,342,7,7,i==step?W.Silver:W.Alpha(W.Silver,.2f));d.rectTransform.localRotation=Quaternion.Euler(0,0,45);}
        VB(p,"skip-tutorial","跳过",806,322,156,64,quiet:true);VB(p,"next-tutorial",step==2?(Lab.EncounterStarted?"开始狩猎":"开始探索"):"下一步  Enter",980,322,274,64,true);
    }
    void VArrows() { CurrentTab="arrows";VManual(); }
    void VPause() {
        VDim(.28f);var p=VPanel("Pause",640,900,(640-VReferenceWidth)*.5f+64);VT(p,"IRON CROWN HALL",48,44,540,25,16,W.Silver);
        VT(p,"狩猎暂歇",48,91,540,86,44,null,true);VT(p,Lab.EncounterStarted?"战斗已暂停":"探索已暂停",48,187,540,40,18,W.Muted);W.Icon(p,"divider",48,248,360,20,W.Silver);
        string[] actions={"back","retry","manual","settings","developer","menu"},labels={"继续狩猎    Esc","重新挑战","猎人手册","设置","开发者工具","返回主菜单"};
        for(int i=0;i<actions.Length;i++)VB(p,actions[i],labels[i],48,293+i*80,544,72,i==0,quiet:i>0);
        VT(p,(vSnapshot.Frozen?"F1 冻结仍开启    ":"")+(vSnapshot.HealthLocked?"F2 血量已锁定":""),48,815,544,54,16,W.Muted);
        var context=W.Anchor(vPage,"Pause context",new Vector2(1,0),new Vector2(1,0),-120,155,535,240);
        VT(context,"盯紧灯火，\n及时移出攻击预警。",0,0,535,130,32,W.Ink,true,TextAnchor.MiddleRight);
        VT(context,"暖火照出敌影，紫焰表示灯已熄灭。",0,175,535,60,18,W.Muted,false,TextAnchor.MiddleRight);
    }
    RectTransform VTabbedBody(RectTransform panel,string kind,string[] ids,string[] labels) {
        W.Fill(panel,"Tab separator",260,185,1,565,W.Alpha(W.Silver,.2f));
        for(int i=0;i<ids.Length;i++)VB(panel,kind+":"+ids[i],labels[i],24,192+i*78,212,66,CurrentTab==ids[i],quiet:CurrentTab!=ids[i],size:20);
        return W.Rect(panel,"Section content",296,191,1112,564);
    }
    void VManual() {
        VDim();var p=VPanel("Hunter manual");VHeading(p,"FIELD NOTES","猎人手册");
        var body=VTabbedBody(p,"manual",new[]{"combat","arrows","lamps"},new[]{"影子与战斗","弩与飞刀","灯火与暗猎"});
        float scale=VScale;var c=W.Scroll(body,"Manual scroll",0,0,1112,564,CurrentTab=="lamps"?760*scale:CurrentTab=="arrows"?740*scale:820*scale);
        if(CurrentTab=="arrows") {
            VT(c,"一把圣水弩，几枚留住火光的飞刀。",0,0,1055,58,22);
            VManualItem(c,"holy","左键 · 圣水箭","每匣四发。射中可见敌影造成直接伤害；按 R 装满弹匣，空匣再次射击也会装填。",86*scale,1050);
            VManualItem(c,"fire","E · 圣火飞刀","掷向影魔的可见影子可引燃它，也能点燃灯芯。初始携带两把，最多五把；空手时不会投出。",278*scale,1050);
            VManualItem(c,"light","从灯火里补充飞刀","每将一盏熄灭的灯重新点亮，获得一把圣火飞刀。击中已点亮的灯只延续灯火，不重复获得飞刀。",470*scale,1050);
        } else if(CurrentTab=="lamps") {
            VT(c,"守住火焰，让影魔留在光里。",0,0,1050,60,22);
            string[] stage={"强光","中光","弱光"},descriptions={"火焰饱满，照亮周围","火焰收小，亮度减弱","将要熄灭，仍能照出敌影"};
            for(int i=0;i<3;i++) {
                var tile=W.Rect(c,"Flame stage "+i,i*354,90,330,225*scale);VRule(tile,32,0,266,.22f);VRule(tile,32,225*scale-1,266,.22f);
                float h=70-i*15;W.Icon(tile,"fire",140,20+70-h,50-i*10,h,W.Alpha(W.Hex("E9BB88"),1-i*.22f));
                VT(tile,stage[i],20,117,290,48*scale,22,null,true,TextAnchor.MiddleCenter);
                VT(tile,descriptions[i],20,174*scale,290,60,16,W.Muted,false,TextAnchor.MiddleCenter);
            }
            VManualItem(c,"light","用圣水箭或飞刀延续灯火","圣水箭或圣火飞刀命中灯芯，都能重新点亮或刷新壁灯寿命。黑痕向灯芯收拢时，射中该灯可打断灭灯仪式。",350*scale,1050);
            VManualItem(c,"shield","紫焰表示灯已熄灭","紫焰只标记灭灯状态，不照出敌影。六灯全灭时影魔免疫伤害并追击；射亮任意一盏灯可打断暗猎，使它硬直 2 秒。",520*scale,1050);
        } else {
            VT(c,"狩猎它的影子，而非它的外表。",0,0,1050,70,22);
            VManualItem(c,"crossbow","瞄准敌影","射中地面的怪物影子可造成伤害。本体会挡住弩箭；指尖可以命中，指缝会让箭落在地面。",90*scale,1050);
            VManualItem(c,"eye","移出攻击预警","影魔攻击只判定你的身体，玩家影子不会受伤。移出固定爪圈和紫色斩线；跳跃可避开地面爪痕和震爆。",260*scale,1050);
            VManualItem(c,"bolt","为装填留下空隙","每匣四发，射后会重新上弦。装填约 1.20 秒；收弩、装箭、举弩期间无法射击。圣火飞刀单独使用。",430*scale,1050);
            VManualItem(c,"holy","疾跑与呼吸回血","按住 Shift 疾跑：战斗消耗体力，脱战无限。停止疾跑后恢复体力，耗尽后需恢复一部分才能再次疾跑。连续 5 秒未受伤后每秒回复 8 点生命；再次受伤重新计时。",600*scale,1050);
        }
        VFooter(p,"WASD 移动    Shift 疾跑    Space 跳跃    左键 射击");VB(p,"tutorial","再次查看教学",1160,804,248,52,quiet:true,size:18);
    }
    void VManualItem(Transform p,string icon,string title,string body,float y,float width) {
        VRule(p,0,y,width);W.Icon(p,icon,0,y+24,38,42,W.Silver);VT(p,title,64,y+20,width-64,44*VScale,22);
        VT(p,body,64,y+76*VScale,width-64,80*VScale,18,W.Muted);
    }
    void VSettings() {
        VDim();var p=VPanel("Settings");VHeading(p,"PREFERENCES","设置");
        var body=VTabbedBody(p,"settings",new[]{"interface","audio","display","controls"},new[]{"界面与辅助","声音","画面","操作"});
        var c=W.Scroll(body,"Settings scroll",0,0,1112,564,CurrentTab=="audio"?770:CurrentTab=="controls"?770:590);string tab=CurrentTab;
        if(tab=="audio") {
            VRange(c,"Master","总音量","控制全部声音。",Settings.Master,0,1,0,true);
            VRange(c,"Effects","效果音量","界面确认与弩机声音",Settings.Effects,0,1,142,true);
            VRange(c,"Music","音乐音量",ShiyinAudio.HasMusic?"环境音乐与战斗配乐。":"当前演示暂无音乐。",Settings.Music,0,1,284,true,ShiyinAudio.HasMusic);
            VRange(c,"Narration","旁白音量","英文讲述与战斗指引。",Settings.Narration,0,1,426,true);
            VToggle(c,"NarrationSubtitles","中文旁白字幕","配合英文语音显示中文讲述内容。",Settings.NarrationSubtitles,568);
            VT(c,"危险与武器状态也会通过画面传达。",0,719,1040,45,16,W.Muted);
        } else if(tab=="display") {
            VCycle(c,"DisplayMode","显示模式","选择游戏窗口的显示方式。",new[]{"全屏","无边框","窗口"},Mathf.Clamp(Settings.DisplayMode,0,2),new[]{0f,1f,2f},0);
            VRange(c,"Brightness","画面亮度","让弱光中的火焰与影子仍能辨认。",Settings.Brightness,.7f,1.3f,142,true);
            VRange(c,"Fov","视野","调整第一人称可见范围。",Settings.Fov,60,90,284,false,true,"°");
        } else if(tab=="controls") {
            string[] actions={"移动","瞄准","圣水箭","跳跃","圣火飞刀","装填","武器手册","暂停 / 返回","疾跑","守灯者灯心 · 回血"},keys={"W A S D","鼠标","左键","Space","E","R","Tab","Esc","按住 Shift","Q"};
            for(int i=0;i<actions.Length;i++){int col=i%2,row=i/2;float x=col*540,y=row*72;VT(c,actions[i],x,y,260,60,18);VT(c,keys[i],x+280,y,220,60,18,W.Silver,false,TextAnchor.UpperRight);VRule(c,x,y+55,500);}
            VT(c,"疾跑：战斗消耗体力，脱战无限；停止疾跑后恢复体力。",0,356,1050,44,16,W.Muted);
            VRange(c,"Sensitivity","鼠标灵敏度","调整水平与垂直瞄准速度。",Settings.Sensitivity,.5f,4,406,false);
            VToggle(c,"InvertY","反转垂直视角","向上移动鼠标时向下看。",Settings.InvertY,558);
        } else {
            int sizeIndex=Settings.TextScale>1.2f?2:Settings.TextScale>1.05f?1:0;
            VCycle(c,"TextScale","文字大小","放大说明、数值与菜单文字。",new[]{"标准 · 100%","较大 · 115%","大 · 130%"},sizeIndex,new[]{1f,1.15f,1.3f},0);
            VToggle(c,"Contrast","增强信息底衬","提高复杂场景上的文字清晰度。",Settings.Contrast,142);
            VToggle(c,"ReducedMotion","减少动态反馈","减少控件过渡与命中闪现，保留状态信息",Settings.ReducedMotion,284);
            VToggle(c,"Tutorial","进入时教学","开始狩猎时显示三步基础说明。",Settings.Tutorial,426);
        }
        VFooter(p,"更改会自动保存");VB(p,"defaults","恢复默认",1190,804,218,52,quiet:true,size:18);
    }
    void VSettingLabel(Transform p,string label,string help,float y) {
        VT(p,label,0,y+13,600,45*VScale,20);VT(p,help,0,y+62,605,64,16,W.Muted);VRule(p,0,y+132,1070);
    }
    void VToggle(Transform p,string key,string label,string help,bool value,float y) {
        VSettingLabel(p,label,help,y);string action="setting:"+key;
        var control=W.Toggle(p,action,value,840,y+20,220,v=>SetPreference(key,v?1:0),VSize(18));vControls[action]=control;
    }
    void VCycle(Transform p,string key,string label,string help,string[] options,int index,float[] values,float y) {
        VSettingLabel(p,label,help,y);string action="setting:"+key;Button b=null;
        b=W.Button(p,action,options[index]+"  >",740,y+16,325,64,()=>{index=(index+1)%options.Length;SetPreference(key,values[index]);if(b!=null)b.GetComponentInChildren<Text>().text=options[index]+"  >";},VSize(18));vControls[action]=b;
    }
    void VRange(Transform p,string key,string label,string help,float value,float min,float max,float y,bool percent=false,bool enabled=true,string suffix="") {
        VSettingLabel(p,label,help,y);string action="setting:"+key;
        Func<float,string> format=v=>percent?Mathf.RoundToInt(v*100)+"%":key=="Sensitivity"?v.ToString("0.0"):Mathf.RoundToInt(v)+suffix;
        var output=VT(p,format(value),985,y+30,85,45,18,W.Muted,false,TextAnchor.MiddleRight);
        var slider=W.Slider(p,action,value,min,max,720,y+24,235,v=>{SetPreference(key,v);output.text=format(v);},key=="Fov");
        slider.interactable=enabled;vControls[action]=slider;
    }
    void VDeveloper() {
        VDim();var p=VPanel("Developer tools");VHeading(p,"DEVELOPER TOOLS","开发者工具");
        var c=W.Scroll(p,"Developer scroll",48,189,1360,564,650);
        string[] ids={"freeze","lock","hide","restart"},keys={"F1","F2","F3","F5"},names={"冻结画面","锁定当前血量","隐藏全部界面","重新开战"};
        string[] help={"停止角色、镜头、特效与战斗计时。","保留当前生命值，暂时不受伤。","用于截图；再次按 F3 恢复。","恢复满血与圣水箭，解除冻结，保留锁血。"};
        bool[] active={vSnapshot.Frozen,vSnapshot.HealthLocked,vSnapshot.Hidden,false};
        for(int i=0;i<4;i++) {
            float y=i*132;var keyBox=W.Rect(c,"Key "+keys[i],0,y+18,65,48);W.Border(keyBox,W.Alpha(W.Silver,.45f));VT(keyBox,keys[i],0,0,65,48,20,W.Silver,false,TextAnchor.MiddleCenter);
            VT(c,names[i],95,y+12,860,44,22);VT(c,help[i],95,y+64,860,60,18,W.Muted);
            VB(c,"dev:"+ids[i],i==3?"重新开战":active[i]?"已开启":"关闭",1100,y+20,220,64,active[i]);VRule(c,0,y+120,1320);
        }
        VT(c,"截图时可先按 F1 冻结，再按 F3 隐藏界面。冻结与锁血会在战斗底部标明。",0,555,1310,80,16,W.Muted);
        VFooter(p,"关闭此页面不会关闭已开启的功能。");
    }
    void VResult(bool won) {
        VDim(.78f);var p=W.Center(vPage,"Hunt result",1260,690,0,25);
        W.Icon(p,won?"crown":"emblem",575,0,110,112,W.Silver);VT(p,won?"THE SHADOW RECEDES":"THE HUNT ENDS",0,155,1260,40,18,W.Silver,false,TextAnchor.MiddleCenter);
        VT(p,won?"影魔已被驱逐":"猎人倒下了",0,224,1260,117,70,won?W.Hex("D9DFD2"):W.Hex("D7B4AE"),true,TextAnchor.MiddleCenter);
        W.Icon(p,"divider",400,371,460,24,W.Silver);VT(p,won?$"灯火尚存，{EncounterName}重归寂静。":"守住灯火，让敌影显形。\n调整下一箭，寻找新的机会。",190,426,880,100,20,W.Muted,false,TextAnchor.MiddleCenter);
        VB(p,"result-retry","再次挑战   R",330,570,280,70,true);VB(p,"result-menu","返回主菜单",636,570,280,70);
        VT(p,EncounterName+" · 战斗演示",0,673,1260,40,16,W.Muted,false,TextAnchor.MiddleCenter);
    }
    void VConfirmation() {
        var group=vPage.gameObject.AddComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
        vControls.Clear();vModal=W.Stretch(vRoot,"Confirmation");W.Image(W.Stretch(vModal,"Modal scrim"),W.Hex("05090F",.86f),true);
        var p=W.Center(vModal,"Confirmation panel",930,450);W.Image(p,W.Alpha(W.Surface,.94f),true);VRule(p,0,0,930,.6f);VRule(p,0,449,930,.3f);W.Corners(p,W.Silver);
        W.Icon(p,ConfirmationKey=="defaults"?"settings":"back",56,41,40,40,W.Silver);
        VT(p,ConfirmTitle,56,106,818,75,36,null,true);VT(p,ConfirmBody,56,199,818,108,20,W.Muted);
        VB(p,"cancel","取消",432,334,180,64,true);VB(p,"confirm",ConfirmActionLabel,635,334,239,64,danger:true);
    }
    HunterDamageBorder damageBorder;
    void VBuildHud() {
        damageBorder=W.Stretch(vHud,"Hunter damage vignette").gameObject.AddComponent<HunterDamageBorder>();
        damageBorder.raycastTarget=false;damageBorder.enabled=false;
        var boss=W.Anchor(vHud,"Boss status",new Vector2(.5f,1),new Vector2(.5f,1),0,-48,660,126);
        vBossObject=boss.gameObject;
        vBossName=VT(boss,"古堡之主",0,0,660,48,27,null,true,TextAnchor.MiddleCenter);
        var track=W.Fill(boss,"Boss health track",20,65,620,3,W.Alpha(W.Silver,.25f));vBossFill=W.Fill(track.transform,"Boss health",0,0,620,3,W.Health).rectTransform;
        W.Diamond(boss,"Boss silver terminal left",3,63,7,W.Silver);W.Diamond(boss,"Boss silver terminal right",650,63,7,W.Silver);
        vBossIcon=W.Icon(boss,"shield",20,91,23,24,W.Silver);vBossStatus=VT(boss,"",52,81,580,45,17,W.Muted,false,TextAnchor.MiddleCenter);
        var exploration=W.Anchor(vHud,"Exploration objective",new Vector2(1,1),new Vector2(1,1),-64,-48,320,150);
        W.Fill(exploration,"Objective silver rule",319,0,1,134,W.Alpha(W.Silver,.4f));
        VT(exploration,"当前目标",0,0,296,28,14,W.Silver,false,TextAnchor.MiddleRight);
        vObjectiveTitle=VT(exploration,"沿山路进入古堡",0,34,296,62,20,null,true,TextAnchor.UpperRight);
        vObjectiveBody=VT(exploration,"Shift 疾跑 · 脱战无限",0,104,296,40,15,W.Muted,false,TextAnchor.UpperRight);
        vObjectiveLamps=VT(exploration,"",0,104,296,40,17,W.Silver,false,TextAnchor.UpperRight);
        vExplorationObjective=exploration.gameObject;
        var hunter=W.Anchor(vHud,"Hunter health",new Vector2(0,0),new Vector2(0,0),64,172,360,90);vHealthBack=W.Image(W.Stretch(hunter,"Health backing"),Color.clear);
        // Rotate the engraved frame about its centre, then centre the upright
        // emblem inside its diamond's inscribed area (including UI scaling).
        var seal=W.Rect(hunter,"Hunter covenant seal",0,0,38,38);
        seal.pivot=new Vector2(.5f,.5f);seal.anchoredPosition=new Vector2(27,-36);
        W.Border(seal,W.Alpha(W.Silver,.75f));seal.localRotation=Quaternion.Euler(0,0,45);
        W.Icon(hunter,"emblem",17,22,20,28,W.Silver);
        VT(hunter,"猎魔人",64,0,110,36,17,W.Ink,true,TextAnchor.MiddleLeft);vHealth=VT(hunter,"100 / 100",176,0,184,36,20,null,false,TextAnchor.MiddleRight);
        var healthTrack=W.Fill(hunter,"Health track",64,48,296,4,W.Alpha(W.Silver,.24f));vHealthFill=W.Fill(healthTrack.transform,"Health",0,0,296,4,W.Health).rectTransform;
        vLowHealth=VT(hunter,"生命危急",64,61,296,29,15,W.Hex("EFB1A7"));
        var condition=W.Anchor(hunter,"Hunter condition",new Vector2(0,1),new Vector2(0,0),64,18,296,46);vConditionBack=W.Image(condition,Color.clear);
        vConditionIcon=W.Icon(condition,"rooted",0,9,22,25,W.Silver);vCondition=VT(condition,"",32,0,200,46,16,W.Silver,false,TextAnchor.MiddleLeft);vConditionTime=VT(condition,"",232,0,64,46,16,W.Muted,false,TextAnchor.MiddleRight);vConditionObject=condition.gameObject;
        var stamina=W.Anchor(vHud,"Hunter stamina",new Vector2(0,0),new Vector2(0,0),128,94,296,70);
        vStaminaObject=stamina.gameObject;vStaminaBack=W.Image(W.Stretch(stamina,"Stamina backing"),Color.clear);
        VT(stamina,"体力",0,0,130,27,15,W.Muted,false,TextAnchor.MiddleLeft);
        vStaminaValue=VT(stamina,"100%",176,0,120,27,17,W.Silver,false,TextAnchor.MiddleRight);vStaminaValue.gameObject.name="Stamina percentage";
        var staminaTrack=W.Fill(stamina,"Stamina track",0,36,296,2,W.Alpha(W.Silver,.24f));
        vStaminaColor=W.Fill(staminaTrack.transform,"Stamina fill",0,0,296,2,W.Energy);vStaminaFill=vStaminaColor.rectTransform;
        vStaminaState=VT(stamina,"",0,43,296,27,14,W.Muted);vStaminaState.gameObject.name="Stamina feedback";
        var ammo=W.Anchor(vHud,"Crossbow ammunition",new Vector2(1,0),new Vector2(1,0),-64,144,360,144);vAmmoBack=W.Image(W.Stretch(ammo,"Ammo backing"),Color.clear);
        vArrowIcon=W.Icon(ammo,"holy",0,9,26,34,W.Silver);vArrowName=VT(ammo,"圣水弩",40,0,144,52,19,null,true,TextAnchor.MiddleLeft);vAmmo=VT(ammo,"4 / 4",196,0,164,58,36,null,true,TextAnchor.MiddleRight);
        vAmmo.horizontalOverflow=vHealth.horizontalOverflow=HorizontalWrapMode.Overflow;
        VRule(ammo,0,66,360,.45f);
        vBolts=new Image[4];for(int i=0;i<4;i++)vBolts[i]=W.Icon(ammo,"bolt",281+i*22,78,12,20,W.Silver);
        vWeaponLabel=VT(ammo,"",0,99,360,35,15,W.Muted,false,TextAnchor.MiddleRight);
        var progress=W.Fill(ammo,"Weapon progress",0,142,360,2,W.Alpha(W.Silver,.2f));vWeaponFill=W.Fill(progress.transform,"Progress",0,0,360,2,W.Silver).rectTransform;vWeaponProgress=progress.gameObject;
        vQueued=VT(ammo,"",0,72,260,29,14,W.Silver,false,TextAnchor.MiddleLeft);
        var knife=W.Anchor(vHud,"Holy fire knives",new Vector2(1,0),new Vector2(1,0),-64,80,360,54);vKnifeBack=W.Image(knife,Color.clear);
        var key=W.Rect(knife,"Knife key",0,11,26,26);W.Border(key,W.Alpha(W.Silver,.6f));VT(key,"E",0,0,26,26,14,W.Ink,false,TextAnchor.MiddleCenter);
        VT(knife,"圣火飞刀",38,0,106,48,16,W.Silver,false,TextAnchor.MiddleLeft);
        vKnifeCount=VT(knife,"2 / 5",150,0,65,48,16,W.Flame,false,TextAnchor.MiddleRight);
        vKnifeCount.gameObject.name="Holy knife count";
        var relic=W.Anchor(vHud,"Guardian healing relic",new Vector2(1,0),new Vector2(1,0),-64,30,360,44);
        vRelic=relic.gameObject;
        var relicKey=W.Rect(relic,"Relic key",0,9,26,26);W.Border(relicKey,W.Alpha(W.Silver,.65f));
        VT(relicKey,"Q",0,0,26,26,15,W.Silver,false,TextAnchor.MiddleCenter);
        VT(relic,"守灯者灯心",38,0,110,44,16,W.Silver,false,TextAnchor.MiddleLeft);
        vRelicCount=VT(relic,"",150,0,65,44,16,W.Energy,false,TextAnchor.MiddleRight);
        vRelicRecharge=VT(relic,"",218,0,142,44,14,W.Muted,false,TextAnchor.MiddleRight);
        vRelic.SetActive(false);
        vKnifeMarks=new Image[5];
        for(int i=0;i<vKnifeMarks.Length;i++) {var mark=W.Fill(knife,"Holy knife mark "+i,236+i*27,17,5,18,W.Alpha(W.Flame,.2f));mark.rectTransform.localRotation=Quaternion.Euler(0,0,-25);vKnifeMarks[i]=mark;}
        var crosshair=W.Center(vHud,"Crosshair",24,24);W.Fill(crosshair,"Top",11,0,2,4,W.Alpha(W.Ink,.7f));W.Fill(crosshair,"Bottom",11,20,2,4,W.Alpha(W.Ink,.7f));W.Fill(crosshair,"Left",0,11,4,2,W.Alpha(W.Ink,.7f));W.Fill(crosshair,"Right",20,11,4,2,W.Alpha(W.Ink,.7f));
        var center=W.Rect(crosshair,"Aim center",9,9,6,6);W.Border(center,W.Alpha(W.Ink,.6f));var hit=W.Center(crosshair,"Hit confirmation",19,19);W.Border(hit,W.Ink,2);hit.localRotation=Quaternion.Euler(0,0,45);vHitObject=hit.gameObject;
        var notice=W.Anchor(vHud,"Combat notice",new Vector2(.5f,1),new Vector2(.5f,1),0,-194,820,134);vNoticeBack=W.Image(W.Stretch(notice,"Notice backing"),Color.clear);
        VRule(notice,300,0,92,.38f);VRule(notice,428,0,92,.38f);W.Diamond(notice,"Notice seal",407,-3,6,W.Silver);
        vNoticeTitle=VT(notice,"",24,18,772,49,24,null,true,TextAnchor.MiddleCenter);vNoticeBody=VT(notice,"",24,75,772,58,17,W.Muted,false,TextAnchor.MiddleCenter);vNoticeObject=notice.gameObject;
        vCombatGuide=VT(vHud,"",0,0,700,150,26,W.Ink,true,TextAnchor.UpperCenter);
        vCombatGuide.name="Combat guidance";
        var guideRect=vCombatGuide.rectTransform;
        guideRect.anchorMin=guideRect.anchorMax=guideRect.pivot=new Vector2(.5f,1);
        vCombatGuide.gameObject.SetActive(false);
        var toast=W.Anchor(vHud,"Combat toast",new Vector2(.5f,0),new Vector2(.5f,0),0,217,900,62);vToastBack=W.Image(W.Stretch(toast,"Toast backing"),Color.clear);vToast=VT(toast,"",20,0,860,62,18,W.Silver,false,TextAnchor.MiddleCenter);vToastObject=toast.gameObject;
        var dev=W.Anchor(vHud,"Developer status",new Vector2(0,0),new Vector2(0,0),64,24,1000,32);vDev=VT(dev,"",0,0,1000,32,15,W.Muted);
        foreach(var text in vHud.GetComponentsInChildren<Text>(true))W.TextOutline(text);
        VFitHud();
    }
    public void RefreshHud(ShiyinHudSnapshot data) {
        if(damageBorder!=null && Lab!=null) {
            float strength=Lab.HurtIntensity;
            damageBorder.enabled=strength>0;
            if(strength>0)damageBorder.color=new Color(.48f,.055f,.075f,strength*(Settings.ReducedMotion ? .34f : .64f));
        }
        vSnapshot=data;if(vHud==null)return;if(Mathf.Abs(vAppliedScale-VScale)>.001f)VApplyFonts();
        if(vAppliedReducedMotion!=(Settings!=null&&Settings.ReducedMotion))VApplyMotion();
        VFitHud();vBossObject.SetActive(data.EncounterStarted);vExplorationObjective.SetActive(true);
        vObjectiveTitle.text=data.EncounterStarted?(data.BossName!=null&&data.BossName.StartsWith("守灯者")?"守住灯火，击败守灯者":"夺回灯火，击败影魔"):"沿山路进入古堡";
        vObjectiveBody.gameObject.SetActive(!data.EncounterStarted);vObjectiveLamps.gameObject.SetActive(data.EncounterStarted);
        vObjectiveBody.text=data.CombatActive?"Shift 疾跑 · 消耗体力":"Shift 疾跑 · 脱战无限";
        vObjectiveLamps.text=data.LampCapacity>0?"亮灯    "+data.LitLamps+" / "+data.LampCapacity:"亮灯    "+data.LitLamps+" 盏";
        vBossName.text=string.IsNullOrEmpty(data.BossName)?"古堡之主":data.BossName;
        int max=Mathf.Max(1,data.MaxHealth);
        vHealth.text=data.Health+" / "+max;vAmmo.text=data.Rounds+" / 4";vArrowName.text="圣水弩";vArrowIcon.sprite=W.IconSprite("holy");vArrowIcon.color=vArrowColors[0];
        vKnifeCount.text=data.HolyKnives+" / "+Mathf.Max(1,data.HolyKnifeCapacity);
        vKnifeCount.color=data.HolyKnives>0?W.Flame:W.Muted;
        vRelic.SetActive(data.HasGuardianRelic);
        if(data.HasGuardianRelic) {
            vRelicCount.text=data.GuardianRelicCharges+" / "+ShadowLab.GuardianRelicCapacity;
            vRelicCount.color=data.GuardianRelicCharges>0?W.Energy:W.Muted;
            vRelicRecharge.text=data.GuardianRelicCharges==ShadowLab.GuardianRelicCapacity ? "充能已满" : "重燃 "+data.GuardianRelicRelights+" / 2";
        }
        for(int i=0;i<vKnifeMarks.Length;i++)vKnifeMarks[i].color=i<data.HolyKnives?W.Flame:W.Alpha(W.Silver,.22f);
        vHealthFill.sizeDelta=new Vector2(296*Mathf.Clamp01((float)data.Health/max),4);vBossFill.sizeDelta=new Vector2(620*Mathf.Clamp01(data.BossHealth01),3);
        float stamina=ShiyinSettings.FiniteClamp(data.SprintStamina01,0,1,1);
        vStaminaObject.SetActive(data.CombatActive&&data.Health>0);
        vStaminaFill.sizeDelta=new Vector2(296*stamina,2);vStaminaValue.text=Mathf.CeilToInt(stamina*100)+"%";
        Color staminaColor=data.SprintExhausted?W.Hex("E7A391"):stamina<=.25f?W.Hex("E8B77C"):W.Energy;
        vStaminaColor.color=vStaminaValue.color=staminaColor;
        vStaminaState.text=data.SprintExhausted?(stamina<=.001f?"体力耗尽 · 暂不可疾跑":"恢复中 · 暂不可疾跑"):
            data.Sprinting?"疾跑中 · 消耗体力":stamina<.999f?"停止疾跑后恢复":"Shift 疾跑";
        vStaminaState.color=data.SprintExhausted?staminaColor:W.Muted;
        vWeaponLabel.text=data.WeaponLabel??"";vWeaponLabel.color=data.Rounds==0&&!data.WeaponBusy?W.Hex("E6B1A5"):W.Muted;
        vQueued.text="";
        vWeaponProgress.SetActive(data.WeaponBusy);vWeaponFill.sizeDelta=new Vector2(360*Mathf.Clamp01(data.WeaponProgress),2);
        vLowHealth.gameObject.SetActive(data.LowHealth||!string.IsNullOrEmpty(data.RecoveryStatus));
        vLowHealth.text=(data.LowHealth?"生命危急":"")+(!string.IsNullOrEmpty(data.RecoveryStatus)?(data.LowHealth?" · ":"")+data.RecoveryStatus:"");
        vLowHealth.color=data.Recovering?W.Hex("9ED8BC"):data.LowHealth?W.Hex("EFB1A7"):W.Muted;
        vBossStatus.text=data.BossStatus??"";bool bossStatus=!string.IsNullOrEmpty(data.BossStatus);vBossIcon.gameObject.SetActive(bossStatus);
        if(bossStatus){vBossIcon.sprite=W.IconSprite(string.IsNullOrEmpty(data.BossStatusIcon)?"shield":data.BossStatusIcon);vBossStatus.color=data.BossStatusIcon=="shield"?W.Hex("E8AAA0"):W.Silver;}
        vConditionObject.SetActive(!string.IsNullOrEmpty(data.Condition));vCondition.text=data.Condition??"";
        vConditionTime.text=data.ConditionTime>0?data.ConditionTime.ToString("0.0")+"s":"";
        vConditionIcon.sprite=W.IconSprite(data.ConditionTime>0?"rooted":"bolt");
        vNoticeObject.SetActive(!string.IsNullOrEmpty(data.NoticeTitle));vNoticeTitle.text=data.NoticeTitle??"";vNoticeBody.text=data.NoticeBody??"";vNoticeTitle.color=data.DangerNotice?W.Hex("EFB1A7"):W.Ink;
        RefreshCombatGuidance(data);
        vToastObject.SetActive(!string.IsNullOrEmpty(data.Toast));vToast.text=data.Toast??"";vHitObject.SetActive(data.Hit);
        vDev.text=(data.Frozen?"F1 画面已冻结    ":"")+(data.HealthLocked?"F2 血量已锁定":"");
        for(int i=0;i<4;i++)vBolts[i].color=i<data.Rounds?W.Silver:W.Alpha(W.Silver,.2f);
        bool contrast=Settings!=null&&Settings.Contrast;
        vHealthBack.color=vAmmoBack.color=vStaminaBack.color=vKnifeBack.color=vConditionBack.color=W.Alpha(W.Surface,contrast?.76f:0);
        vNoticeBack.color=vToastBack.color=W.Alpha(W.Surface,contrast?.76f:0);
    }
    void RefreshCombatGuidance(ShiyinHudSnapshot data) {
        if(displayedGuides!=data.CombatGuides) {
            displayedGuides=data.CombatGuides;
            var lines=new List<string>(3);
            if((displayedGuides&CombatGuidance.GhostShadows)!=0)lines.Add("攻击它们的影子");
            if((displayedGuides&CombatGuidance.AcolyteLight)!=0)lines.Add("当侍僧窃取灯光时，它是无敌的\n熄灭紫色的灯火");
            if((displayedGuides&CombatGuidance.HealWithRelic)!=0)lines.Add("按q使用灯心回血");
            vCombatGuide.text=string.Join("\n",lines);
            vCombatGuide.gameObject.SetActive(displayedGuides!=CombatGuidance.None);
        }
        if(displayedGuides==CombatGuidance.None)return;
        // Keep multi-line guidance clear of the right objective, boss bar and attack notices.
        float top=VReferenceWidth<1380 ? 206 : data.EncounterStarted ? 192 : 66;
        if(!string.IsNullOrEmpty(data.NoticeTitle))top=342;
        float width=VReferenceWidth<1380 ? VReferenceWidth-64 : VReferenceWidth-800;
        var rect=vCombatGuide.rectTransform;
        rect.sizeDelta=new Vector2(Mathf.Clamp(width,250,1020),178);
        float bob=Settings!=null && Settings.ReducedMotion ? 0 : 5*Mathf.Sin(Time.time*2.8f);
        rect.anchoredPosition=new Vector2(0,-top+bob);
    }
    void VFitHud(float referenceWidth=0) {
        // Keep a clear side gutter for the objective on 4:3 as well as wide screens.
        float width=referenceWidth>0?referenceWidth:VReferenceWidth;
        if(vBossObject!=null)vBossObject.transform.localScale=Vector3.one*Mathf.Clamp((width-832)/660,.65f,1);
        if(vToastObject!=null) {
            float toastWid