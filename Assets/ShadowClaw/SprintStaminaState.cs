using System;

// Simulation time is supplied by the caller. Movement spends immediately; the
// matching Tick retires that movement time before advancing idle recovery.
public sealed class SprintStaminaState
{
    public float Maximum { get; private set; } = 100;
    public float DrainPerSecond { get; private set; } = 100f / 6f;
    public float RecoveryPerSecond { get; private set; } = 25;
    public float RecoveryDelay { get; private set; } = 1;
    public float RestartFraction { get; private set; } = .25f;
    public float Remaining { get; private set; } = 100;
    public bool Exhausted { get; private set; }
    public bool Combat { get; private set; }
    public float RecoveryRemaining { get; private set; }
    float pendingSprintTime;

    static float Finite(float value, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    public void Configure(float maximum, float drain, float recovery, float delay, float restartFraction)
    {
        float nextMaximum = Math.Max(1, Finite(maximum, 100));
        Remaining = Math.Min(nextMaximum, Remaining / Maximum * nextMaximum);
        Maximum = nextMaximum;
        DrainPerSecond = Math.Max(0, Finite(drain, 100f / 6f));
        RecoveryPerSecond = Math.Max(0, Finite(recovery, 25));
        RecoveryDelay = Math.Max(0, Finite(delay, 1));
        RestartFraction = Math.Max(.01f, Math.Min(1, Finite(restartFraction, .25f)));
        UnlockIfRecovered();
    }
    public void Reset()
    {
        Remaining = Maximum;
        RecoveryRemaining = pendingSprintTime = 0;
        Exhausted = Combat = false;
    }
    public void SetCombat(bool value)
    {
        if (!value) Reset();
        else Combat = true;
    }
    public float AvailableSprintTime(float duration)
    {
        if (duration <= 0 || float.IsNaN(duration) || float.IsInfinity(duration)) return 0;
        if (!Combat) return duration;
        if (Exhausted || Remaining <= 0) return 0;
        return DrainPerSecond > 0 ? Math.Min(duration, Remaining / DrainPerSecond) : duration;
    }
    public void SpendSprint(float duration)
    {
        if (!Combat) return;
        duration = AvailableSprintTime(duration);
        if (duration <= 0) return;
        Remaining = Math.Max(0, Remaining - duration * DrainPerSecond);
        pendingSprintTime += duration;
        RecoveryRemaining = RecoveryDelay;
        if (Remaining <= .0001f) { Remaining = 0; Exhausted = true; }
    }
    public void Tick(float duration)
    {
        if (duration <= 0 || float.IsNaN(duration) || float.IsInfinity(duration)) return;
        if (!Combat) { Reset(); return; }
        // Walk and Tick normally cover the same frame. A sprint frame must not
        // also count toward its recovery delay, even when Tick is subdivided.
        float active = Math.Min(duration, pendingSprintTime);
        pendingSprintTime = Math.Max(0, pendingSprintTime - active);
        float idle = duration - active;
        float waiting = Math.Min(idle, RecoveryRemaining);
        RecoveryRemaining -= waiting;
        Remaining = Math.Min(Maximum, Remaining + (idle - waiting) * RecoveryPerSecond);
        UnlockIfRecovered();
    }
    void UnlockIfRecovered()
    {
        if (Remaining + .0001f >= Maximum * RestartFraction) Exhausted = false;
    }
}
