using System;
using UnityEngine;

/// <summary>Player preferences for the Shiyin interface. Construction never reads or writes preferences.</summary>
public sealed class ShiyinSettings
{
    public const string DefaultPreferencePrefix = "Shiyin";
    public readonly string PreferencePrefix;

    public float TextScale = 1f;
    public bool Contrast;
    public bool ReducedMotion;
    public bool Tutorial = true;
    public float Master = .8f;
    public float Effects = .9f;
    public float Music = .55f;
    public float Narration = .85f;
    public bool NarrationSubtitles = true;
    public float Brightness = 1f;
    public int DisplayMode;
    public float Fov = 72f;
    public float Sensitivity = 2f;
    public bool InvertY;

    bool initializedDisplay;

    // An isolated prefix lets checks exercise persistence without modifying the player's settings.
    public ShiyinSettings(string preferencePrefix = DefaultPreferencePrefix)
    {
        string prefix = string.IsNullOrWhiteSpace(preferencePrefix)
            ? DefaultPreferencePrefix : preferencePrefix.Trim().TrimEnd('.');
        PreferencePrefix = string.IsNullOrEmpty(prefix) ? DefaultPreferencePrefix : prefix;
    }

    string Key(string name) => PreferencePrefix + "." + name;

    public void ResetDefaults()
    {
        TextScale = 1f;
        Contrast = ReducedMotion = InvertY = false;
        Tutorial = true;
        Master = .8f;
        Effects = .9f;
        Music = .55f;
        Narration = .85f; NarrationSubtitles=true;
        Brightness = 1f;
        DisplayMode = 0;
        Fov = 72f;
        Sensitivity = 2f;
    }

