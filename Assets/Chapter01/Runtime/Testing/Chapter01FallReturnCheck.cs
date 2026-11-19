using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed partial class ShadowLab {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartFallReturnCheck() {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-check-village-fall")<0)return;
        var lab=FindFirstObjectByType<ShadowLab>();
        lab.StartCoroutine(lab.CheckFallReturn());
    }

    IEnumerator CheckFallReturn() {
        yield return null;
        try {
            enabled=false;
            var objects=gameObject.scene.GetRootGameObjects()
                .SelectMany(root=>root.GetComponentsInChildren<Transform>(true)).ToArray();
            var backdrop=objects.Single(t=>t.name=="VillageBackdrop");
            var settlement=objects.Where(t=>t.name.Contains("__Village_") || t.name.StartsWith("Village_",StringComparison.Ordinal)).ToArray();
            if(settlement.Length>0)throw new Exception("Unexpected village object: "+settlement[0].name);
            int ridges=backdrop.GetComponentsInChildren<MeshFilter>().Count(f=>f.sharedMesh!=null && f.name.StartsWith("E04",StringComparison.Ordinal));
            if(ridges==0)throw new Exception("Horizon meshes missing");

            var start=hall.HunterStart;
            float threshold=start.y-hall.FallDepthBelowSpawn;
            void Position(Vector3 feet) {
                playerBody.enabled=false;
                playerBody.transform.position=feet;
                playerBody.enabled=true;
                state.Player=feet;
            }
            void Fall() {
                Jump();state.StepMovement(.1f);
                Position(new Vector3(start.x+30,threshold-1,start.z+11));
                int health=HunterHealth, knives=HolyKnifeCount;
                Tick(.016f);
                if(Vector3.Distance(playerBody.transform.position,start)>.3f || state.JumpHeight!=0 ||
                   Vector3.Distance(state.Player,playerBody.transform.position)>.001f ||
                   Vector3.Distance(Eye.transform.position,state.Player+Vector3.up*1.65f)>.01f ||
                   HunterHealth!=health || HolyKnifeCount!=knives)
                    throw new Exception("Return did not preserve player state at spawn");
                Tick(.016f);
                if(state.JumpHeight!=0 || Vector3.Distance(state.Player,start)>.3f)throw new Exception("Unstable landing");
            }
            void EnterArena() {
                state.Player=PlaceBody(playerBody,hall.FogGate.FloorCenter+hall.FogGate.Inward*(hall.FogGate.EntryClearance+1));
                Tick(.016f);
                if(!EncounterStarted || !FinalRoomIsolated)throw new Exception("Could not enter the final arena");
            }
            Position(new Vector3(start.x+30,threshold+.5f,start.z+11));
            Tick(.016f);
            if(Vector3.Distance(state.Player,start)<10)throw new Exception("Returned above the detection plane");
            DamageHunter(15);
            Fall();
            EnterArena();
            Fall();
            if(EncounterStarted || FinalRoomIsolated || hall.FogGate.Sealed)throw new Exception("Arena stayed sealed after return");
            EnterArena();
            Debug.Log($"VILLAGE_FALL_PASS village={settlement.Length} ridges={ridges} threshold={threshold} spawn={start}");
            Application.Quit(0);
        } catch(Exception error) {
            Debug.LogException(error);
            Application.Quit(1);
        }
    }
}
