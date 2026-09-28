using Unity.Entities;
using Unity.Rendering;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Packed VAT clip state for <c>_AnimIndex</c>. Presentation-only.
    /// </summary>
    [MaterialProperty("_AnimIndex")]
    public struct VatClipState : IComponentData
    {
        public uint Value;
    }
}
