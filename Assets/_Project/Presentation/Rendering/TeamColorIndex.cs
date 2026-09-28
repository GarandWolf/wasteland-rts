using Unity.Entities;
using Unity.Rendering;

namespace Wasteland.Presentation
{
    /// <summary>
    /// 4-byte Entities Graphics override for <c>_TeamIndex</c>.
    /// Stored as IEEE bits of a packed uint via <c>math.asfloat</c> so the
    /// shader <c>asuint</c> recovers flags. Written by presentation at spawn /
    /// ownership change. Never stored in sim.
    /// </summary>
    [MaterialProperty("_TeamIndex")]
    public struct TeamColorIndex : IComponentData
    {
        public float Value;
    }
}
