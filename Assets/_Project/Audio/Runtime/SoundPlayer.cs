using UnityEngine;

namespace Wasteland.Audio
{
    /// <summary>
    /// Plays one-shot placeholder cues. 2D for UI and stingers, 3D for cues
    /// the bank marks spatial. Does not play <see cref="SoundCue.CombatLoop"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Wasteland/Audio/Sound Player")]
    public sealed class SoundPlayer : MonoBehaviour
    {
        /// <summary>
        /// Scale for a rejected human order. Spec §10: <c>ui_click</c> at low volume,
        /// no separate error sound. A quarter of the bank volume.
        /// </summary>
        public const float LowVolumeScale = 0.25f;

        [SerializeField] private SoundBank _bank;
        [SerializeField] private float _minDistance = 1f;
        [SerializeField] private float _maxDistance = 500f;

        private AudioSource _uiSource;
        private CueCooldown _cooldown;

        public SoundBank Bank
        {
            get { return _bank; }
        }

        public void UseBank(SoundBank bank)
        {
            _bank = bank;
        }

        private void Reset()
        {
            ConfigureUiSource();
        }

        private void Awake()
        {
            _cooldown = new CueCooldown(SoundCues.Count);
            ConfigureUiSource();
        }

        /// <summary>2D. UI clicks and the win, lose, and draw stingers.</summary>
        public void Play(SoundCue cue)
        {
            PlayScaled(cue, 1f, false, default);
        }

        /// <summary>2D, multiplied by <paramref name="volumeScale"/>.</summary>
        public void Play(SoundCue cue, float volumeScale)
        {
            PlayScaled(cue, volumeScale, false, default);
        }

        /// <summary>
        /// Spatial one-shot at <paramref name="worldPos"/> when the bank entry is 3D.
        /// A 2D entry ignores the position.
        /// </summary>
        public void Play(SoundCue cue, Vector3 worldPos)
        {
            Play(cue, worldPos, 1f);
        }

        /// <summary>Spatial one-shot with a volume scale. 2D entries ignore the position.</summary>
        public void Play(SoundCue cue, Vector3 worldPos, float volumeScale)
        {
            PlayScaled(cue, volumeScale, true, worldPos);
        }

        /// <summary>2D at <see cref="LowVolumeScale"/>. Use for a rejected human order.</summary>
        public void PlayLow(SoundCue cue)
        {
            Play(cue, LowVolumeScale);
        }

        private void PlayScaled(SoundCue cue, float volumeScale, bool usePosition, Vector3 worldPos)
        {
            if (!Application.isPlaying || cue == SoundCue.CombatLoop)
            {
                return;
            }

            if (float.IsNaN(volumeScale) || float.IsInfinity(volumeScale))
            {
                return;
            }

            if (volumeScale < 0f)
            {
                volumeScale = 0f;
            }

            if (_bank == null || !_bank.TryGet(cue, out SoundBankEntry entry) || entry.Clip == null)
            {
                return;
            }

            if (_cooldown == null)
            {
                _cooldown = new CueCooldown(SoundCues.Count);
            }

            if (!_cooldown.TryAcquire(cue, Time.unscaledTime, entry.CooldownSeconds))
            {
                return;
            }

            float volume = entry.Volume * volumeScale;
            if (usePosition && entry.Spatial)
            {
                PlaySpatial(entry.Clip, worldPos, volume);
            }
            else
            {
                PlayFlat(entry.Clip, volume);
            }
        }

        private void ConfigureUiSource()
        {
            _uiSource = GetComponent<AudioSource>();
            if (_uiSource == null)
            {
                return;
            }

            _uiSource.playOnAwake = false;
            _uiSource.loop = false;
            _uiSource.spatialBlend = 0f;
            _uiSource.dopplerLevel = 0f;
            // PlayOneShot multiplies by this. Bank volume is the per-cue level.
            _uiSource.volume = 1f;
        }

        private void PlayFlat(AudioClip clip, float volume)
        {
            if (_uiSource == null)
            {
                ConfigureUiSource();
            }

            if (_uiSource == null)
            {
                return;
            }

            _uiSource.PlayOneShot(clip, volume);
        }

        private void PlaySpatial(AudioClip clip, Vector3 worldPos, float volume)
        {
            GameObject oneShot = new GameObject("SoundOneShot");
            oneShot.transform.position = worldPos;
            AudioSource source = oneShot.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = 1f;
            source.loop = false;
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = _minDistance;
            source.maxDistance = _maxDistance > _minDistance ? _maxDistance : _minDistance;
            source.Play();

            float life = clip.length;
            if (life < 0.05f)
            {
                life = 0.05f;
            }

            Destroy(oneShot, life + 0.05f);
        }

        [ContextMenu("Preview Ui Click")]
        private void PreviewUiClick()
        {
            Play(SoundCue.UiClick);
        }

        [ContextMenu("Preview Ui Click Low")]
        private void PreviewUiClickLow()
        {
            PlayLow(SoundCue.UiClick);
        }

        [ContextMenu("Preview Order Ack")]
        private void PreviewOrderAck()
        {
            Play(SoundCue.OrderAck);
        }

        [ContextMenu("Preview Raise Battalion Here")]
        private void PreviewRaiseBattalion()
        {
            Play(SoundCue.RaiseBattalion, transform.position);
        }

        [ContextMenu("Preview Depot Built Here")]
        private void PreviewDepotBuilt()
        {
            Play(SoundCue.DepotBuilt, transform.position);
        }

        [ContextMenu("Preview Town Captured Here")]
        private void PreviewTownCaptured()
        {
            Play(SoundCue.TownCaptured, transform.position);
        }

        [ContextMenu("Preview Town Lost Here")]
        private void PreviewTownLost()
        {
            Play(SoundCue.TownLost, transform.position);
        }

        [ContextMenu("Preview Win Stinger")]
        private void PreviewWinStinger()
        {
            Play(SoundCue.WinStinger);
        }

        [ContextMenu("Preview Lose Stinger")]
        private void PreviewLoseStinger()
        {
            Play(SoundCue.LoseStinger);
        }

        [ContextMenu("Preview Draw Stinger")]
        private void PreviewDrawStinger()
        {
            Play(SoundCue.DrawStinger);
        }
    }
}
