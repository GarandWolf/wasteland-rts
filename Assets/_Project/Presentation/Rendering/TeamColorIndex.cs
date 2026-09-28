using Unity.Entities;
using Unity.Rendering;

namespace Wasteland.Presentation
{
    /// <summary>
    /// 4-byte Entities Graphics override for <c>_TeamIndex</c>.
    /// Written by presentation at spawn / ownership change. Never stored in sim.
    /// </summary>
    [MaterialProperty("_TeamIndex")]
    public struct TeamColorIndex : IComponentData
    {
        public uint Value;
    }
}
