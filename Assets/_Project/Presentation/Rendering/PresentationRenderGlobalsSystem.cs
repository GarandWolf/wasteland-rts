using Unity.Entities;
using UnityEngine;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Advances the global VAT clock by scaled game delta time and binds the
    /// team palette. Lives in presentation so animation follows pause / map slowdown
    /// without touching <c>Wasteland.Sim</c>.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PresentationRenderGlobalsSystem : SystemBase
    {
        public const string VatTimeProperty = "_VATTime";
        public const float WrapSeconds = VatClipPacking.WrapSeconds;

        private float _vatTime;

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

            _vatTime += UnityEngine.Time.deltaTime;
            if (_vatTime >= WrapSeconds)
            {
                _vatTime -= WrapSeconds;
            }

            Shader.SetGlobalFloat(VatTimeProperty, _vatTime);
        }
    }
}
