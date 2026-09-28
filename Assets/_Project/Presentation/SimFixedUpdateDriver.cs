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
            _accumulator += Time.unscaledDeltaTime;
            int steps = 0;
            while (_accumulator >= tickDt && steps < _maxCatchUpSteps)
            {
                _world.Step();
                _accumulator -= tickDt;
                steps++;
            }
        }
    }
}
