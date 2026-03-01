using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace UnityMcpPro
{
    public class AudioCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("add_audio_source", AddAudioSource);
            router.Register("get_audio_clips", GetAudioClips);
            router.Register("get_audio_mixer_info", GetAudioMixerInfo);
            router.Register("set_audio_mixer_param", SetAudioMixerParam);
            router.Register("add_audio_listener", AddAudioListener);
        }

        private static object AddAudioSource(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string clipPath = GetStringParam(p, "clip_path");
            bool playOnAwake = GetBoolParam(p, "play_on_awake");
            bool loop = GetBoolParam(p, "loop");
            float volume = GetFloatParam(p, "volume", 1f);
            float spatialBlend = GetFloatParam(p, "spatial_blend", 0f);

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            var source = Undo.AddComponent<AudioSource>(go);

            source.playOnAwake = playOnAwake;
            source.loop = loop;
            source.volume = volume;
            source.spatialBlend = spatialBlend;

            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip != null)
                    source.clip = clip;
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "clip", source.clip != null ? source.clip.name : "none" },
                { "volume", source.volume }
            };
        }

        private static object GetAudioClips(Dictionary<string, object> p)
        {
            string searchPath = GetStringParam(p, "path", "Assets");

            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { searchPath });
            var clips = new List<object>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    clips.Add(new Dictionary<string, object>
                    {
                        { "name", clip.name },
                        { "path", path },
                        { "length", clip.length },
                        { "channels", clip.channels },
                        { "frequency", clip.frequency },
                        { "samples", clip.samples }
                    });
                }
            }

            return new Dictionary<string, object>
            {
                { "count", clips.Count },
                { "clips", clips }
            };
        }

        private static object GetAudioMixerInfo(Dictionary<string, object> p)
        {
            string mixerPath = GetStringParam(p, "mixer_path");
            if (string.IsNullOrEmpty(mixerPath))
                throw new ArgumentException("mixer_path is required");

            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(mixerPath);
            if (mixer == null)
                throw new ArgumentException($"AudioMixer not found at: {mixerPath}");

            var groups = mixer.FindMatchingGroups("");
            var groupList = new List<object>();

            foreach (var group in groups)
            {
                groupList.Add(new Dictionary<string, object>
                {
                    { "name", group.name }
                });
            }

            return new Dictionary<string, object>
            {
                { "name", mixer.name },
                { "groups", groupList }
            };
        }

        private static object SetAudioMixerParam(Dictionary<string, object> p)
        {
            string mixerPath = GetStringParam(p, "mixer_path");
            string parameter = GetStringParam(p, "parameter");
            float value = GetFloatParam(p, "value");

            if (string.IsNullOrEmpty(mixerPath))
                throw new ArgumentException("mixer_path is required");
            if (string.IsNullOrEmpty(parameter))
                throw new ArgumentException("parameter is required");

            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(mixerPath);
            if (mixer == null)
                throw new ArgumentException($"AudioMixer not found at: {mixerPath}");

            bool set = mixer.SetFloat(parameter, value);
            if (!set)
                throw new ArgumentException($"Parameter '{parameter}' not found or not exposed on mixer");

            return Success($"Set {parameter} to {value} on {mixer.name}");
        }

        private static object AddAudioListener(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);

            // Check for existing listeners
            var existing = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            if (existing.Length > 0)
            {
                var warnings = new List<string>();
                foreach (var listener in existing)
                {
                    if (listener.gameObject != go)
                        warnings.Add($"Existing AudioListener on {listener.gameObject.name}");
                }

                if (warnings.Count > 0)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", true },
                        { "gameObject", go.name },
                        { "warning", $"Multiple AudioListeners: {string.Join(", ", warnings)}" }
                    };
                }
            }

            if (go.GetComponent<AudioListener>() == null)
                Undo.AddComponent<AudioListener>(go);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name }
            };
        }
    }
}
