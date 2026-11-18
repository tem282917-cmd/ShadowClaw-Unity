using UnityEngine;

// The prefab's edit-time silhouette is stripped from builds by EditorOnly.
// In editor Play mode the actor creates its animated runtime presentation.
public sealed class EncounterAuthoringPreview : MonoBehaviour
{
    void Awake() { gameObject.SetActive(false); }
}
