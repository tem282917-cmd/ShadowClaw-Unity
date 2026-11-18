using UnityEngine;

/// <summary>Built-in pipeline image brightness; never changes lights, exposure probes, or shadow geometry.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class ShiyinCameraGrade : MonoBehaviour
{
    const string ShaderPath = "Shiyin/SettingsBrightness";
    static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    float brightness = 1f;
    Material grade;
    bool reportedUnavailable;

    public float Brightness
    {
        get => brightness;
        set => brightness = ShiyinSettings.FiniteClamp(value, .7f, 1.3f, 1f);
    }

    public bool ShaderAvailable
    {
        get
        {
            var shader = Resources.Load<Shader>(ShaderPath);
            return shader != null && shader.isSupported;
        }
    }

    public static ShiyinCameraGrade ApplyTo(Camera camera, float brightness)
    {
        if (camera == null) return null;
        var effect = camera.GetComponent<ShiyinCameraGrade>();
        if (effect == null) effect = camera.gameObject.AddComponent<ShiyinCameraGrade>();
        effect.Brightness = brightness;
        // Authored atmosphere is already graded before this display-only adjustment.
        // At its neutral setting, avoid an extra full-screen copy on the castle camera.
        bool authoredAtmosphere = camera.GetComponent<Chapter01.Rendering.Chapter01NightPostProcess>() != null;
        effect.enabled = !authoredAtmosphere || !Mathf.Approximately(effect.Brightness, 1f);
        return effect;
    }

    bool EnsureMaterial()
    {
        if (grade != null) return true;
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported)
        {
            if (!reportedUnavailable)
            {
                Debug.LogWarning("Shiyin brightness shader is unavailable; rendering the original image.", this);
                reportedUnavailable = true;
            }
            return false;
        }
        grade = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return true;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (Mathf.Approximately(brightness, 1f) || !EnsureMaterial())
        {
            Graphics.Blit(source, destination);
            return;
        }
        grade.SetFloat(BrightnessId, brightness);
        Graphics.Blit(source, destination, grade);
    }

    void OnDisable()
    {
        if (grade == null) return;
        if (Application.isPlaying) Destroy(grade);
        else DestroyImmediate(grade);
        grade = null;
    }
}
