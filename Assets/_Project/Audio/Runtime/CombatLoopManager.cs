using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wasteland.Audio
{
    /// <summary>A contested town the caller can currently see, in Unity world space.</summary>
    public readonly struct ContestedTown
    {
        public int Id { get; }
        public Vector3 WorldPosition { get; }

        public ContestedTown(int id, Vector3 worldPosition)
        {
            Id = id;
            WorldPosition = worldPosition;
        }
    }

    /// <summary>
    /// One looping combat bed per contested on-screen town, capped at
    /// <see cref="CombatVoiceSelector.MaxVoices"/> nearest the listener.
    /// The clip importer leaves Loop off, so each source sets <c>loop</c> itself.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wasteland/Audio/Combat Loop Manager")]
    public sealed class CombatLoopManager : MonoBehaviour
    {
        [SerializeField] private SoundBank _bank;
        [SerializeField] private Transform _listenerOverride;
        [SerializeField] private float _fadeSeconds = 0.2f;
        [SerializeField] private float _minDistance = 1f;
        [SerializeField] private float _maxDistance = 500f;

        private readonly List<ContestedTown> _towns = new List<ContestedTown>();
        private readonly CombatBedLedger _beds = new CombatBedLedger();
        private Voice[] _voices;
        private int[] _ids = Array.Empty<int>();
        private Vector3[] _positions = Array.Empty<Vector3>();
        private readonly int[] _selected = new int[CombatVoiceSelector.MaxVoices];
        private readonly bool[] _matched = new bool[CombatVoiceSelector.MaxVoices];
        private AudioListener _listener;

        private sealed class Voice
        {
            public AudioSource Source;
            public int TownId;
            public bool Occupied;
            public float Gain;
            public float TargetGain;
        }

        public SoundBank Bank
        {
            get { return _bank; }
        }

        private void Awake()
        {
            EnsureVoices();
        }

        /// <summary>
        /// Replace the contested towns that are on screen. Call every frame, or
        /// when the set or the positions change. Null or empty clears the bed.
        /// A repeated id keeps the last position. Playback waits for Play mode.
        /// </summary>
        public void SetContestedTowns(IReadOnlyList<ContestedTown> towns)
        {
            _towns.Clear();
            if (towns == null)
            {
                return;
            }

            for (int i = 0; i < towns.Count; i++)
            {
                ContestedTown town = towns[i];
                int existing = -1;
                for (int j = 0; j < _towns.Count; j++)
                {
                    if (_towns[j].Id == town.Id)
                    {
                        existing = j;
                        break;
                    }
                }

                if (existing >= 0)
                {
                    _towns[existing] = town;
                }
                else
                {
                    _towns.Add(town);
                }
            }
        }

        public void Clear()
        {
            _beds.Clear();
            SetContestedTowns(null);
        }

        public void SetListener(Transform listener)
        {
            _listenerOverride = listener;
            _listener = null;
        }

        public void SetListener(AudioListener listener)
        {
            _listener = listener;
            _listenerOverride = listener != null ? listener.transform : null;
        }

        public void SetRolloff(float minDistance, float maxDistance)
        {
            if (minDistance > 0f)
            {
                _minDistance = minDistance;
            }

            if (maxDistance > _minDistance)
            {
                _maxDistance = maxDistance;
            }

            float knee = CombatBedRolloff.NormalizedKnee(_minDistance, _maxDistance);
            if (_customRolloff == null || !Mathf.Approximately(knee, _customKnee))
            {
                _customKnee = knee;
                _customRolloff = new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(knee, 1f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f));
                for (int k = 0; k < _customRolloff.length; k++)
                {
                    AnimationUtility_SetLinear(_customRolloff, k);
                }
            }
        }

        private AnimationCurve _customRolloff;
        private float _customKnee = -1f;

        // Runtime-safe linear tangents (UnityEditor.AnimationUtility is editor-only).
        private static void AnimationUtility_SetLinear(AnimationCurve curve, int index)
        {
            Keyframe key = curve[index];
            if (index > 0)
            {
                Keyframe prev = curve[index - 1];
                float dx = key.time - prev.time;
                key.inTangent = dx > 0f ? (key.value - prev.value) / dx : 0f;
            }

            if (index < curve.length - 1)
            {
                Keyframe next = curve[index + 1];
                float dx = next.time - key.time;
                key.outTangent = dx > 0f ? (next.value - key.value) / dx : 0f;
            }

            curve.MoveKey(index, key);
        }

        /// <summary>
        /// Start or stop the bed for a contest event. Kind 10 starts, kind 11 stops.
        /// Subject is the town. Aux and param0 are the two seats, -1 for a garrison.
        /// </summary>
        public bool ApplyContestEvent(int kind, int subjectId, int auxId, int param0, Vector3 worldPosition)
        {
            int townId;
            int seatA;
            int seatB;
            bool started;
            if (!CombatContestEvents.TryRead(kind, subjectId, auxId, param0, out townId, out seatA, out seatB, out started))
            {
                return false;
            }

            _beds.Apply(kind, subjectId, auxId, param0);
            if (started)
            {
                SetContestedTowns(ReplaceTown(_towns, new ContestedTown(townId, worldPosition)));
                return true;
            }

            List<ContestedTown> remaining = new List<ContestedTown>();
            for (int i = 0; i < _towns.Count; i++)
            {
                if (_towns[i].Id != townId)
                {
                    remaining.Add(_towns[i]);
                }
            }

            SetContestedTowns(remaining);
            return true;
        }

        public bool IsTownBedActive(int townId)
        {
            return _beds.Contains(townId);
        }

        private static List<ContestedTown> ReplaceTown(List<ContestedTown> current, ContestedTown town)
        {
            List<ContestedTown> next = new List<ContestedTown>();
            bool replaced = false;
            for (int i = 0; i < current.Count; i++)
            {
                if (current[i].Id == town.Id)
                {
                    next.Add(town);
                    replaced = true;
                }
                else
                {
                    next.Add(current[i]);
                }
            }

            if (!replaced)
            {
                next.Add(town);
            }

            return next;
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Reconcile(Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            if (_voices == null)
            {
                return;
            }

            for (int i = 0; i < _voices.Length; i++)
            {
                Voice voice = _voices[i];
                if (voice.Source != null)
                {
                    voice.Source.Stop();
                }

                voice.Occupied = false;
                voice.Gain = 0f;
                voice.TargetGain = 0f;
            }
        }

        private void EnsureVoices()
        {
            if (_voices != null)
            {
                return;
            }

            _voices = new Voice[CombatVoiceSelector.MaxVoices];
            for (int i = 0; i < _voices.Length; i++)
            {
                GameObject child = new GameObject("Combat Loop Voice");
                child.transform.SetParent(transform, false);
                AudioSource source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.Stop();
                _voices[i] = new Voice { Source = source };
            }
        }

        private void Reconcile(float deltaTime)
        {
            EnsureVoices();

            SoundBankEntry loopEntry = default;
            bool hasLoop = _bank != null
                && _bank.TryGet(SoundCue.CombatLoop, out loopEntry)
                && loopEntry.Clip != null;
            if (!hasLoop)
            {
                loopEntry = default;
            }

            int selectedCount = 0;
            if (hasLoop && _towns.Count > 0)
            {
                if (_ids.Length < _towns.Count)
                {
                    _ids = new int[_towns.Count];
                    _positions = new Vector3[_towns.Count];
                }

                for (int i = 0; i < _towns.Count; i++)
                {
                    _ids[i] = _towns[i].Id;
                    _positions[i] = _towns[i].WorldPosition;
                }

                selectedCount = CombatVoiceSelector.SelectNearest(
                    _ids.AsSpan(0, _towns.Count),
                    _positions.AsSpan(0, _towns.Count),
                    ListenerPosition(),
                    _selected);
            }

            for (int i = 0; i < _matched.Length; i++)
            {
                _matched[i] = false;
            }

            for (int v = 0; v < _voices.Length; v++)
            {
                Voice voice = _voices[v];
                if (!voice.Occupied)
                {
                    continue;
                }

                int selectedSlot = -1;
                for (int s = 0; s < selectedCount; s++)
                {
                    int townIndex = _selected[s];
                    if (_towns[townIndex].Id == voice.TownId)
                    {
                        selectedSlot = s;
                        break;
                    }
                }

                if (selectedSlot >= 0 && hasLoop)
                {
                    _matched[selectedSlot] = true;
                    voice.TargetGain = 1f;
                    int townIndex = _selected[selectedSlot];
                    voice.Source.transform.position = _towns[townIndex].WorldPosition;
                    ApplyLoopSource(voice.Source, loopEntry);
                }
                else
                {
                    voice.TargetGain = 0f;
                }

                float fade = CombatBedClock.FadeSeconds(voice.Gain, voice.TargetGain, _fadeSeconds);
                voice.Gain = SoundFades.Advance(voice.Gain, voice.TargetGain, deltaTime, fade);
                float baseVolume = hasLoop ? loopEntry.Volume : 1f;
                voice.Source.volume = baseVolume * voice.Gain;

                if (voice.TargetGain > 0f && !voice.Source.isPlaying)
                {
                    voice.Source.Play();
                }

                if (voice.TargetGain <= 0f && voice.Gain <= 0.0001f)
                {
                    voice.Source.Stop();
                    voice.Occupied = false;
                    voice.Gain = 0f;
                }
            }

            if (!hasLoop)
            {
                return;
            }

            for (int s = 0; s < selectedCount; s++)
            {
                if (_matched[s])
                {
                    continue;
                }

                Voice free = null;
                for (int v = 0; v < _voices.Length; v++)
                {
                    if (!_voices[v].Occupied)
                    {
                        free = _voices[v];
                        break;
                    }
                }

                if (free == null)
                {
                    break;
                }

                int townIndex = _selected[s];
                free.Occupied = true;
                free.TownId = _towns[townIndex].Id;
                free.Gain = 0f;
                free.TargetGain = 1f;
                free.Source.transform.position = _towns[townIndex].WorldPosition;
                free.Source.clip = loopEntry.Clip;
                ApplyLoopSource(free.Source, loopEntry);
                free.Source.volume = 0f;
                free.Source.Play();
            }
        }

        private void ApplyLoopSource(AudioSource source, SoundBankEntry entry)
        {
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.playOnAwake = false;
            source.minDistance = _minDistance;
            source.maxDistance = _maxDistance > _minDistance ? _maxDistance : _minDistance;
            if (_customRolloff != null)
            {
                source.rolloffMode = AudioRolloffMode.Custom;
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _customRolloff);
            }
            else
            {
                source.rolloffMode = AudioRolloffMode.Logarithmic;
            }
            if (source.clip != entry.Clip)
            {
                source.clip = entry.Clip;
            }
        }

        private Vector3 ListenerPosition()
        {
            if (_listenerOverride != null)
            {
                return _listenerOverride.position;
            }

            if (_listener == null)
            {
                _listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
            }

            if (_listener != null)
            {
                return _listener.transform.position;
            }

            return transform.position;
        }

        [ContextMenu("Preview One Contested Town Here")]
        private void PreviewOneTown()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            SetContestedTowns(new[] { new ContestedTown(1, transform.position) });
        }

        [ContextMenu("Clear Contested Towns")]
        private void PreviewClear()
        {
            Clear();
        }
    }
}
