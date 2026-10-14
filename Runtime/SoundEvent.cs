using System;
using System.Collections.Generic;
using UnityEngine;

namespace ITKamianets.Engine.Audio
{
    /// <summary>The volume group a sound plays in. A settings screen changes one value per group.</summary>
    public enum SoundGroup
    {
        Effects,
        Music,
        Voice,
        Interface,
    }

    /// <summary>
    /// A sound by name, e.g. <c>footstep.stone</c>. Each play picks one of its clips (never the
    /// same one twice in a row) and varies volume and pitch, so repeated sounds never feel
    /// copy-pasted. Changing how an event sounds is a data change, not a code change.
    /// </summary>
    [Serializable]
    public class SoundEvent
    {
        [Tooltip("Name the game plays it by, e.g. footstep.stone, spell.fireball.cast")]
        public string Name;

        [Tooltip("Clips made in Unity.")]
        public List<AudioClip> Clips = new List<AudioClip>();

        [Tooltip("Audio asset ids (3d-asset-schema), loaded through 3d-unity-assets.")]
        public List<string> ClipIds = new List<string>();

        public SoundGroup Group = SoundGroup.Effects;

        public Vector2 Volume = new Vector2(0.9f, 1f);
        public Vector2 Pitch = new Vector2(0.95f, 1.05f);

        [Tooltip("Heard from a place in the world (direction and distance), not straight in the ears.")]
        public bool Spatial = true;

        [Tooltip("Full volume up to this distance, in meters.")]
        public float MinDistance = 1f;

        [Tooltip("Silent beyond this distance, in meters.")]
        public float MaxDistance = 40f;

        public bool Loop;

        [Tooltip("How many of this event can play at once; the oldest stops for a new one.")]
        public int MaxVoices = 4;

        [Tooltip("Plays closer than this many seconds apart are skipped.")]
        public float MinInterval;

        [Tooltip("Higher wins when every voice is busy.")]
        public int Priority;

        public int ClipCount => Clips.Count + ClipIds.Count;
    }
}
