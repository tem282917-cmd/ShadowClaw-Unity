using System;
using System.Collections.Generic;
using UnityEngine;

// Shortcuts belong to this play session. Register here so input and the in-game
// shortcut legend always use the same bindings, including when play is frozen.
internal sealed class DeveloperControls {
    readonly Dictionary<KeyCode,Action> commands=new Dictionary<KeyCode,Action>();
    readonly List<KeyCode> keys=new List<KeyCode>();
    readonly HashSet<string> pauseReasons=new HashSet<string>();
    float previousTimeScale=1f;
    public bool Frozen {get; private set;}
    public bool HealthLocked {get; private set;}
    public bool HudHidden {get; private set;}
    public bool Paused => pauseReasons.Count>0;
    public string Help {get; private set;}="";

    public DeveloperControls(Action<bool> setHealthLock) {
        Register(KeyCode.F1,"冻结 / 继续",()=>SetFrozen(!Frozen));
        Register(KeyCode.F2,"锁血",()=>{
            HealthLocked=!HealthLocked;
            setHealthLock(HealthLocked);
        });
        Register(KeyCode.F3,"隐藏界面",()=>SetHudHidden(!HudHidden));
    }
    void Register(KeyCode key,string label,Action execute) {
        commands.Add(key,execute);
        keys.Add(key);
        Help+=(keys.Count>1 ? "    " : "")+key+" "+label;
    }
    public bool HandleKey(KeyCode key) {
        if(!commands.TryGetValue(key,out var execute))return false;
        execute();
        return true;
    }
    public bool Poll() {
        bool handled=false;
        foreach(var key in keys) if(Input.GetKeyDown(key)) handled|=HandleKey(key);
        return handled;
    }
    public void SetFrozen(bool value) {
        if(Frozen==value)return;
        Frozen=value;
        SetPauseReason("developer-freeze",value);
    }
    public void SetHudHidden(bool value) { HudHidden=value; }
    public void SetPauseReason(string reason,bool value) {
        if(value) {
            if(!pauseReasons.Add(reason))return;
            if(pauseReasons.Count==1)previousTimeScale=Time.timeScale>0 ? Time.timeScale : 1f;
        } else if(!pauseReasons.Remove(reason))return;
        Time.timeScale=Paused ? 0f : previousTimeScale;
    }
    public void ClearPauseReasons() {
        Frozen=false;pauseReasons.Clear();Time.timeScale=previousTimeScale;
    }
}
