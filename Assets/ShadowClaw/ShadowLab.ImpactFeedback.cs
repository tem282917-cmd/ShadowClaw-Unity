using UnityEngine;

public sealed partial class ShadowLab {
    const float ShakeDuration=.22f;
    float impactShake,hurtSoundCooldown;
    AudioClip hunterHitClip;
    public float HurtIntensity => Mathf.Clamp01(hurtFlash/.34f);
    internal float CameraImpactRoll {
        get {
            if(impactShake<=0 || UI?.Settings.ReducedMotion==true)return 0;
            float remaining=impactShake/ShakeDuration;
            return .68f*remaining*remaining*Mathf.Sin((1-remaining)*Mathf.PI*3);
        }
    }
    internal void ShakeOnImpact() {
        if(!GameplayPaused && UI?.Settings.ReducedMotion!=true)impactShake=ShakeDuration;
    }
    void HunterHitFeedback() {
        hurtFlash=.34f;
        if(hurtSoundCooldown>0)return;
        hurtSoundCooldown=.30f;
        if(hunterHitClip==null)hunterHitClip=Resources.Load<AudioClip>("GameplaySfx/HunterHurt");
        eye.GetComponent<ShiyinAudio>()?.PlayEffect(hunterHitClip,.66f);
    }
}
