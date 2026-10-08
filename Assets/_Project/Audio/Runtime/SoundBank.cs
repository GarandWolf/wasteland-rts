using System;
using UnityEngine;

namespace Wasteland.Audio
{
    [Serializable]
    public struct SoundBankEntry
    {
        [SerializeField] private SoundCue _cue;
        [SerializeField] private AudioClip _clip;
        [SerializeField] private float _volume;
        [SerializeField] private int _cooldownMilliseconds;
        [SerializeField] private bool _spatial;

        public SoundCue Cue
        {
            get { return _cue; }
        }

        public AudioClip Clip
        {
            get { return _clip; }
        }

        public float Volume
        {
            get { return _volume; }
        }

        public int CooldownMilliseconds
        {
            get { return _cooldownMilliseconds; }
        }

        public float CooldownSeconds
        {
            get { return _cooldownMilliseconds > 0 ? _cooldownMilliseconds / 1000f : 0f; }
        }

        public bool Spatial
        {
            get { return _spatial; }
        }
    }

    /// <summary>
    /// Maps each <see cref="SoundCue"/> to a clip, volume, retrigger gap, and 2D/3D flag.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundBank", menuName = "Wasteland/Audio/Sound Bank")]
    public sealed class SoundBank : ScriptableObject
    {
        public const string PlaceholderAssetPath = "Assets/_Project/Audio/Runtime/PlaceholderSoundBank.asset";

        [SerializeField] private SoundBankEntry[] _entries = Array.Empty<SoundBankEntry>();

        public int EntryCount
        {
            get { return _entries == null ? 0 : _entries.Length; }
        }

        public SoundBankEntry GetEntry(int index)
        {
            return _entries[index];
        }

        /// <summary>
        /// Returns false when the cue is missing or listed more than once.
        /// </summary>
        public bool TryGet(SoundCue cue, out SoundBankEntry entry)
        {
            entry = default;
            if (_entries == null)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Cue != cue)
                {
                    continue;
                }

                if (found)
                {
                    entry = default;
                    return false;
                }

                entry = _entries[i];
                found = true;
            }

            return found;
        }

        public bool ContainsDuplicateCues()
        {
            if (_entries == null)
            {
                return false;
            }

            bool[] seen = new bool[SoundCues.Count];
            for (int i = 0; i < _entries.Length; i++)
            {
                int index = (int)_entries[i].Cue;
                if ((uint)index >= (uint)seen.Length || seen[index])
                {
                    return true;
                }

                seen[index] = true;
            }

            return false;
        }
    }
}
