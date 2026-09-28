using Unity.Entities;
using Unity.Rendering;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Clip start time on the global VAT clock (seconds), including a per-entity
    /// phase offset. Presentation-only; never hashed with sim state.
    /// </summary>
    [MaterialProperty("_AnimTime")]
    public struct VatStartTime : IComponentData
    {
        public float Value;
    }
}