    public void Load()
    {
        ResetDefaults();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (PreferencePrefix == DefaultPreferencePrefix)
            ImportPreviousWindowsPreferences();
#endif
        TextScale = PlayerPrefs.GetFloat(Key(nameof(TextScale)), TextScale);
        Contrast = ReadBool(nameof(Contrast), Contrast);
        ReducedMotion = ReadBool(nameof(ReducedMotion), ReducedMotion);
        Tutorial = ReadBool(nameof(Tutorial), Tutorial);
        Master = PlayerPrefs.GetFloat(Key(nameof(Master)), Master);
        Effects = PlayerPrefs.GetFloat(Key(nameof(Effects)), Effects);
        Music = PlayerPrefs.GetFloat(Key(nameof(Music)), Music);
        Narration = PlayerPrefs.GetFloat(Key(nameof(Narration)), Narration);
        NarrationSubtitles = ReadBool(nameof(NarrationSubtitles), NarrationSubtitles);
        Brightness = PlayerPrefs.GetFloat(Key(nameof(Brightness)), Brightness);
        DisplayMode = PlayerPrefs.GetInt(Key(nameof(DisplayMode)), DisplayMode);
        Fov = PlayerPrefs.GetFloat(Key(nameof(Fov)), Fov);
        Sensitivity = PlayerPrefs.GetFloat(Key(nameof(Sensitivity)), Sensitivity);
        InvertY = ReadBool(nameof(InvertY), InvertY);
        Validate();
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [System.Runtime.InteropServices.DllImport("advapi32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    static extern int RegGetValueW(IntPtr root, string subKey, string value, uint flags,
        out uint type, [System.Runtime.InteropServices.Out] byte[] data, ref uint size);

    void ImportPreviousWindowsPreferences()
    {
        string marker = Key("ImportedFromShadowClaw");
        if (Application.productName == "ShadowClaw" || PlayerPrefs.HasKey(marker)) return;
        string registryRoot = Application.isEditor ? @"Software\Unity\UnityEditor" : @"Software";
        string previousPath = registryRoot + @"\" + Application.companyName + @"\ShadowClaw";
        string[] previousSettings =
        {
            nameof(TextScale), nameof(Contrast), nameof(ReducedMotion), nameof(Tutorial),
            nameof(Master), nameof(Effects), nameof(Music), nameof(Narration), nameof(NarrationSubtitles),
            nameof(Brightness), nameof(DisplayMode), nameof(Fov), nameof(Sensitivity), nameof(InvertY)
        };
        var currentUser = new IntPtr(unchecked((int)0x80000001));
        var data = new byte[8];
        foreach (string setting in previousSettings)
        {
            string key = Key(setting);
            if (PlayerPrefs.HasKey(key)) continue;
            // Windows Unity appends the unsigned DJB2-XOR hash to each PlayerPrefs value name.
            uint hash = 5381;
            foreach (char character in key) hash = unchecked(hash * 33 ^ character);
            string storedName = key + "_h" + hash.ToString(System.Globalization.CultureInfo.InvariantCulture);
            uint size = (uint)data.Length;
            int status = RegGetValueW(currentUser, previousPath, storedName, 0xffff, out uint type, data, ref size);
            if (status == 2 || status == 3) continue; // Missing value or missing old product key.
            if (status != 0)
            {
                Debug.LogWarning("Could not import previous ShadowClaw preferences (Windows error " + status + ").");
                return;
            }
            // The previous Windows player used REG_DWORD for both four-byte ints and eight-byte doubles.
            if (type == 4 && size == 8) PlayerPrefs.SetFloat(key, (float)BitConverter.ToDouble(data, 0));
            else if (type == 4 && size == 4) PlayerPrefs.SetInt(key, BitConverter.ToInt32(data, 0));
        }
        PlayerPrefs.SetInt(marker, 1);
        PlayerPrefs.Save();
    }
#endif

    bool ReadBool(string name, bool fallback)
    {
        int stored = PlayerPrefs.GetInt(Key(name), fallback ? 1 : 0);
        return stored == 0 ? false : stored == 1 ? true : fallback;
    }

    public void Save()
    {
        Validate();
        PlayerPrefs.SetFloat(Key(nameof(TextScale)), TextScale);
        PlayerPrefs.SetInt(Key(nameof(Contrast)), Contrast ? 1 : 0);
        PlayerPrefs.SetInt(Key(nameof(ReducedMotion)), ReducedMotion ? 1 : 0);
        PlayerPrefs.SetInt(Key(nameof(Tutorial)), Tutorial ? 1 : 0);
        PlayerPrefs.SetFloat(Key(nameof(Master)), Master);
        PlayerPrefs.SetFloat(Key(nameof(Effects)), Effects);
        PlayerPrefs.SetFloat(Key(nameof(Music)), Music);
        PlayerPrefs.SetFloat(Key(nameof(Narration)), Narration);
        PlayerPrefs.SetInt(Key(nameof(NarrationSubtitles)), NarrationSubtitles ? 1 : 0);
        PlayerPrefs.SetFloat(Key(nameof(Brightness)), Brightness);
        PlayerPrefs.SetInt(Key(nameof(DisplayMode)), DisplayMode);
        PlayerPrefs.SetFloat(Key(nameof(Fov)), Fov);
        PlayerPrefs.SetFloat(Key(nameof(Sensitivity)), Sensitivity);
        PlayerPrefs.SetInt(Key(nameof(InvertY)), InvertY ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void Validate()
    {
        float scale = FiniteClamp(TextScale, 1f, 1.3f, 1f);
        TextScale = scale < 1.075f ? 1f : scale < 1.225f ? 1.15f : 1.3f;
        Master = FiniteClamp(Master, 0f, 1f, .8f);
        Effects = FiniteClamp(Effects, 0f, 1f, .9f);
        Music = FiniteClamp(Music, 0f, 1f, .55f);
        Narration = FiniteClamp(Narration, 0f, 1f, .85f);
        Brightness = FiniteClamp(Brightness, .7f, 1.3f, 1f);
        DisplayMode = Mathf.Clamp(DisplayMode, 0, 2);
        Fov = FiniteClamp(Fov, 60f, 90f, 72f);
        Sensitivity = FiniteClamp(Sensitivity, .5f, 4f, 2f);
    }

    public static float FiniteClamp(float value, float min, float max, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    /// <summary>
    /// Apply saved display mode at normal startup and explicit display edits. Ordinary preference
    /// edits pass false; explicit launch arguments take precedence on the first call.
    /// </summary>
    public void Apply(Camera camera, bool applyDisplay = true)
    {
        Validate();
        AudioListener.volume = Master;
        if (camera != null)
        {
            camera.fieldOfView = Fov;
            ShiyinCameraGrade.ApplyTo(camera, Brightness);
            ShiyinAudio.Ensure(camera).SetLevels(Effects, Music);
        }

        // Also protect command-line overrides if a caller accidentally uses the default on startup.
        bool firstDisplay = !initializedDisplay;
        bool deferToCommandLine = firstDisplay && HasCommandLineDisplayOverride();
        initializedDisplay = true;
        if (applyDisplay && !deferToCommandLine && !Application.isEditor)
        {
            // Start at the chapter's 1080p default even if Windows remembers a previous 720p session.
            int width = firstDisplay ? 1920 : Screen.width;
            int height = firstDisplay ? 1080 : Screen.height;
            if (width > 0 && height > 0)
                Screen.SetResolution(width, height, RequestedDisplayMode);
        }
    }

    public FullScreenMode RequestedDisplayMode => DisplayMode == 2 ? FullScreenMode.Windowed
        : DisplayMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.ExclusiveFullScreen;

    public static bool HasCommandLineDisplayOverride()
    {
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (argument.StartsWith("-screen-", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("-window-mode", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("-window-mode=", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
