namespace Wasteland.Sim.Scenario
{
    /// <summary>
    /// Combat and supply-penalty numbers from <c>rules.json</c>.
    /// The sim does not keep its own copies of these.
    /// </summary>
    public sealed class CombatRules
    {
        internal CombatRules(
            int indirectVsStructureBonusPpt,
            int armourAttackInUrbanPpt,
            int attackPptMax,
            int maxDefensePpt,
            int minDamagePerVolley,
            int infantryInUrban,
            int infantryInForest,
            int slopeDefense,
            int perFortificationLevel,
            int fordDefense,
            int highGroundThresholdDm,
            int highGroundPpt,
            int defenseMin,
            int recheckIntervalTicks,
            int graceTicks,
            int fireRatePpt,
            int speedPpt,
            int hqRangeCm,
            int depotRangeCm,
            int unitLinkMaxOffRoadCm)
        {
            IndirectVsStructureBonusPpt = indirectVsStructureBonusPpt;
            ArmourAttackInUrbanPpt = armourAttackInUrbanPpt;
            AttackPptMax = attackPptMax;
            MaxDefensePpt = maxDefensePpt;
            MinDamagePerVolley = minDamagePerVolley;
            InfantryInUrban = infantryInUrban;
            InfantryInForest = infantryInForest;
            SlopeDefense = slopeDefense;
            PerFortificationLevel = perFortificationLevel;
            FordDefense = fordDefense;
            HighGroundThresholdDm = highGroundThresholdDm;
            HighGroundPpt = highGroundPpt;
            DefenseMin = defenseMin;
            RecheckIntervalTicks = recheckIntervalTicks;
            GraceTicks = graceTicks;
            FireRatePpt = fireRatePpt;
            SpeedPpt = speedPpt;
            HqRangeCm = hqRangeCm;
            DepotRangeCm = depotRangeCm;
            UnitLinkMaxOffRoadCm = unitLinkMaxOffRoadCm;
        }

        public int IndirectVsStructureBonusPpt { get; }

        public int ArmourAttackInUrbanPpt { get; }

        public int AttackPptMax { get; }

        public int MaxDefensePpt { get; }

        public int MinDamagePerVolley { get; }

        public int InfantryInUrban { get; }

        public int InfantryInForest { get; }

        public int SlopeDefense { get; }

        public int PerFortificationLevel { get; }

        public int FordDefense { get; }

        public int HighGroundThresholdDm { get; }

        public int HighGroundPpt { get; }

        public int DefenseMin { get; }

        public int RecheckIntervalTicks { get; }

        public int GraceTicks { get; }

        public int FireRatePpt { get; }

        public int SpeedPpt { get; }

        public int HqRangeCm { get; }

        public int DepotRangeCm { get; }

        public int UnitLinkMaxOffRoadCm { get; }
    }
}
