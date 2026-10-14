using ITKamianets.Engine.Character;
using ITKamianets.Engine.Scene;
using UnityEngine;

namespace ITKamianets.Engine.Audio
{
    /// <summary>
    /// Footsteps and landings for a <see cref="CharacterMotor"/>: plays <c>footstep.&lt;surface&gt;</c>
    /// every stride while it walks on the ground, quieter when crouching, and
    /// <c>land.&lt;surface&gt;</c> after a fall, louder for harder landings. Falls back to plain
    /// <c>footstep</c> / <c>land</c> when there is no event for the surface.
    ///
    /// Works from how the character moves, not from motor events, so it also sounds for other
    /// players' characters shown by the network. The surface comes from an <see cref="AudioSurface"/>
    /// on the ground collider, or a <c>surface:stone</c> tag on its scene object.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class FootstepPlayer : MonoBehaviour
    {
        [SerializeField] private string footstepEvent = "footstep";
        [SerializeField] private string landEvent = "land";
        [SerializeField] private string defaultSurface = "default";

        [Tooltip("Meters walked between two steps.")]
        [SerializeField] private float stride = 0.75f;

        [Tooltip("Steps are longer when sprinting.")]
        [SerializeField] private float sprintStrideScale = 1.3f;

        [Tooltip("Footstep volume when crouching. Crouch-walking is how players sneak.")]
        [SerializeField] private float crouchVolume = 0.35f;

        [Tooltip("Falls slower than this (m/s) land silently.")]
        [SerializeField] private float minLandSpeed = 3f;

        [Tooltip("Landing speed (m/s) that plays at full volume.")]
        [SerializeField] private float loudLandSpeed = 12f;

        private CharacterMotor _motor;
        private Vector3 _lastPosition;
        private float _walked;
        private bool _wasGrounded;
        private float _fallSpeed;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
            _wasGrounded = true;
            _walked = 0f;
        }

        private void LateUpdate()
        {
            var position = transform.position;
            var delta = position - _lastPosition;
            _lastPosition = position;

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            var grounded = _motor.IsGrounded;
            if (!grounded)
            {
                // Measured from movement, so it works for network copies too.
                _fallSpeed = Mathf.Max(0f, -delta.y / deltaTime);
            }
            else if (!_wasGrounded)
            {
                if (_fallSpeed >= minLandSpeed)
                {
                    var loudness = Mathf.InverseLerp(minLandSpeed, loudLandSpeed, _fallSpeed);
                    Play(landEvent, Mathf.Lerp(0.4f, 1f, loudness));
                }

                _walked = 0f;
            }

            _wasGrounded = grounded;
            if (!grounded)
            {
                return;
            }

            // Teleports and spawns are not walking.
            var horizontal = new Vector2(delta.x, delta.z).magnitude;
            if (horizontal > 2f)
            {
                return;
            }

            _walked += horizontal;
            var length = stride * (_motor.IsSprinting ? sprintStrideScale : 1f);
            if (_walked >= length)
            {
                _walked -= length;
                Play(footstepEvent, _motor.IsCrouching ? crouchVolume : 1f);
            }
        }

        private void Play(string baseEvent, float volume)
        {
            var audio = EngineAudio.Default;
            var name = $"{baseEvent}.{Surface()}";
            if (!audio.HasEvent(name))
            {
                name = baseEvent;
            }

            if (audio.HasEvent(name))
            {
                audio.Play(name, transform.position, volume);
            }
        }

        private string Surface()
        {
            var ground = _motor.GroundCollider;
            if (ground == null)
            {
                return defaultSurface;
            }

            var surface = ground.GetComponentInParent<AudioSurface>();
            if (surface != null)
            {
                return surface.Surface;
            }

            var sceneObject = ground.GetComponentInParent<SceneObject>();
            if (sceneObject != null && sceneObject.TryGetTag("surface", out var tagged))
            {
                return tagged;
            }

            return defaultSurface;
        }
    }
}
