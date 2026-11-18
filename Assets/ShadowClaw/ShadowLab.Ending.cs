using UnityEngine;

public sealed partial class ShadowLab
{
    [Header("Victory story presentation")]
    [Min(.1f)] public float VictoryDeathSeconds = 3.2f;
    [Min(.1f)] public float VictoryFadeOutSeconds = 1.4f;
    [Min(.1f)] public float VictoryStoryLineSeconds = 4.8f;
    [Min(.1f)] public float VictoryMenuRevealSeconds = 1.4f;
    static readonly string[] storyIds = { FairyTaleNarrator.CastleEndingId, FairyTaleNarrator.EpilogueId };
    const float VoiceTail = .32f;
    readonly VictoryPresentationState victoryPresentation = new VictoryPresentationState();
    Quaternion victoryCameraStart;
    public bool VictoryPresentationActive => victoryPresentation.Active;
    public VictoryPresentationState.Stage VictoryPhase => victoryPresentation.Phase;
    public float VictoryBlackOpacity => victoryPresentation.BlackOpacity;
    public float VictoryStoryOpacity => ShowingDispersalSubtitle ? 1 : victoryPresentation.StoryOpacity;
    public float VictoryStoryStart => victoryPresentation.StoryStart;
    public float VictoryPageDuration(int index) => victoryPresentation.PageDuration(index);
    public bool VictoryMenuReady => victoryPresentation.MenuReady;
    public bool ShowingDispersalSubtitle => narrator != null && narrator.CurrentId == FairyTaleNarrator.DispersalId &&
        (VictoryPhase == VictoryPresentationState.Stage.Death || VictoryPhase == VictoryPresentationState.Stage.FadeOut) &&
        (UI == null || UI.Settings.NarrationSubtitles);
    public string VictoryStoryText {
        get {
            int line = victoryPresentation.StoryIndex;
            if (ShowingDispersalSubtitle) return narrator.CurrentSubtitle;
            return line >= 0 ? narrator?.CurrentSubtitle ?? "" : "";
        }
    }
    void BeginVictoryPresentation() {
        if (VictoryPhase != VictoryPresentationState.Stage.Idle) return;
        float[] pages = new float[storyIds.Length];
        for (int i = 0; i < pages.Length; i++)
            pages[i] = Mathf.Max(VictoryStoryLineSeconds, (narrator?.LineDuration(storyIds[i]) ?? 0) + VoiceTail);
        victoryPresentation.Begin(VictoryDeathSeconds, VictoryFadeOutSeconds,
            (narrator?.LineDuration(FairyTaleNarrator.DispersalId) ?? 0) + VoiceTail, pages, VictoryMenuRevealSeconds);
        narrator?.PlayEnding(FairyTaleNarrator.DispersalId);
        modelView?.BeginDeath();
        UI?.BeginVictorySequence();
        if(Eye!=null)victoryCameraStart=Eye.transform.rotation;
    }
    void ResetVictoryPresentation() {
        if (VictoryPhase != VictoryPresentationState.Stage.Idle) narrator?.EndEnding();
        victoryPresentation.Reset();
        modelView?.ResetDeath();
        UI?.ClearVictorySequence();
    }
    public void CancelVictoryPresentation() { ResetVictoryPresentation(); }
    internal void TickVictoryPresentation(float unscaledDt) => AdvanceVictory(unscaledDt, false);
    void AdvanceVictory(float unscaledDt, bool trackDevice) {
        if (!victoryPresentation.Active) return;
        // A stalled frame must not skip an entire spoken page. At a boundary
        // wait for the audio device to finish the previous recording as well.
        if (trackDevice && !VictoryMenuReady) {
            float untilNext = victoryPresentation.NextVoiceBoundary - victoryPresentation.Elapsed;
            if (narrator != null && narrator.EndingSpeechPending) untilNext -= .001f;
            unscaledDt = Mathf.Min(unscaledDt, Mathf.Max(0, untilNext));
        }
        float previousDeath=victoryPresentation.DeathProgress;
        int previousPage = victoryPresentation.StoryIndex;
        victoryPresentation.Advance(unscaledDt);
        int page = victoryPresentation.StoryIndex;
        if (page >= 0 && page != previousPage) narrator?.PlayEnding(storyIds[page]);
        if (VictoryMenuReady) RestoreExterior();
        if (VictoryMenuReady && narrator != null && narrator.Speaking) narrator.EndEnding();
        modelView?.SampleDeath(victoryPresentation.DeathProgress);
        modelView?.SampleDeathMist(victoryPresentation.Elapsed,victoryPresentation.DeathDuration,VictoryBlackOpacity,
            VictoryPhase==VictoryPresentationState.Stage.Death || VictoryPhase==VictoryPresentationState.Stage.FadeOut);
        if(previousDeath<.89f && victoryPresentation.DeathProgress>=.89f && state!=null) {
            state.Witch.Show(false);state.Arm.Show(false);shadowBurn?.Clear();
        }
        if(Eye!=null && state!=null && (UI==null || !UI.Settings.ReducedMotion) &&
            (VictoryPhase==VictoryPresentationState.Stage.Death || VictoryPhase==VictoryPresentationState.Stage.FadeOut)) {
            // The last shot usually points at the floor. A gentle stationary
            // turn keeps the collapse in frame without moving the hunter.
            Vector3 focus=modelView!=null ? modelView.DeathFocusPoint : state.Enemy+Vector3.up*1.1f;
            Vector3 towards=focus-Eye.transform.position;
            if(towards.sqrMagnitude>.001f)
                Eye.transform.rotation=Quaternion.Slerp(victoryCameraStart,Quaternion.LookRotation(towards),
                    Mathf.SmoothStep(0,1,victoryPresentation.Elapsed/1.2f));
        }
        UI?.RefreshVictorySequence();
    }
}
