using UnityEngine;
using UnityEngine.UI;
using W = ShiyinWidgets;

public sealed partial class ShiyinUI
{
    RectTransform victoryOverlay, storyFrame;
    Image victoryBlack;
    Text victoryStory;

    public void BeginVictorySequence() {
        if (!initialized) return;
        history.Clear(); ConfirmationKey = ""; ResetNotices();
        Lab.Developer.SetHudHidden(false);
        SetView("ending");
        RefreshVictorySequence();
    }
    public void ClearVictorySequence() {
        if (victoryOverlay != null) victoryOverlay.gameObject.SetActive(false);
    }
    public void RefreshVictorySequence() {
        if (!initialized || Lab.VictoryPhase == VictoryPresentationState.Stage.Idle) { ClearVictorySequence(); return; }
        if (Lab.VictoryMenuReady && CurrentView == "ending") {
            // The menu replaces the combat view only while the screen is opaque.
            history.Clear(); ConfirmationKey = ""; sessionStarted = false;
            Lab.Developer.SetFrozen(false); ResetNotices(); SetView("menu");
        }
        if (!Lab.VictoryPresentationActive) { ClearVictorySequence(); return; }
        if (victoryOverlay == null) {
            victoryOverlay = W.Stretch(vRoot, "Victory story transition");
            victoryBlack = W.Image(W.Stretch(victoryOverlay, "Victory blackout"), Color.black, true);
            storyFrame = W.Center(victoryOverlay, "Ending story safe area", 1160, 240, 0, -65);
            victoryStory = W.Text(storyFrame, "", 40, 0, 1080, 240, 30, W.Ink, true, TextAnchor.MiddleCenter);
            victoryStory.gameObject.name = "Ending story subtitle";
        }
        victoryOverlay.gameObject.SetActive(true); victoryOverlay.SetAsLastSibling();
        UiCanvas.enabled = true;
        victoryBlack.color = new Color(0, 0, 0, Lab.VictoryBlackOpacity);
        storyFrame.anchoredPosition = new Vector2(0, Lab.ShowingDispersalSubtitle ? -340 : -65);
        victoryStory.fontSize = VSize(30);
        victoryStory.text = Lab.VictoryStoryText;
        victoryStory.color = W.Alpha(W.Ink, Lab.VictoryStoryOpacity);
    }
}
