using System;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Read-only mirror of live seat manpower. The sim pays income. This
    /// type does not accrue a second stock. <see cref="IncomePerMinute"/> is
    /// the planning rate: HQ plus holding tiers, then faction and difficulty
    /// ppt. With a world, the tier is <see cref="TownView.Tier"/> (the band
    /// checked against <c>derived.territories[].places</c>), not map
    /// population class. Contested holdings pay only when
    /// <c>contestedHoldingPaysIncome</c> is true.
    /// </summary>
    public sealed class AiEconomyStub : IAiEconomy
    {
        private readonly AiMapSnapshot _map;
        private readonly AiOwnedState _owned;
        private readonly AiBalanceTables _balance;
        private readonly ICaptureOccupancyView _occupancy;
        private readonly ISimWorldView? _world;

        public AiEconomyStub(
            AiMapSnapshot map,
            AiOwnedState owned,
            AiBalanceTables balance,
            ICaptureOccupancyView occupancy)
            : this(map, owned, balance, occupancy, null)
        {
        }

        public AiEconomyStub(
            AiMapSnapshot map,
            AiOwnedState owned,
            AiBalanceTables balance,
            ICaptureOccupancyView occupancy,
            ISimWorldView? world)
        {
            _map = map;
            _owned = owned;
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
            _world = world;
        }

        public int IncomePerMinute(int factionId)
        {
            AiFactionState faction = _owned.GetFaction(factionId);
            AiPersonalityProfile personality = _balance.GetPersonalityOrGeneric(faction.PersonalityId);
            int baseIncome = _balance.HqManpowerPerMin(personality.EconomyFactionId);
            bool contestedPays = _balance.ContestedHoldingPaysIncome;
            for (int i = 0; i < _map.TownCount; i++)
            {
                AiTownRecord town = _map.TownAt(i);
                if (_world != null)
                {
                    if (!_world.TryGetTown(town.Id, out TownView view))
                    {
                        continue;
                    }

                    if (view.OwnerFactionId != factionId || view.Tier < 0)
                    {
                        continue;
                    }

                    if (!contestedPays && view.Contested)
                    {
                        continue;
                    }

                    baseIncome += _balance.IncomePerMinForClass(view.Tier);
                    continue;
                }

                if (_owned.TownOwnerAt(i) != factionId)
                {
                    continue;
                }

                if (!contestedPays && HoldingIsContested(i))
                {
                    continue;
                }

                // Planning-only tests. Live play uses TownView.Tier (scenario place bands).
                baseIncome += _balance.IncomePerMinForClass(town.PopulationClass);
            }

            int modified = ApplyPpt(baseIncome, _balance.ManpowerModifierPpt(personality.EconomyFactionId));
            return ApplyPpt(modified, _balance.IncomeMultiplierPpt(faction.DifficultyId));
        }

        public void AccrueTick(int tick)
        {
            if (_world == null || tick < 0)
            {
                return;
            }

            for (int i = 0; i < _owned.FactionCount; i++)
            {
                AiFactionState faction = _owned.FactionAt(i);
                if (_world.TryGetSeat(faction.FactionId, out SeatView live))
                {
                    faction.Manpower = live.Manpower;
                }
            }
        }

        private bool HoldingIsContested(int townIndex)
        {
            AiTownRecord town = _map.TownAt(townIndex);
            if (_world != null && _world.TryGetTown(town.Id, out TownView view))
            {
                return view.Contested;
            }

            CaptureZoneOccupancyResult zone = _occupancy.OccupancyOf(
                town.XCm,
                town.YCm,
                _balance.CaptureRadiusCmForClass(town.PopulationClass));
            return zone.Kind == CaptureOccupancyKind.Contested;
        }

        private static int ApplyPpt(int value, int ppt)
        {
            if (ppt == SimConfig.PptScale)
            {
                return value;
            }

            return (int)(((long)value * ppt) / SimConfig.PptScale);
        }
    }
}
