#nullable disable
using Wasteland.Sim.Events;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Player-facing copy for a sim rejection. Mapped from
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
                    return "That order was too late.";
                case CommandRejectReason.UnknownBattalion:
                    return "That battalion is not on the map.";
                case CommandRejectReason.DuplicateBattalion:
                    return "That battalion is already in the fight.";
                case CommandRejectReason.NotOwner:
                    return "That battalion is not yours.";
                case CommandRejectReason.NegativeSpeed:
                    return "A battalion cannot move backwards.";
                case CommandRejectReason.UnknownKind:
                    return "That is not an order you can give.";
                case CommandRejectReason.TierLocked:
                    return "Requires " + TierName(param0) + " research.";
                case CommandRejectReason.UnitNotRaisable:
                    return "You cannot raise that battalion.";
                case CommandRejectReason.PopCapReached:
                    return "You cannot field any more battalions. The limit is " + param0 + " and you already have " + param1 + ".";
                case CommandRejectReason.InsufficientManpower:
                    return "You need " + param0 + " manpower and have " + param1 + ".";
                case CommandRejectReason.InsufficientMaterials:
                    return "You need " + param0 + " materiel and have " + param1 + ".";
                case CommandRejectReason.InsufficientFuel:
                    return "You need " + param0 + " fuel and have " + param1 + ".";
                case CommandRejectReason.UnknownSeat:
                    return "You are not commanding a side in this match.";
                case CommandRejectReason.NoPath:
                    return "No road route to that point.";
                case CommandRejectReason.UnknownTown:
                    return "That town is not on the map.";
                case CommandRejectReason.NotCapturer:
                    return "That battalion cannot claim a town.";
                case CommandRejectReason.AlreadyOwned:
                    return "You already hold that town.";
                case CommandRejectReason.NotBuilder:
                    return "That battalion cannot build a depot.";
                case CommandRejectReason.BuildingNotBuildable:
                    return "You cannot build a depot.";
                case CommandRejectReason.UnknownNode:
                    return "That place is not on a road.";
                case CommandRejectReason.TerritoryNotOwned:
                    return "That place is outside land you hold.";
                case CommandRejectReason.NotConnectedToSupply:
                    return "That place is out of supply.";
                case CommandRejectReason.DepotTooClose:
                    return "That place is too close to another depot.";
                case CommandRejectReason.NodeHasDepot:
                    return "There is already a depot there.";
                case CommandRejectReason.DepotCapReached:
                    return "You cannot build any more depots. The limit is " + param0 + " and you already have " + param1 + ".";
                case CommandRejectReason.NotSeatTown:
                    return "That town cannot be claimed.";
                case CommandRejectReason.UnknownTarget:
                    return "There is nothing there to attack.";
                case CommandRejectReason.TargetNotEnemy:
                    return "That battalion is not an enemy.";
                case CommandRejectReason.NotArmed:
                    return "That battalion has no weapon.";
                case CommandRejectReason.TargetIsSelf:
                    return "A battalion cannot attack itself.";
                case CommandRejectReason.ResearchInProgress:
                    return "Research is already in progress.";
                case CommandRejectReason.TierAlreadyReached:
                    return "You already have that tier.";
                case CommandRejectReason.NoQualifyingTier:
                    return "You need a town of your own before you can research that.";
                default:
                    return "That order could not be carried out.";
            }
        }

        public static string TierName(int tier)
        {
            switch (tier)
            {
                case 1:
                    return "Tier I";
                case 2:
                    return "Tier II";
                case 3:
                    return "Tier III";
                default:
                    return "Tier " + tier;
            }
        }
    }
}
