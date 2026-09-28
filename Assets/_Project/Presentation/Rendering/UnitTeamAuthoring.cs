using Unity.Entities;
using UnityEngine;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Authoring for the packed team-color override. Baker writes
    /// <see cref="TeamColorIndex"/>; the sim never sees this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitTeamAuthoring : MonoBehaviour
    {
        [Range(0, 3)] public int Faction;
        [Range(0, 3)] public int Shade;
        [Range(0, 3)] public int Pips;
        public bool Hatch;
        public bool Valid = true;

        public uint PackedValue
        {
            get
            {
                return TeamIndexPacking.Pack(Faction, Shade, Pips, Hatch, Valid);
            }
        }

        private class Baker : Baker<UnitTeamAuthoring>
        {
            public override void Bake(UnitTeamAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(entity, new TeamColorIndex { Value = authoring.PackedValue });
            }
        }
    }
}
