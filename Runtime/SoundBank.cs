using System.Collections.Generic;
using UnityEngine;

namespace ITKamianets.Engine.Audio
{
    /// <summary>
    /// A set of sound events made in Unity: Assets → Create → IT-Kamianets → Sound Bank. Add it with
    /// <see cref="EngineAudio.AddBank"/>; a game usually has one bank per area (player, spells, UI).
    /// </summary>
    [CreateAssetMenu(menuName = "IT-Kamianets/Sound Bank", fileName = "SoundBank")]
    public class SoundBank : ScriptableObject
    {
        [SerializeField] private List<SoundEvent> events = new List<SoundEvent>();

        public List<SoundEvent> Events => events;
    }
}
