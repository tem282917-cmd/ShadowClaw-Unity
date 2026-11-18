// Only a confirmed hit on an acolyte's protected shadow asks the player to
// break its lamp link. A lit lamp is still being stolen; purple marks a lamp
// whose effective light is already out and must be restored by shooting it.
public static class AcolyteProtectionFeedback
{
    public const string PurpleFlame="连灯护佑 · 先射关联灯，熄灭紫色火焰";
    public const string StealingFire="连灯护佑 · 先射击关联灯，打断偷火";

    public static string For(ShadowEncounterActor target) {
        if(!(target is FireThiefAcolyte acolyte) || !acolyte.ProtectedByLamp)return null;
        return acolyte.Empowered ? PurpleFlame : StealingFire;
    }
    public static bool IsProtectedHit(string feedback) => feedback==PurpleFlame || feedback==StealingFire;
}
