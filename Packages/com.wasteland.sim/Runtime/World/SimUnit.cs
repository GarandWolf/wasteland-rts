namespace Wasteland.Sim.World
{
    /// <summary>
    /// Sim unit. Belongs to a <see cref="SimBattalion"/>; this skeleton stores
    /// membership only. The battalion centroid is the position of record.
    /// </summary>
    public struct SimUnit
    {
        public int Id;
        public int BattalionId;
        public int OwnerId;
    }
}
