#nullable disable
using Wasteland.Sim.Events;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Readable copy for a sim rejection. Mapped from
    /// <see cref="CommandRejectReason"/> only. AI reject enums are not used.
    /// </summary>
    public static class PlayerRejectText
    {
        public static string Describe(CommandRejectReason reason, int param0, int param1)
        {
            switch (reason)
            {
                case CommandRejectReason.None:
                    return string.Empty;
                case CommandRejectReason.TooLate:
                    return "That order was issued too late to enter the queue.";
                case CommandRejectReason.UnknownBattalion:
                    return "No battalion matches that order.";
                case CommandRejectReason.DuplicateBattalion:
                    return "That battalion id is already in use.";
                case CommandRejectReason.NotOwner:
                    return "That battalion belongs to another seat.";
                case CommandRejectReason.NegativeSpeed:
                    return "A battalion cannot have a negative speed.";
                case CommandRejectReason.UnknownKind:
                    return "That order is not recognised.";
                case CommandRejectReason.TierLocked:
                    return "That needs tier " + param0 + ". This seat is tier " + param1 + ".";
                case CommandRejectReason.UnitNotRaisable:
                    return "That battalion cannot be raised.";
                case CommandRejectReason.PopCapReached:
                    return "Population cap is " + param0 + " and " + param1 + " is already in use.";
                case CommandRejectReason.InsufficientManpower:
                    return "Need " + param0 + " manpower. This seat has " + param1 + ".";
                case CommandRejectReason.InsufficientMaterials:
                    return "Need " + param0 + " materiel. This seat has " + param1 + ".";
                case CommandRejectReason.InsufficientFuel:
                    return "Need " + param0 + " fuel. This seat has " + param1 + ".";
                case CommandRejectReason.UnknownSeat:
                    return "No seat is bound for that order.";
                case CommandRejectReason.NoPath:
                    return "No road path from node " + param0 + " to node " + param1 + ".";
                case CommandRejectReason.UnknownTown:
                    return "No town matches that order.";
                case CommandRejectReason.NotCapturer:
                    return "That battalion cannot claim a town.";
                case CommandRejectReason.AlreadyOwned:
                    return "That town is already yours.";
                case CommandRejectReason.NotBuilder:
                    return "That battalion cannot build a depot.";
                case CommandRejectReason.BuildingNotBuildable:
                    return "A supply depot is not available to this seat.";
                case CommandRejectReason.UnknownNode:
                    return "No road node matches that order.";
                case CommandRejectReason.TerritoryNotOwned:
                    return "That road node is outside territory this seat owns.";
                case CommandRejectReason.NotConnectedToSupply:
                    return "That road node is outside supply range.";
                case CommandRejectReason.DepotTooClose:
                    return "That site is too close to another depot.";
                case CommandRejectReason.NodeHasDepot:
                    return "That road node already has a depot.";
                case CommandRejectReason.DepotCapReached:
                    return "Depot cap is " + param0 + " and " + param1 + " are already counted.";
                case CommandRejectReason.NotSeatTown:
                    return "That town is not a territory seat.";
                case CommandRejectReason.UnknownTarget:
                    return "That attack target is not a living battalion.";
                case CommandRejectReason.TargetNotEnemy:
                    return "That battalion is not an enemy.";
                case CommandRejectReason.NotArmed:
                    return "That battalion has no weapon.";
                case CommandRejectReason.TargetIsSelf:
                    return "A battalion cannot attack itself.";
                case CommandRejectReason.ResearchInProgress:
                    return "Research is already in progress.";
                case CommandRejectReason.TierAlreadyReached:
                    return "This seat already has that tier.";
                case CommandRejectReason.NoQualifyingTier:
                    return "No owned holding qualifies for this research.";
                default:
                    return "Order rejected (" + (int)reason + ").";
            }
        }
    }
}
