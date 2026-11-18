using System;
using System.Collections.Generic;
using UnityEngine;

public static class HallPresentationChecks
{
    static void Check(bool condition,string label,List<string> rows) {
        if(!condition)throw new InvalidOperationException("FAIL hall presentation: "+label);
        rows.Add("PASS hall presentation: "+label);
    }
    public static string[] Run() {
        var rows=new List<string>();
        var root=new GameObject("Temporary fog gate check") {hideFlags=HideFlags.HideAndDontSave};
        root.SetActive(false);
        try {
            var gate=root.AddComponent<BossFogGate>();gate.Width=8;gate.Height=12;
            gate.Thickness=.6f;gate.EntryClearance=.7f;
            gate.Barrier=root.AddComponent<BoxCollider>();
            root.transform.SetPositionAndRotation(new Vector3(3,68,83.5f),Quaternion.Euler(0,25,0));
            Vector3 World(float x,float z,float y=1) => root.transform.TransformPoint(new Vector3(x,y,z));
            gate.ResetGate();
            Check(!gate.Sealed && gate.MistOpacity==1,"the approach is traversable with full mist",rows);
            Check(!gate.IsInside(World(0,-.5f)) && !gate.IsInside(World(0,.69f)) && gate.IsInside(World(0,.71f)),
                "battle entry waits for the complete player capsule to clear the plane",rows);
            Check(!gate.IsInside(World(4.1f,1)) && !gate.IsInside(World(-4.1f,30)) && gate.IsInside(World(3.9f,30)),
                "entry is confined to the doorway width and accepts a restored interior position",rows);
            Check(!gate.IsInside(World(0,1,-1)) && !gate.IsInside(World(0,1,13)),
                "positions on another floor do not start the battle",rows);
            gate.SetEncounterState(true,false,false);
            Check(gate.Sealed,"active combat physically seals the visible gate",rows);
            gate.SetEncounterState(true,true,true);
            Check(!gate.Sealed,"victory immediately restores the exit collision",rows);
            gate.AdvancePresentation(.6f,true);
            Check(Mathf.Abs(gate.MistOpacity-.5f)<.0001f,"victory mist fades while finished gameplay is paused",rows);
            gate.AdvancePresentation(.6f,true);
            Check(gate.MistOpacity==0,"victory mist disappears after 1.2 seconds",rows);
            gate.ResetGate();
            Check(!gate.Sealed && gate.MistOpacity==1,"restart restores an open mist gate",rows);
            gate.SetEncounterState(true,false,false);gate.SetEncounterState(true,true,false);
            Check(!gate.Sealed && gate.MistOpacity==1,"defeat releases the seal for restarting",rows);
        } finally {
            if(Application.isPlaying)UnityEngine.Object.Destroy(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }
        return rows.ToArray();
    }
}
