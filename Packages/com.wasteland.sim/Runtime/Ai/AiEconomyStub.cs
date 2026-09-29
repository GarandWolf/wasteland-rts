namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// AI-owned manpower until the sim owns economy. Brutal bonus is
    /// <c>base * (1000 + EconomyBonusPpt) / 1000</c>.
    /// </summary>
    public sealed class AiEconomyStub : IAiEconomy
    {
        private readonly AiMapSnapshot _map;
        private readonly AiOwnedState _owned;

        public AiEconomyStub(AiMapSnapshot map, AiOwnedState owned)
        {
            _map = map;
            _owned = owned;
        }

        public int IncomePerSecond(int factionId)
        {
            int baseIncome = 0;
            for (int i = 0; i < _map.TownCount; i++)
            {
                if (_owned.TownOwnerAt(i) != factionId)
                {
                    continue;
                }

                baseIncome += AiMapSnapshot.IncomePerSecondForClass(_map.TownAt(i).PopulationClass);
            }

            AiFactionState faction = _owned.GetFaction(factionId);
            return ApplyEconomyBonus(baseIncome, faction.EconomyBonusPpt);
        }

        public void AccrueTick()
        {
            for (int i = 0; i < _owned.FactionCount; i++)
            {
                AiFactionState faction = _owned.FactionAt(i);
                int income = IncomePerSecond(faction.FactionId);
                faction.ManpowerCarry += income;
                int gained = faction.ManpowerCarry / AiConstants.TickRateHz;
                if (gained != 0)
                {
                    faction.Manpower += gained;
                    faction.ManpowerCarry -= gained * AiConstants.TickRateHz;
                }
            }
        }

        private static int ApplyEconomyBonus(int baseIncome, int economyBonusPpt)
        {
            if (economyBonusPpt == 0)
            {
                return baseIncome;
            }

            return (int)(((long)baseIncome * (SimConfig.PptScale + economyBonusPpt)) / SimConfig.PptScale);
        }
    }
}
