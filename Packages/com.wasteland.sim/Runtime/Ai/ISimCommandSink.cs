using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// The only mutation path the AI may use. Implementations must forward
    /// spawn/move/claim/depot to the foundation <see cref="SimWorld.Submit"/>
    /// (or a test sink of it). <see cref="CommandKind.Build"/> is unused;
    /// depots use <see cref="CommandKind.BuildDepot"/>.
    /// </summary>
    public interface ISimCommandSink
    {
        int Tick { get; }

        int CommandDelayTicks { get; }

        SimCommand Submit(SimCommand command);

        /// <summary>
        /// Sequence assigned by the last <see cref="Submit"/> that entered the world queue.
        /// Pairs with <see cref="SimEvent.Key"/> on <see cref="SimEventKind.CommandRejected"/>.
        /// </summary>
        int LastSequence { get; }

        int AllocateBattalionId();

        SimCommand SubmitRaiseBattalion(int factionId, string unitId, int rallyXCm, int rallyYCm);

        SimCommand SubmitRaiseBattalion(int factionId, int battalionId, string unitId, int rallyXCm, int rallyYCm);

        bool TryGetRejection(int subjectId, out CommandRejectReason code, out int param0, out int param1);

        bool HasBattalionRaiseStarted(int battalionId);
    }

    /// <summary>
    /// Fog-honest world reads. Enemy live intent stays hidden.
    /// </summary>
    public interface ISimWorldView
    {
        int Tick { get; }

        int BattalionCount { get; }

        void CopyOwnBattalionIds(int factionId, List<int> into);

        void CopyVisibleEnemyBattalionIds(int factionId, List<int> into);

        int CountOwnBattalions(int factionId);

        bool TryGetOwnBattalion(int factionId, int battalionId, out SimBattalion battalion);

        bool TryGetSeat(int factionId, out SeatView view);

        bool TryGetBattalionView(int battalionId, out BattalionView view);

        bool TryCopyProductionQueue(int factionId, List<ProductionQueueItem> into);

        int TownCount { get; }

        bool TryGetTown(int townId, out TownView view);

        void CopyTowns(List<TownView> into);

        bool TryGetTerritory(ulong key, out TerritoryView view);

        void CopyDepots(List<DepotView> into);

        /// <summary>
        /// Events on one tick from the retained history. The AI reads tick t-1 at the start of tick t.
        /// </summary>
        void CopyEventsOfTick(int tick, List<SimEvent> into);

        /// <summary>
        /// Events in <c>[fromTick, toTick]</c> from the last 64 ticks, ordered by event id.
        /// Derived state: not hashed and not saved. Replay rebuilds it by stepping.
        /// </summary>
        void CopyEventsInRange(int fromTick, int toTick, List<SimEvent> into);

        /// <summary>
        /// Map territory id of a road node, or 0 if the node is unknown.
        /// Bind the match road graph on the sink before the first tick.
        /// </summary>
        int GetNodeTerritoryId(int nodeId);

        CommandRejectReason PreviewDepotPlacement(int factionId, int nodeId, out int param0, out int param1);
    }

    /// <summary>
    /// Capture occupancy is a world process, not vision. The AI may ask about
    /// zones it already knows; the view returns only empty / one faction /
    /// contested. Battalion identities and positions are not exposed.
    /// </summary>
    public enum CaptureOccupancyKind : byte
    {
        Empty = 0,
        OneFaction = 1,
        Contested = 2
    }

    public readonly struct CaptureZoneOccupancyResult
    {
        public readonly CaptureOccupancyKind Kind;
        public readonly int FactionId;

        public CaptureZoneOccupancyResult(CaptureOccupancyKind kind, int factionId)
        {
            Kind = kind;
            FactionId = factionId;
        }
    }

    public interface ICaptureOccupancyView
    {
        CaptureZoneOccupancyResult OccupancyOf(int xCm, int yCm, int radiusCm);
    }

    /// <summary>
    /// Manpower / income until sim economy exists. Designer rates are per minute.
    /// </summary>
    public interface IAiEconomy
    {
        int IncomePerMinute(int factionId);

        void AccrueTick(int tick);
    }

}
