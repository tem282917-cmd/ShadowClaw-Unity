using UnityEngine;

public sealed partial class ShiyinUI
{
    const float ProtectionHintSeconds=3.2f;
    const float ProtectionHintRepeatInterval=.65f;
    string lastProtectionHint;
    float nextProtectionHint;

    // Reuse the existing single combat-toast slot: no stacked notifications or
    // new black panel. Rapid hits keep the current hint instead of retriggering it.
    bool TryShowAcolyteProtectionFeedback(string feedback) {
        if(!AcolyteProtectionFeedback.IsProtectedHit(feedback))return false;
        hitUntil=0; // A blocked shadow hit must not flash the successful-hit reticle.
        float now=Time.time;
        if(toast==feedback && lastProtectionHint==feedback && now<nextProtectionHint && now<toastUntil)return true;
        toast=lastProtectionHint=feedback;
        toastUntil=now+ProtectionHintSeconds;
        nextProtectionHint=now+ProtectionHintRepeatInterval;
        return true;
    }
}
