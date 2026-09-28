namespace Wasteland.Sim.World
{
    /// <summary>
    /// Sim unit. Belongs to a <see cref="SimSquad"/>; this skeleton stores
    /// membership only. The squad centroid is the position of record.
    /// </summary>
    public struct SimUnit
    {
        public int Id;
        public int SquadId;
        public int OwnerId;
    }
}
