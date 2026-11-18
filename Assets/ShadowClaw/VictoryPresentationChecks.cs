using System;
using System.Collections.Generic;

public static class VictoryPresentationChecks
{
    public static List<string> Run() {
        var rows = new List<string>();
        void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); rows.Add("PASS " + name); }
        var flow = new VictoryPresentationState();
        Require(!flow.Active && flow.Phase == VictoryPresentationState.Stage.Idle, "victory flow starts dormant");
        flow.Begin(3, 2, 5, 2, 2);
        Require(flow.Active && flow.Phase == VictoryPresentationState.Stage.Death && flow.BlackOpacity == 0,
            "boss death remains visible before blackout");
        flow.Advance(3);
        Require(flow.Phase == VictoryPresentationState.Stage.FadeOut && flow.BlackOpacity == 0,
            "blackout starts only after death performance");
        flow.Advance(1);
        Require(Math.Abs(flow.BlackOpacity - .5f) < .0001f && !flow.MenuReady, "death scene fades smoothly halfway to black");
        flow.Advance(1.5f);
        Require(flow.Phase == VictoryPresentationState.Stage.Story && flow.StoryIndex == 0 && flow.BlackOpacity == 1 && flow.StoryOpacity > .5f,
            "first draft story paragraph appears on full black");
        flow.Advance(5);
        Require(flow.StoryIndex == 1 && flow.StoryOpacity > .5f && !flow.MenuReady, "story progresses to its second paragraph before menu");
        flow.Advance(4.5f);
        Require(flow.Phase == VictoryPresentationState.Stage.MenuReveal && flow.MenuReady && flow.BlackOpacity == 1 && flow.StoryOpacity == 0,
            "menu handoff occurs under complete black after the story");
        flow.Advance(1);
        Require(Math.Abs(flow.BlackOpacity - .5f) < .0001f && flow.Active, "menu gradually reveals beneath the same blackout");
        flow.Advance(1);
        Require(!flow.Active && flow.Phase == VictoryPresentationState.Stage.Complete && flow.BlackOpacity == 0,
            "completed ending releases the menu without a result popup");
        flow.Reset();
        Require(!flow.Active && !flow.MenuReady && flow.BlackOpacity == 0, "restart clears completion and transition opacity");
        flow.Begin(3, 2, 5, 2, 2); flow.Advance(float.NaN); flow.Advance(-2);
        Require(flow.Elapsed == 0, "invalid presentation elapsed time cannot corrupt the story");
        flow.Advance(100);
        Require(!flow.Active && flow.MenuReady && flow.BlackOpacity == 0, "large time step still reaches a completed menu handoff");
        return rows;
    }
}
