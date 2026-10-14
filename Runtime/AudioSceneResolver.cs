using ITKamianets.Engine.Assets;
using ITKamianets.Engine.Assets.Model;
using ITKamianets.Engine.Scene;
using UnityEngine;

namespace ITKamianets.Engine.Audio
{
    /// <summary>
    /// Scene asset resolver that turns audio assets placed in a scene (a torch, a waterfall) into
    /// looping 3D sound sources, and passes every other asset on to <see cref="EngineAssets"/>.
    ///
    /// <code>
    /// var options = new SceneLoadOptions { AssetResolver = new AudioSceneResolver() };
    /// </code>
    ///
    /// The source's range comes from the scene object's metadata properties <c>minDistance</c>
    /// and <c>maxDistance</c> (meters), and its volume from <c>volume</c>, when present. It plays
    /// in the Effects group and follows its volume when the source is created.
    /// </summary>
    public class AudioSceneResolver : ISceneAssetResolver
    {
        private readonly EngineAssets _assets;

        public AudioSceneResolver(EngineAssets assets = null)
        {
            _assets = assets ?? EngineAssets.Default;
        }

        public void Resolve(SceneObject target)
        {
            if (!_assets.TryGetDefinition(target.Asset, out var definition) || definition.Kind != AssetKind.Audio)
            {
                ((ISceneAssetResolver)_assets).Resolve(target);
                return;
            }

            var source = target.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = Property(target, "minDistance", 1f);
            source.maxDistance = Property(target, "maxDistance", 25f);
            source.volume = Property(target, "volume", 1f) * EngineAudio.Default.GroupGain(SoundGroup.Effects);

            _assets.Load<AudioClip>(target.Asset, clip =>
            {
                if (clip != null && source != null)
                {
                    source.clip = clip;
                    source.Play();
                }
            });
        }

        private static float Property(SceneObject target, string key, float fallback)
        {
            var properties = target.Data?.Metadata?.Properties;
            if (properties != null && properties.TryGetValue(key, out var value) && value != null && float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            return fallback;
        }
    }
}
