using System;
using System.Collections.Generic;
using ITKamianets.Engine.Assets;
using ITKamianets.Engine.Core;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace ITKamianets.Engine.Audio
{
    /// <summary>
    /// Plays sound events by name from a pool of audio sources, applies volume groups, and plays
    /// music with crossfades. Use <see cref="Default"/>.
    ///
    /// <code>
    /// EngineAudio.Default.AddBank(bank);
    /// EngineAudio.Default.Play("spell.fireball.cast", caster.position);
    /// EngineAudio.Default.Play2D("ui.click");
    /// EngineAudio.Default.PlayMusic("music/menu");
    /// </code>
    ///
    /// A name with no event is treated as an audio asset id and played with default settings.
    /// It knows nothing about game rules or networking: it plays what it is told.
    /// </summary>
    public sealed class EngineAudio
    {
        private const string LogCategory = "Audio";
        private const string VolumeKey = "audio.volume.";
        private const int VoiceCount = 32;

        private static EngineAudio _default;

        public static EngineAudio Default => _default ??= new EngineAudio();

        /// <summary>Where audio asset ids are loaded from. Default: <see cref="EngineAssets.Default"/>.</summary>
        public EngineAssets Assets { get; set; }

        /// <summary>Raised for every sound started, with its event name and position (zero for 2D). For debugging and tests.</summary>
        public event Action<string, Vector3> Played;

        public float MasterVolume { get; private set; } = 1f;

        private readonly Dictionary<string, SoundEvent> _events = new Dictionary<string, SoundEvent>();
        private readonly Dictionary<SoundGroup, float> _groupVolumes = new Dictionary<SoundGroup, float>();
        private readonly Dictionary<SoundGroup, AudioMixerGroup> _mixerGroups = new Dictionary<SoundGroup, AudioMixerGroup>();
        private readonly Dictionary<string, int> _lastClip = new Dictionary<string, int>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();

        private AudioRunner _runner;
        private Voice[] _voices;
        private AudioSource[] _music;
        private int _musicCurrent;

        private EngineAudio()
        {
            var storage = EngineStorage.Current;
            MasterVolume = storage.GetFloat(VolumeKey + "master", 1f);
            foreach (SoundGroup group in Enum.GetValues(typeof(SoundGroup)))
            {
                _groupVolumes[group] = storage.GetFloat(VolumeKey + group.ToString().ToLowerInvariant(), 1f);
            }
        }

        // ---- Events ----

        public void AddBank(SoundBank bank)
        {
            foreach (var soundEvent in bank.Events)
            {
                Define(soundEvent);
            }
        }

        /// <summary>Adds or replaces an event, e.g. built from data at runtime.</summary>
        public void Define(SoundEvent soundEvent)
        {
            if (!string.IsNullOrEmpty(soundEvent?.Name))
            {
                _events[soundEvent.Name] = soundEvent;
            }
        }

        public bool HasEvent(string name)
        {
            return name != null && _events.ContainsKey(name);
        }

        /// <summary>Loads an event's asset clips now, so its first play is not delayed by loading.</summary>
        public void Preload(string name)
        {
            if (TryGetEvent(name, out var soundEvent))
            {
                foreach (var id in soundEvent.ClipIds)
                {
                    AssetLoader.Load<AudioClip>(id, null);
                }
            }
        }

        // ---- Playing ----

        /// <summary>Plays an event at a point in the world.</summary>
        public SoundHandle Play(string name, Vector3 position, float volumeScale = 1f)
        {
            return Start(name, position, null, volumeScale, forceFlat: false);
        }

        /// <summary>Plays an event that follows a moving object, e.g. a spell in flight.</summary>
        public SoundHandle PlayAttached(string name, Transform target, float volumeScale = 1f)
        {
            return Start(name, target.position, target, volumeScale, forceFlat: false);
        }

        /// <summary>Plays an event straight in the ears, without a position: interface sounds.</summary>
        public SoundHandle Play2D(string name, float volumeScale = 1f)
        {
            return Start(name, Vector3.zero, null, volumeScale, forceFlat: true);
        }

        public void Stop(SoundHandle handle)
        {
            if (TryGetVoice(handle, out var voice))
            {
                voice.Source.Stop();
                voice.Event = null;
            }
        }

        public bool IsPlaying(SoundHandle handle)
        {
            return TryGetVoice(handle, out var voice) && (voice.Pending || voice.Source.isPlaying);
        }

        // ---- Music ----

        /// <summary>Crossfades to a music event or audio asset id. Music loops; play one-off stingers with <see cref="Play2D"/>.</summary>
        public void PlayMusic(string name, float fadeSeconds = 1.5f)
        {
            if (!TryGetEvent(name, out var soundEvent) || soundEvent.ClipCount == 0)
            {
                EngineLog.Warning(LogCategory, $"No music '{name}'.");
                return;
            }

            EnsureRunner();
            var index = PickClip(name, soundEvent);
            GetClip(soundEvent, index, clip =>
            {
                if (clip == null)
                {
                    return;
                }

                _musicCurrent = 1 - _musicCurrent;
                var source = _music[_musicCurrent];
                source.clip = clip;
                source.loop = true;
                source.volume = 0f;
                source.pitch = 1f;
                source.outputAudioMixerGroup = MixerGroup(SoundGroup.Music);
                source.Play();
                _runner.FadeMusic(_music, _musicCurrent, Mathf.Max(0.01f, fadeSeconds), () => GroupGain(SoundGroup.Music) * soundEvent.Volume.y);
                Played?.Invoke(name, Vector3.zero);
            });
        }

        public void StopMusic(float fadeSeconds = 1.5f)
        {
            if (_runner != null)
            {
                _musicCurrent = 1 - _musicCurrent;
                _music[_musicCurrent].Stop();
                _runner.FadeMusic(_music, _musicCurrent, Mathf.Max(0.01f, fadeSeconds), () => 0f);
            }
        }

        // ---- Volume ----

        public float GetVolume(SoundGroup group)
        {
            return _groupVolumes[group];
        }

        /// <summary>Sets and saves a group's volume (0..1). Playing sounds follow at once.</summary>
        public void SetVolume(SoundGroup group, float volume)
        {
            _groupVolumes[group] = Mathf.Clamp01(volume);
            EngineStorage.Current.SetFloat(VolumeKey + group.ToString().ToLowerInvariant(), _groupVolumes[group]);
            EngineStorage.Current.Save();
            _runner?.RefreshVolumes();
        }

        public void SetMasterVolume(float volume)
        {
            MasterVolume = Mathf.Clamp01(volume);
            EngineStorage.Current.SetFloat(VolumeKey + "master", MasterVolume);
            EngineStorage.Current.Save();
            _runner?.RefreshVolumes();
        }

        /// <summary>Routes a group through a mixer group of the game's AudioMixer (for effects such as reverb or ducking).</summary>
        public void SetMixerGroup(SoundGroup group, AudioMixerGroup mixerGroup)
        {
            _mixerGroups[group] = mixerGroup;
        }

        internal float GroupGain(SoundGroup group)
        {
            return MasterVolume * _groupVolumes[group];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefault()
        {
            _default = null;
        }

        // ---- Internals ----

        private EngineAssets AssetLoader => Assets ?? EngineAssets.Default;

        private bool TryGetEvent(string name, out SoundEvent soundEvent)
        {
            if (string.IsNullOrEmpty(name))
            {
                soundEvent = null;
                return false;
            }

            if (_events.TryGetValue(name, out soundEvent))
            {
                return true;
            }

            // No event: treat the name as an audio asset id, with default settings.
            if (AssetLoader.TryGetDefinition(name, out var definition) && definition.Kind == ITKamianets.Engine.Assets.Model.AssetKind.Audio)
            {
                soundEvent = new SoundEvent { Name = name, ClipIds = { name } };
                _events[name] = soundEvent;
                return true;
            }

            return false;
        }

        private SoundHandle Start(string name, Vector3 position, Transform follow, float volumeScale, bool forceFlat)
        {
            if (!TryGetEvent(name, out var soundEvent) || soundEvent.ClipCount == 0)
            {
                EngineLog.Warning(LogCategory, $"No sound '{name}'.");
                return default;
            }

            if (soundEvent.MinInterval > 0f && _lastPlayed.TryGetValue(name, out var last) && Time.unscaledTime - last < soundEvent.MinInterval)
            {
                return default;
            }

            _lastPlayed[name] = Time.unscaledTime;
            EnsureRunner();

            var voice = TakeVoice(soundEvent);
            if (voice == null)
            {
                return default;
            }

            voice.Generation++;
            voice.Event = soundEvent;
            voice.Follow = follow;
            voice.BaseVolume = Random.Range(soundEvent.Volume.x, soundEvent.Volume.y) * Mathf.Max(0f, volumeScale);
            voice.Started = Time.unscaledTime;
            voice.Pending = true;

            var source = voice.Source;
            source.Stop();
            source.transform.position = position;
            source.spatialBlend = soundEvent.Spatial && !forceFlat ? 1f : 0f;
            source.minDistance = soundEvent.MinDistance;
            source.maxDistance = soundEvent.MaxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            source.loop = soundEvent.Loop;
            source.pitch = Random.Range(soundEvent.Pitch.x, soundEvent.Pitch.y);
            source.priority = 128 - Mathf.Clamp(soundEvent.Priority, -127, 127);
            source.outputAudioMixerGroup = MixerGroup(soundEvent.Group);
            source.volume = voice.BaseVolume * GroupGain(soundEvent.Group);

            var generation = voice.Generation;
            GetClip(soundEvent, PickClip(name, soundEvent), clip =>
            {
                // The voice may have been taken by another sound while the clip loaded.
                if (voice.Generation != generation)
                {
                    return;
                }

                voice.Pending = false;
                if (clip == null)
                {
                    voice.Event = null;
                    return;
                }

                source.clip = clip;
                source.Play();
                Played?.Invoke(name, forceFlat ? Vector3.zero : position);
            });

            return new SoundHandle(voice.Index, generation);
        }

        private Voice TakeVoice(SoundEvent soundEvent)
        {
            Voice free = null;
            Voice oldestSame = null;
            Voice weakest = null;
            var sameCount = 0;

            foreach (var voice in _voices)
            {
                var busy = voice.Event != null && (voice.Pending || voice.Source.isPlaying);
                if (!busy)
                {
                    free ??= voice;
                    continue;
                }

                if (voice.Event == soundEvent)
                {
                    sameCount++;
                    if (oldestSame == null || voice.Started < oldestSame.Started)
                    {
                        oldestSame = voice;
                    }
                }

                if (voice.Event.Priority <= soundEvent.Priority && (weakest == null || voice.Event.Priority < weakest.Event.Priority || (voice.Event.Priority == weakest.Event.Priority && voice.Started < weakest.Started)))
                {
                    weakest = voice;
                }
            }

            if (soundEvent.MaxVoices > 0 && sameCount >= soundEvent.MaxVoices)
            {
                return oldestSame;
            }

            return free ?? weakest;
        }

        // A different clip from last time whenever there is a choice.
        private int PickClip(string name, SoundEvent soundEvent)
        {
            var count = soundEvent.ClipCount;
            if (count <= 1)
            {
                return 0;
            }

            _lastClip.TryGetValue(name, out var last);
            var index = Random.Range(0, count - 1);
            if (index >= last)
            {
                index++;
            }

            _lastClip[name] = index;
            return index;
        }

        private void GetClip(SoundEvent soundEvent, int index, Action<AudioClip> done)
        {
            if (index < soundEvent.Clips.Count)
            {
                done(soundEvent.Clips[index]);
                return;
            }

            AssetLoader.Load(soundEvent.ClipIds[index - soundEvent.Clips.Count], done);
        }

        private AudioMixerGroup MixerGroup(SoundGroup group)
        {
            return _mixerGroups.TryGetValue(group, out var mixerGroup) ? mixerGroup : null;
        }

        private bool TryGetVoice(SoundHandle handle, out Voice voice)
        {
            voice = null;
            if (_voices == null || handle.Index < 0 || handle.Index >= _voices.Length)
            {
                return false;
            }

            voice = _voices[handle.Index];
            return voice.Generation == handle.Generation && voice.Event != null;
        }

        private void EnsureRunner()
        {
            if (_runner != null)
            {
                return;
            }

            var go = new GameObject("[EngineAudio]") { hideFlags = HideFlags.HideInHierarchy };
            if (Application.isPlaying)
            {
                Object.DontDestroyOnLoad(go);
            }

            _runner = go.AddComponent<AudioRunner>();
            _runner.Audio = this;

            _voices = new Voice[VoiceCount];
            for (var i = 0; i < VoiceCount; i++)
            {
                var voiceObject = new GameObject($"Voice {i}");
                voiceObject.transform.SetParent(go.transform, false);
                var source = voiceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                _voices[i] = new Voice { Index = i, Source = source };
            }

            _music = new AudioSource[2];
            for (var i = 0; i < 2; i++)
            {
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.loop = true;
                _music[i] = source;
            }

            _runner.Voices = _voices;
        }

        internal sealed class Voice
        {
            public int Index;
            public AudioSource Source;
            public SoundEvent Event;
            public Transform Follow;
            public float BaseVolume;
            public float Started;
            public int Generation;

            // Waiting for its clip to load.
            public bool Pending;
        }

        /// <summary>Moves attached sounds, frees finished voices, fades music. Hidden, kept across scenes.</summary>
        private sealed class AudioRunner : MonoBehaviour
        {
            [System.NonSerialized] public EngineAudio Audio;
            [System.NonSerialized] public Voice[] Voices;

            private AudioSource[] _music;
            private int _musicTarget = -1;
            private float _fadeSpeed;
            private Func<float> _musicVolume;

            public void FadeMusic(AudioSource[] music, int target, float seconds, Func<float> volume)
            {
                _music = music;
                _musicTarget = target;
                _fadeSpeed = 1f / seconds;
                _musicVolume = volume;
            }

            public void RefreshVolumes()
            {
                foreach (var voice in Voices)
                {
                    if (voice.Event != null)
                    {
                        voice.Source.volume = voice.BaseVolume * Audio.GroupGain(voice.Event.Group);
                    }
                }
            }

            private void Update()
            {
                foreach (var voice in Voices)
                {
                    if (voice.Event == null)
                    {
                        continue;
                    }

                    if (!voice.Pending && !voice.Source.isPlaying)
                    {
                        voice.Event = null;
                        voice.Follow = null;
                        continue;
                    }

                    if (voice.Follow != null)
                    {
                        voice.Source.transform.position = voice.Follow.position;
                    }
                }

                if (_music == null || _musicTarget < 0)
                {
                    return;
                }

                var step = _fadeSpeed * Time.unscaledDeltaTime;
                var target = _music[_musicTarget];
                var other = _music[1 - _musicTarget];
                target.volume = Mathf.MoveTowards(target.volume, _musicVolume(), step);
                other.volume = Mathf.MoveTowards(other.volume, 0f, step);
                if (other.volume <= 0f && other.isPlaying)
                {
                    other.Stop();
                }
            }
        }
    }

    /// <summary>A started sound, for stopping it or checking whether it still plays.</summary>
    public readonly struct SoundHandle
    {
        public readonly int Index;
        public readonly int Generation;

        public SoundHandle(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        /// <summary>False for a sound that was skipped (unknown name, too soon after the last play).</summary>
        public bool IsValid => Generation > 0;
    }
}
