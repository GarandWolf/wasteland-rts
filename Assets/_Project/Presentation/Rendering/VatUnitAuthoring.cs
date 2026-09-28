using Unity.Entities;
using UnityEngine;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Authoring for VAT clip state. Vehicles should omit this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VatUnitAuthoring : MonoBehaviour
    {
        [Range(0, 63)] public int Clip;
        public bool Hold;
        public bool Valid = true;
        public float Speed = 1f;
        public float StartTime;

        public uint PackedClip
        {
            get
            {
                return VatClipPacking.Pack(Clip, Hold, Valid, Speed);
            }
        }

        private class Baker : Baker<VatUnitAuthoring>
        {
            public override void Bake(VatUnitAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(entity, new VatClipState { Value = authoring.PackedClip });
                AddComponent(entity, new VatStartTime { Value = authoring.StartTime });
            }
        }
    }
}
