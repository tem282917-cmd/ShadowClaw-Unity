using UnityEditor;
using UnityEngine;

/// <summary>Explicit authoring actions. No scene is changed by import or startup.</summary>
public static class EncounterAuthoring
{
    static void Create<T>(MenuCommand command,string name) where T:Component {
        var obj=EncounterPrefabBuilder.LoadOrCreate<T>(name);
        GameObjectUtility.SetParentAndAlign(obj,command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(obj,"Create "+name);Selection.activeGameObject=obj;
    }
    [MenuItem("GameObject/ShadowClaw/地影群",false,20)]
    static void Swarm(MenuCommand command) {Create<GroundShadowSwarm>(command,"地影群");}
    [MenuItem("GameObject/ShadowClaw/窃火侍僧",false,21)]
    static void Acolyte(MenuCommand command) {Create<FireThiefAcolyte>(command,"窃火侍僧");}
    [MenuItem("GameObject/ShadowClaw/守灯者",false,22)]
    static void Guardian(MenuCommand command) {Create<FixedLanternGuardian>(command,"守灯者");}
    [MenuItem("GameObject/ShadowClaw/可交互灯芯",false,23)]
    static void Lamp(MenuCommand command) {Create<EncounterLantern>(command,"可交互灯芯");}
    [MenuItem("GameObject/ShadowClaw/遭遇触发范围",false,24)]
    static void Trigger(MenuCommand command) {Create<AuthoredEncounterTrigger>(command,"遭遇触发范围");}
}
