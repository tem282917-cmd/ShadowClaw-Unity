using UnityEngine;

// A read-only presentation snapshot of the live encounter, never a second combat model.
public struct ShiyinHudSnapshot {
    public int Health, MaxHealth, Rounds, HolyKnives, HolyKnifeCapacity, LitLamps, LampCapacity;
    public bool HasGuardianRelic;
    public int GuardianRelicCharges, GuardianRelicRelights;
    public float BossHealth01, WeaponProgress, ConditionTime, SprintStamina01;
    public Bolt Loaded, Target;
    public Bolt? Queued;
    public string BossName, BossStatus, BossStatusIcon, WeaponLabel, Condition;
    public string NoticeTitle, NoticeBody, Toast, RecoveryStatus;
    public CombatGuidance CombatGuides;
    public bool Recovering, Sprinting, CombatActive, SprintExhausted;
    public bool EncounterStarted, WeaponBusy, DangerNotice, Hit, LowHealth, Frozen, HealthLocked, Hidden;
}
