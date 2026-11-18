using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    internal static class NarrationResourceChecks
    {
        [Serializable] sealed class Catalog { public Cue[] lines; }
        [Serializable] sealed class Cue { public string id, zh, en; public Part[] parts; }
        [Serializable] sealed class Part { public string clip, zh, en; }

        internal static void Run(Action<bool,string> check)
        {
            var asset = Resources.Load<TextAsset>("Narration/approved");
            check(asset != null, "the Player packages the approved bilingual script");
            var cues = JsonUtility.FromJson<Catalog>(asset.text).lines;
            var clips = Resources.LoadAll<AudioClip>("Narration/en");
            var approved = new HashSet<string>(StringComparer.Ordinal);
            var recordings = new HashSet<string>(StringComparer.Ordinal);
            check(cues.Length == 21 && clips.Length == 34, "all 21 events and 34 separately playable lines are packaged in the Player");
            foreach (var cue in cues)
            {
                check(approved.Add(cue.id) && !string.IsNullOrWhiteSpace(cue.zh) && !string.IsNullOrWhiteSpace(cue.en),
                    cue.id + " has distinct identity, Chinese subtitles and an English script");
                var zh = cue.zh.Split('\n');
                var en = cue.en.Split('\n');
                check(zh.Length == en.Length && zh.Length == cue.parts.Length, cue.id + " retains each authored line break");
                for (int i=0; i<cue.parts.Length; i++)
                {
                    var part = cue.parts[i];
                    check(recordings.Add(part.clip) && zh[i] == part.zh && en[i] == part.en,
                        part.clip + " pairs exactly one original Chinese line with its English counterpart");
                    var clip = Resources.Load<AudioClip>("Narration/en/" + part.clip);
                    check(clip != null && clip.channels == 1 && clip.frequency == 24000 && clip.length > .25f &&
                        clip.length < 40f && clip.LoadAudioData(), part.clip + " resolves to a playable 24 kHz mono recording");
                    var samples = new float[clip.samples];
                    check(clip.GetData(samples, 0), part.clip + " can be decoded by the Player");
                    double energy = 0;
                    foreach (float sample in samples) energy += sample * sample;
                    check(energy / samples.Length > 0.00002, part.clip + " decodes to audible non-silent speech");
                }
            }
            foreach (var clip in clips)
                check(recordings.Contains(clip.name), "every bundled narration clip belongs to the approved script: " + clip.name);
            check(!approved.Contains("04_purple_flame") && !approved.Contains("07_boss_sleeves") && !approved.Contains("10_local_light_shadow"),
                "the disabled events have no narration content");
        }
    }
}
