using System;
using System.Collections.Generic;

public static class StaminaChecks
{
    public static string[] Run()
    {
        var rows = new List<string>();
        void Check(bool valid, string label) {
            if (!valid) throw new InvalidOperationException("FAIL stamina: " + label);
            rows.Add("PASS stamina: " + label);
        }
        bool Near(float value, float target) => Math.Abs(value - target) < .002f;
        var state = new SprintStaminaState();
        state.SpendSprint(600); state.Tick(600);
        Check(Near(state.Remaining, 100) && Near(state.AvailableSprintTime(600), 600),
            "exploration permits unlimited sprint time and always stays full");
        state.SetCombat(true);
        for (int i = 0; i < 60; i++) { state.SpendSprint(.1f); state.Tick(.1f); }
        Check(Near(state.Remaining, 0) && state.Exhausted && state.AvailableSprintTime(.1f) == 0,
            "six seconds of combat sprint exhaust the tank without same-frame regeneration");
        state.Tick(.99f);
        Check(state.Remaining == 0 && state.Exhausted, "stopping for less than one second restores nothing");
        state.Tick(.51f);
        Check(Near(state.Remaining, 12.5f) && state.Exhausted && state.AvailableSprintTime(.1f) == 0,
            "holding sprint below the restart threshold cannot repeatedly spend tiny recovered amounts");
        state.Tick(.5f);
        Check(Near(state.Remaining, 25) && !state.Exhausted && Near(state.AvailableSprintTime(.1f), .1f),
            "a quarter tank releases the exhausted latch");
        state.SpendSprint(.3f); state.Tick(.3f);
        Check(Near(state.Remaining, 20), "resumed sprint spends only its actual movement duration");
        state.Tick(1); state.Tick(4);
        Check(Near(state.Remaining, 100), "one second of rest plus four seconds restores a full tank");

        state.Reset(); state.SetCombat(true); state.SpendSprint(5.9f); state.Tick(5.9f);
        float lastSprint = state.AvailableSprintTime(.4f);
        Check(Near(lastSprint, .1f), "a step crossing exhaustion receives only its affordable sprint fraction");
        state.SpendSprint(lastSprint); state.Tick(.4f); state.Tick(.7f);
        Check(Near(state.Remaining, 0), "the walking remainder of an exhaustion step counts toward recovery delay");
        state.Tick(.4f);
        Check(Near(state.Remaining, 10), "recovery starts exactly after the remaining delay expires");
        state.SetCombat(false);
        Check(Near(state.Remaining, 100) && !state.Exhausted && state.RecoveryRemaining == 0,
            "leaving combat instantly refills and clears exhaustion and pending movement");

        state.SetCombat(true); state.SpendSprint(1);
        state.Tick(.4f); state.Tick(.6f); state.Tick(.5f);
        Check(Near(state.Remaining, 100 - 100f / 6f),
            "subdividing Tick never turns already-recorded movement time into early recovery");
        state.Reset(); state.SetCombat(true); state.SpendSprint(2);
        state.SpendSprint(-1); state.Tick(0); state.Tick(-1); state.Tick(float.NaN);
        Check(Near(state.Remaining, 100 - 200f / 6f), "zero and invalid time cannot spend or regenerate stamina");
        state.Reset();
        Check(Near(state.Remaining, 100) && !state.Combat && !state.Exhausted,
            "reset discards all combat recovery debt");

        state.Configure(200, 20, 40, .5f, .5f); state.SetCombat(true);
        state.SpendSprint(10); state.Tick(10); state.Tick(.5f); state.Tick(2);
        Check(Near(state.Remaining, 80) && state.Exhausted, "custom drain, delay, recovery and restart threshold are honored");
        state.Tick(.5f);
        Check(Near(state.Remaining, 100) && !state.Exhausted, "custom fifty-percent restart unlocks at the configured boundary");
        state.Tick(1000);
        Check(Near(state.Remaining, 200), "long idle time never overfills the configured maximum");

        float Simulate(float dt) {
            var simulated = new SprintStaminaState(); simulated.SetCombat(true);
            int frames = (int)Math.Round(4 / dt);
            for (int i = 0; i < frames; i++) { simulated.SpendSprint(dt); simulated.Tick(dt); }
            simulated.Tick(1.5f); return simulated.Remaining;
        }
        Check(Math.Abs(Simulate(.02f) - Simulate(.1f)) < .003f,
            "equivalent sprint and recovery durations remain stable across frame sizes");
        return rows.ToArray();
    }
}
