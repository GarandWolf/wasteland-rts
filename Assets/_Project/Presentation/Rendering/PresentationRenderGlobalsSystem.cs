using Unity.Entities;
using UnityEngine;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Binds the team palette and the global VAT clock. The clock is interpolated
    /// sim time <c>(tick + alpha) * tickDuration</c> when a
    /// <see cref="SimFixedUpdateDriver"/> is stepping a world; otherwise the
    /// presentation <see cref="GameClock"/>. Nothing here writes back into sim.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PresentationRenderGlobalsSystem : SystemBase
    {
        public const string VatTimeProperty = "_VATTime";
        public const float WrapSeconds = VatClipPacking.WrapSeconds;

        private float _vatTime;
        private SimFixedUpdateDriver _simClock;
        private bool _lookedForSimClock;

        public float VatTime
        {
            get { return _vatTime; }
        }

        protected override void OnCreate()
        {
            TeamPaletteRuntime.BindGlobals();
        }

        protected override void OnUpdate()
        {
            if (!TeamPaletteRuntime.IsBound)
            {
                TeamPaletteRuntime.BindGlobals();
            }

            float seconds;
            if (TryGetInterpolatedSimSeconds(out seconds))
            {
                _vatTime = VatClipPacking.WrapGlobal(seconds);
            }
            else if (GameClock.TryGetTime(out seconds))
            {
                _vatTime = VatClipPacking.WrapGlobal(seconds);
            }
            else
            {
                _vatTime = VatClipPacking.WrapGlobal(_vatTime + UnityEngine.Time.unscaledDeltaTime);
            }

            Shader.SetGlobalFloat(VatTimeProperty, _vatTime);
        }

        private bool TryGetInterpolatedSimSeconds(out float seconds)
        {
            if (!_lookedForSimClock)
            {
                _simClock = Object.FindFirstObjectByType<SimFixedUpdateDriver>();
                _lookedForSimClock = true;
            }

            if (_simClock != null && _simClock.World != null)
            {
                seconds = _simClock.InterpolatedSimSeconds;
                return true;
            }

            seconds = 0f;
            return false;
        }
    }
}
