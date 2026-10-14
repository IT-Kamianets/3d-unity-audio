using UnityEngine;

namespace ITKamianets.Engine.Audio
{
    /// <summary>
    /// What a collider is made of, for footsteps and landings: "stone", "wood", "metal", ...
    /// Scene objects can instead carry a <c>surface:stone</c> tag in their scene metadata.
    /// </summary>
    public class AudioSurface : MonoBehaviour
    {
        [SerializeField] private string surface = "stone";

        public string Surface
        {
            get => surface;
            set => surface = value;
        }
    }
}
