using System;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// AI-owned manpower until the sim owns economy. Rates come from
    /// <c>economy.json</c> as per-minute integers. Accrual is remainder-carry
    /// on <c>incomeAccrual.intervalTicks</c>. Difficulty applies
    /// <c>rules.json</c> <c>difficulty.*.incomeMultiplierPpt</c>. Contested
    /// holdings pay only when <c>contestedHoldingPaysIncome</c> is true.
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
                if (_owned.TownOwnerAt(i) != factionId)
                {
                    continue;
                }

                if (!contestedPays && HoldingIsContested(i))
                {
                    continue;
                }

                baseIncome += _balance.IncomePerMinForClass(_map.TownAt(i).PopulationClass);
            }

            int modified = ApplyPpt(baseIncome, _balance.ManpowerModifierPpt(personality.EconomyFactionId));
            return ApplyPpt(modified, _balance.IncomeMultiplierPpt(faction.DifficultyId));
        }

        public void AccrueTick(int tick)
        {
            int interval = _balance.AccrualIntervalTicks;
            if (tick < 1 || interval < 1 || (tick % interval) != 0)
            {
                return;
            }

            int perMinute = _balance.AccrualIntervalsPerMinute;
            for (int i = 0; i < _owned.FactionCount; i++)
            {
                AiFactionState faction = _owned.FactionAt(i);
                int income = IncomePerMinute(faction.FactionId);
                faction.ManpowerCarry += income;
                int gained = faction.ManpowerCarry / perMinute;
                if (gained != 0)
                {
                    faction.Manpower += gained;
                    faction.ManpowerCarry -= gained * perMinute;
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
