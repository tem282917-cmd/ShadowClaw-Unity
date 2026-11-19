using UnityEngine;
using UnityEngine.UI;
using W = ShiyinWidgets;

public sealed partial class ShiyinUI {
    const float WindLead=1.6f, RevealDuration=2.4f;
    CanvasGroup prologueCurtain;
    Text prologueCaption;
    AudioSource prologueWind;
    FairyTaleNarrator prologueNarrator;
    float prologueElapsed, revealElapsed, captionElapsed;
    bool voiceStarted, revealingMenu;

    void BuildPrologue() {
        var curtain=W.Stretch(vPage,"Opening blackout");
        W.Image(curtain,Color.black);
        prologueCurtain=curtain.gameObject.AddComponent<CanvasGroup>();
        prologueCaption=W.Text(curtain,"",0,0,0,140,VSize(34),W.Ink,true,TextAnchor.MiddleCenter);
        var frame=prologueCaption.rectTransform;
        frame.anchorMin=new Vector2(.12f,.5f);frame.anchorMax=new Vector2(.88f,.5f);
        frame.pivot=new Vector2(.5f,.5f);frame.anchoredPosition=Vector2.zero;frame.sizeDelta=new Vector2(0,150);
        prologueCaption.supportRichText=false;prologueCaption.raycastTarget=false;
        prologueWind=curtain.gameObject.AddComponent<AudioSource>();
        prologueWind.playOnAwake=false;prologueWind.loop=true;prologueWind.spatialBlend=0;
        prologueWind.volume=0;
        prologueWind.clip=Resources.Load<AudioClip>("EnvironmentDynamics/WindWooshLoop");
    }
    void AdvancePrologue(float dt) {
        // Loading a large chapter can make the first frame longer than the wind lead.
        dt=Mathf.Min(dt,.15f);
        if(prologueNarrator==null)prologueNarrator=Lab.GetComponent<FairyTaleNarrator>();
        if(prologueNarrator==null)return;
        if(prologueElapsed==0 && prologueWind.clip!=null)prologueWind.Play();
        prologueElapsed+=dt;
        if(!voiceStarted && prologueElapsed>=WindLead) {
            voiceStarted=true;
            prologueNarrator.BeginOpening();
        }
        if(voiceStarted && prologueNarrator.CurrentId==FairyTaleNarrator.OpeningId) {
            string line=prologueNarrator.CurrentSubtitle;
            if(prologueCaption.text!=line){prologueCaption.text=line;captionElapsed=0;}
            captionElapsed+=dt;
            float opacity=Mathf.Min(Mathf.SmoothStep(0,1,captionElapsed/.75f),
                Mathf.SmoothStep(0,1,prologueNarrator.SecondsLeftInPart/.55f));
            prologueCaption.color=W.Alpha(W.Ink,Settings.NarrationSubtitles?opacity:0);
        } else if(voiceStarted && !revealingMenu) {
            revealingMenu=true;
            prologueCaption.text="";
            audioFeedback.MusicDirector?.SetTitlePage(true, startAtMenuVolume: true);
        }
        if(revealingMenu)revealElapsed+=dt;
        float black=1-Mathf.SmoothStep(0,1,revealElapsed/RevealDuration);
        prologueCurtain.alpha=black;
        prologueWind.volume=.32f*Settings.Effects*Mathf.SmoothStep(0,1,prologueElapsed/.85f)*black;
        if(revealingMenu && revealElapsed>=RevealDuration) {
            prologueWind.Stop();
            SetView("menu");
        }
    }
}
