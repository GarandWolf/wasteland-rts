using Unity.Entities;
using Unity.Rendering;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Packed VAT clip state for <c>_AnimIndex</c>. Presentation-only.
    /// Stored as IEEE bits of a packed uint via <c>math.asfloat</c>.
    /// </summary>
    [MaterialProperty("_AnimIndex")]
    public struct VatClipState : IComponentData
    {
        public float Value;
    }
}
