#nullable disable
using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Orders a human seat may issue. Same set the AI submits. Spawn and the
    /// reserved Build kind are not included.
    /// </summary>
    public static class PlayerOrderSet
    {
        public const string TierTwoTechId = "tier2";

        public static bool IsAllowed(CommandKind kind)
        {
            switch (kind)
            {
                case CommandKind.Move:
                case CommandKind.Attack:
                case CommandKind.ClaimTown:
                case CommandKind.BuildDepot:
                case CommandKind.RaiseBattalion:
                case CommandKind.ResearchTech:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Kenney UI click. Played for a human seat only, never for an AI order.
    /// </summary>
    public static class OrderClick
    {
        public const string KenneyFileName = "ui_click.wav";

        public static bool ForHumanSeat(bool humanSeat)
        {
            return humanSeat;
        }
    }

    /// <summary>Selection ring: MarkerArt bone, then ink. No extra colour.</summary>
    public static class SelectionOutline
    {
        public static float BonePx
        {
            get { return MarkerArt.SelectionBonePx; }
        }

        public static float InkPx
        {
            get { return MarkerArt.SelectionInkPx; }
        }

        public static float PadPx
        {
            get { return MarkerArt.SelectionBonePx + MarkerArt.SelectionInkPx; }
        }

        public static Rgb Bone
        {
            get { return Rgb.FromHex(MarkerArt.Bone); }
        }

        public static Rgb Ink
        {
            get { return Rgb.FromHex(MarkerArt.Ink); }
        }
    }

    /// <summary>Ink, or the faction colour. Neutral grey is never returned.</summary>
    public static class OrderLineColor
    {
        public static Rgb Resolve(Rgb faction)
        {
            Rgb neutral = Rgb.FromHex(MarkerArt.Neutral);
            if (faction.R == neutral.R && faction.G == neutral.G && faction.B == neutral.B)
            {
                return Rgb.FromHex(MarkerArt.Ink);
            }

            return faction;
        }
    }

    public readonly struct OrderLine
    {
        public readonly int FromXCm;
        public readonly int FromYCm;
        public readonly int ToXCm;
        public readonly int ToYCm;
        public readonly Rgb Color;

        public OrderLine(int fromXCm, int fromYCm, int toXCm, int toYCm, Rgb color)
        {
            FromXCm = fromXCm;
            FromYCm = fromYCm;
            ToXCm = toXCm;
            ToYCm = toYCm;
            Color = OrderLineColor.Resolve(color);
        }
    }

    public readonly struct PlayerOrderNotice
    {
        public readonly bool Accepted;
        public readonly string Reason;
        public readonly bool PlayClick;
        public readonly bool LowVolume;
        public readonly SimCommand Command;

        public PlayerOrderNotice(bool accepted, string reason, bool playClick, bool lowVolume, SimCommand command)
        {
            Accepted = accepted;
            Reason = reason ?? string.Empty;
            PlayClick = playClick;
            LowVolume = lowVolume;
            Command = command;
        }
    }

    /// <summary>
    /// The player mutation path. Implementations may only enqueue commands.
    /// </summary>
    public interface IPlayerCommandStream
    {
        int Tick { get; }

        int CommandDelayTicks { get; }

        SimCommand Submit(SimCommand command);

        SimCommand SubmitRaiseBattalion(int factionId, string unitId, int rallyXCm, int rallyYCm);

        SimCommand SubmitResearchTech(int factionId, string techId);

        bool TryGetRejection(int issuerId, int sequence, out CommandRejectReason code, out int param0, out int param1);
    }

    /// <summary>
    /// Forwards player orders through <see cref="SimWorld.Submit"/>, the same
    /// queue the AI uses. Delay is the world's <c>commandDelayTicks</c>.
    /// </summary>
    public sealed class SimPlayerCommandStream : IPlayerCommandStream
    {
        readonly SimWorld _world;

        public SimPlayerCommandStream(SimWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            _world = world;
        }

        public int Tick
        {
            get { return _world.Tick; }
        }

        public int CommandDelayTicks
        {
            get { return _world.CommandDelayTicks; }
        }

        public SimCommand Submit(SimCommand command)
        {
            return _world.Submit(command);
        }

        public SimCommand SubmitRaiseBattalion(int factionId, string unitId, int rallyXCm, int rallyYCm)
        {
            return _world.SubmitRaiseBattalion(factionId, unitId, rallyXCm, rallyYCm);
        }

        public SimCommand SubmitResearchTech(int factionId, string techId)
        {
            return _world.SubmitResearchTech(factionId, techId);
        }

        public bool TryGetRejection(int issuerId, int sequence, out CommandRejectReason code, out int param0, out int param1)
        {
            ulong key = ((ulong)(uint)issuerId << 32) | (uint)sequence;
            IReadOnlyList<SimEvent> events = _world.Events;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                SimEvent ev = events[i];
                if (ev.Kind != SimEventKind.CommandRejected || ev.Key != key)
                {
                    continue;
                }

                code = ev.RejectReason;
                param0 = ev.Param0;
                param1 = ev.Param1;
                return true;
            }

            code = CommandRejectReason.None;
            param0 = 0;
            param1 = 0;
            return false;
        }
    }

    /// <summary>
    /// Human orders: move, attack, claim town, build depot, raise, Tier II research.
    /// Move and depot targets snap to a road node found on the live graph.
    /// </summary>
    public sealed class PlayerOrderSession
    {
        readonly IPlayerCommandStream _stream;
        readonly int _humanSeat;
        readonly bool _hasHuman;
        readonly RoadGraph _roads;
        readonly List<SimCommand> _pending = new List<SimCommand>();
        readonly List<OrderLine> _lines = new List<OrderLine>();
        int _selectedBattalion;

        public PlayerOrderSession(IPlayerCommandStream stream, int humanSeat, bool hasHuman, RoadGraph roads)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            _stream = stream;
            _humanSeat = humanSeat;
            _hasHuman = hasHuman;
            _roads = roads;
        }

        public int SelectedBattalionId
        {
            get { return _selectedBattalion; }
        }

        public int HumanSeat
        {
            get { return _humanSeat; }
        }

        public bool HasHuman
        {
            get { return _hasHuman; }
        }

        public void SelectBattalion(int battalionId)
        {
            _selectedBattalion = battalionId;
        }

        public void ClearSelection()
        {
            _selectedBattalion = 0;
        }

        public OrderLine[] Lines()
        {
            return _lines.ToArray();
        }

        public static string TierTwoLabel(BalanceCatalog catalog)
        {
            if (catalog == null
                || !catalog.TryGetTechCost(PlayerOrderSet.TierTwoTechId, out int manpower, out int fuel, out int materiel)
                || !catalog.TryGetTechResearchTicks(PlayerOrderSet.TierTwoTechId, out int ticks))
            {
                return "Research Tier II";
            }

            return "Research Tier II (" + fuel + " fuel, " + ticks + " ticks)";
        }

        public PlayerOrderNotice Move(int fromXCm, int fromYCm, int clickXCm, int clickYCm, Rgb faction)
        {
            if (!Ready(out PlayerOrderNotice blocked))
            {
                return blocked;
            }

            if (!TrySnap(clickXCm, clickYCm, out int nodeId, out int nodeX, out int nodeY))
            {
                return Local(nodeId == 0 && (_roads == null || _roads.NodeCount < 1)
                    ? "No road graph is loaded."
                    : "No road node is near that point.");
            }

            SimCommand command = SimCommand.Move(_stream.Tick, _humanSeat, _selectedBattalion, nodeX, nodeY);
            return Finish(_stream.Submit(command), fromXCm, fromYCm, nodeX, nodeY, faction, true);
        }

        public PlayerOrderNotice Attack(int fromXCm, int fromYCm, int targetBattalionId, int targetXCm, int targetYCm, Rgb faction)
        {
            if (!Ready(out PlayerOrderNotice blocked))
            {
                return blocked;
            }

            if (targetBattalionId == 0)
            {
                return Local("Choose an enemy battalion.");
            }

            SimCommand command = SimCommand.Attack(_stream.Tick, _humanSeat, _selectedBattalion, targetBattalionId);
            return Finish(_stream.Submit(command), fromXCm, fromYCm, targetXCm, targetYCm, faction, true);
        }

        public PlayerOrderNotice ClaimTown(int fromXCm, int fromYCm, int townId, int townXCm, int townYCm, Rgb faction)
        {
            if (!Ready(out PlayerOrderNotice blocked))
            {
                return blocked;
            }

            if (townId == 0)
            {
                return Local("Choose a town.");
            }

            SimCommand command = SimCommand.ClaimTown(_stream.Tick, _humanSeat, _selectedBattalion, townId);
            return Finish(_stream.Submit(command), fromXCm, fromYCm, townXCm, townYCm, faction, true);
        }

        public PlayerOrderNotice BuildDepot(int fromXCm, int fromYCm, int clickXCm, int clickYCm, Rgb faction)
        {
            if (!Ready(out PlayerOrderNotice blocked))
            {
                return blocked;
            }

            if (!TrySnap(clickXCm, clickYCm, out int nodeId, out int nodeX, out int nodeY))
            {
                return Local("No road node is near that point.");
            }

            SimCommand command = SimCommand.BuildDepot(_stream.Tick, _humanSeat, _selectedBattalion, nodeId);
            return Finish(_stream.Submit(command), fromXCm, fromYCm, nodeX, nodeY, faction, true);
        }

        public PlayerOrderNotice Raise(string unitId, int clickXCm, int clickYCm, Rgb faction)
        {
            if (!_hasHuman)
            {
                return Local("This match has no human seat.");
            }

            if (string.IsNullOrEmpty(unitId))
            {
                return Local("Name a battalion to raise.");
            }

            if (!TrySnap(clickXCm, clickYCm, out int nodeId, out int nodeX, out int nodeY))
            {
                return Local("No road node is near that point.");
            }

            return Finish(
                _stream.SubmitRaiseBattalion(_humanSeat, unitId, nodeX, nodeY),
                nodeX,
                nodeY,
                nodeX,
                nodeY,
                faction,
                false);
        }

        public PlayerOrderNotice ResearchTierTwo()
        {
            if (!_hasHuman)
            {
                return Local("This match has no human seat.");
            }

            return Finish(
                _stream.SubmitResearchTech(_humanSeat, PlayerOrderSet.TierTwoTechId),
                0,
                0,
                0,
                0,
                Rgb.FromHex(MarkerArt.Ink),
                false);
        }

        /// <summary>Spawn and the reserved Build kind never enter the queue.</summary>
        public PlayerOrderNotice Refuse(CommandKind kind)
        {
            if (PlayerOrderSet.IsAllowed(kind))
            {
                return Local("Use the matching order for " + kind + ".");
            }

            return Local("That order is not available.");
        }

        /// <summary>
        /// A rejection that landed on a later tick. Empty when nothing new failed.
        /// </summary>
        public string PollRejection()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                SimCommand command = _pending[i];
                CommandRejectReason code;
                int param0;
                int param1;
                if (_stream.TryGetRejection(command.IssuerId, command.Sequence, out code, out param0, out param1)
                    && code != CommandRejectReason.None)
                {
                    _pending.RemoveAt(i);
                    return PlayerRejectText.Describe(code, param0, param1);
                }

                if (_stream.Tick > command.ExecuteTick)
                {
                    _pending.RemoveAt(i);
                    i--;
                }
            }

            return string.Empty;
        }

        bool Ready(out PlayerOrderNotice blocked)
        {
            if (!_hasHuman)
            {
                blocked = Local("This match has no human seat.");
                return false;
            }

            if (_selectedBattalion == 0)
            {
                blocked = Local("Select a battalion first.");
                return false;
            }

            blocked = default(PlayerOrderNotice);
            return true;
        }

        bool TrySnap(int xCm, int yCm, out int nodeId, out int nodeXCm, out int nodeYCm)
        {
            nodeXCm = 0;
            nodeYCm = 0;
            if (_roads == null)
            {
                nodeId = 0;
                return false;
            }

            nodeId = _roads.NearestNodeId(xCm, yCm);
            if (nodeId == 0 || !_roads.TryGetNodeIndex(nodeId, out int index))
            {
                return false;
            }

            nodeXCm = _roads.GetNodeXCm(index);
            nodeYCm = _roads.GetNodeYCm(index);
            return true;
        }

        PlayerOrderNotice Local(string reason)
        {
            return new PlayerOrderNotice(false, reason, OrderClick.ForHumanSeat(_hasHuman), true, default(SimCommand));
        }

        PlayerOrderNotice Finish(SimCommand stamped, int fromX, int fromY, int toX, int toY, Rgb faction, bool drawLine)
        {
            CommandRejectReason code;
            int param0;
            int param1;
            if (_stream.TryGetRejection(stamped.IssuerId, stamped.Sequence, out code, out param0, out param1)
                && code != CommandRejectReason.None)
            {
                return new PlayerOrderNotice(
                    false,
                    PlayerRejectText.Describe(code, param0, param1),
                    OrderClick.ForHumanSeat(_hasHuman),
                    true,
                    stamped);
            }

            _pending.Add(stamped);
            if (drawLine)
            {
                _lines.Add(new OrderLine(fromX, fromY, toX, toY, faction));
            }

            return new PlayerOrderNotice(true, string.Empty, OrderClick.ForHumanSeat(_hasHuman), false, stamped);
        }
    }
}
