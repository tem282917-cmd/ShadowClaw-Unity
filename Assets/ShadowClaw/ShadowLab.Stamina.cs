using UnityEngine;

public sealed partial class ShadowLab
{
    [Header("Combat sprint stamina")]
    [Min(1)] public float SprintStaminaMax = 100;
    [Min(0)] public float SprintStaminaDrainPerSecond = 100f / 6f;
    [Min(0)] public float SprintStaminaRecoveryDelay = 1;
    [Min(0)] public float SprintStaminaRecoveryPerSecond = 25;
    [Range(.01f, 1)] public float SprintStaminaRestartFraction = .25f;
    [Min(0)] public float MinorCombatExitDelay = 3;
    readonly SprintStaminaState sprintStamina = new SprintStaminaState();
    float minorCombatRelease;
    bool walkedSinceStaminaTick;

    public bool CombatActive => IsCombatActive(EngagedMinorKinds);
    bool IsCombatActive(CombatGuidance engaged) => GameplayReady && !finished &&
        (engaged!=CombatGuidance.None || minorCombatRelease>0 || EncounterStarted || ActiveGuardian!=null);
    public float SprintStamina => CombatActive ? sprintStamina.Remaining : Mathf.Max(1, SprintStaminaMax);
    public float SprintStamina01 => CombatActive ? Mathf.Clamp01(sprintStamina.Remaining / sprintStamina.Maximum) : 1;
    public bool SprintExhausted => CombatActive && sprintStamina.Exhausted;

    void ConfigureStamina() => sprintStamina.Configure(SprintStaminaMax, SprintStaminaDrainPerSecond,
        SprintStaminaRecoveryPerSecond, SprintStaminaRecoveryDelay, SprintStaminaRestartFraction);
    void ResetSprintStamina()
    {
        ConfigureStamina();
        sprintStamina.Reset();
        minorCombatRelease = 0;
        walkedSinceStaminaTick = sprinting = false;
    }
    void SyncStaminaCombat(float dt)
    {
        ConfigureStamina();
        var engagedKinds=EngagedMinorKinds;
        minorCombatRelease = !finished && engagedKinds!=CombatGuidance.None ? Mathf.Max(0, MinorCombatExitDelay)
            : Mathf.Max(0, minorCombatRelease - dt);
        bool inCombat=IsCombatActive(engagedKinds);
        firstEncounterGuides=inCombat ? firstEncounterGuides | (engagedKinds & ~seenMinorKinds) : CombatGuidance.None;
        seenMinorKinds|=engagedKinds;
        sprintStamina.SetCombat(inCombat);
    }
    void TickStamina(float dt)
    {
        SyncStaminaCombat(dt);
        sprintStamina.Tick(dt);
        if (!walkedSinceStaminaTick) sprinting = false;
        walkedSinceStaminaTick = false;
    }
}
