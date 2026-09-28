using UnityEngine;
using Wasteland.Sim;
using Wasteland.Sim.World;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Presentation-side clock. Accumulates render time and steps
    /// <see cref="SimWorld"/> at <see cref="SimConfig.TickRateHz"/>.
    /// Interpolation between the previous and current sim snapshots belongs
    /// here; the simulation never reads <see cref="Time"/>.
    /// VAT and other presentation systems read
    /// <see cref="InterpolatedSimSeconds"/>; nothing flows back into sim.
    /// </summary>
    public sealed class SimFixedUpdateDriver : MonoBehaviour
    {
        [SerializeField] private ulong _seed = 1;
        [SerializeField] private int _maxCatchUpSteps = 8;

        private SimWorld _world;
        private float _accumulator;

        public SimWorld World
        {
            get { return _world; }
        }

        /// <summary>0..1 blend factor from the last completed tick toward the next.</summary>
        public float TickInterpolation
        {
            get { return _accumulator * SimConfig.TickRateHz; }
        }

        /// <summary>
        /// Display time in seconds: <c>(tick + alpha) * tickDuration</c>.
        /// Pause and speed come from <see cref="GameClock"/> when present.
        /// </summary>
        public float InterpolatedSimSeconds
        {
            get
            {
                if (_world == null)
                {
                    return 0f;
                }

                float tickDuration = 1f / SimConfig.TickRateHz;
                return (_world.Tick + TickInterpolation) * tickDuration;
            }
        }

        private void Awake()
        {
            _world = new SimWorld(_seed);
        }

        private void Update()
        {
            if (_world == null)
            {
                return;
            }

            float tickDt = 1f / SimConfig.TickRateHz;
            _accumulator += PresentationDeltaTime();
            int steps = 0;
            while (_accumulator >= tickDt && steps < _maxCatchUpSteps)
            {
                _world.Step();
                _accumulator -= tickDt;
                steps++;
            }
        }

        private static float PresentationDeltaTime()
        {
            GameClock clock = GameClock.Instance;
            if (clock != null)
            {
                return clock.DeltaTime;
            }

            return Time.unscaledDeltaTime;
        }
    }
}
