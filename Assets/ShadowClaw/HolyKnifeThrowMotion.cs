using UnityEngine;

// One gameplay clock owns the draw, release and recovery. No animation event
// can spend another item or fire again when the game resumes from a pause.
public sealed class HolyKnifeThrowMotion
{
    public const float ReleaseTime=.22f, Duration=.62f;
    public float Age {get;private set;}
    public bool Active {get;private set;}
    public bool Released {get;private set;}
    public float Progress=>Mathf.Clamp01(Age/Duration);
    public bool Begin() {
        if(Active)return false;
        Age=0;Released=false;Active=true;return true;
    }
    public bool Advance(float dt) {
        if(!Active || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return false;
        Age=Mathf.Min(Duration,Age+dt);
        bool release=!Released && Age>=ReleaseTime;
        if(release)Released=true;
        if(Age>=Duration)Active=false;
        return release;
    }
    public void Reset() {Age=0;Active=Released=false;}
    public static Vector3 Position(float age) {
        var drawn=new Vector3(-.43f,-.22f,.54f);
        var pulled=new Vector3(-.48f,-.08f,.38f);
        var release=new Vector3(-.29f,-.075f,.78f);
        if(age<.10f)return Vector3.Lerp(new Vector3(-.55f,-.62f,.36f),drawn,Mathf.SmoothStep(0,1,age/.1f));
        if(age<.15f)return Vector3.Lerp(drawn,pulled,Mathf.SmoothStep(0,1,(age-.1f)/.05f));
        return Vector3.Lerp(pulled,release,Mathf.SmoothStep(0,1,(age-.15f)/(.22f-.15f)));
    }
    public static Quaternion Rotation(float age) {
        if(age<.15f)return Quaternion.Slerp(Quaternion.Euler(-64,-16,18),Quaternion.Euler(-98,-18,24),Mathf.SmoothStep(0,1,age/.15f));
        return Quaternion.Slerp(Quaternion.Euler(-98,-18,24),Quaternion.Euler(-4,3,-12),Mathf.SmoothStep(0,1,(age-.15f)/.07f));
    }
}
