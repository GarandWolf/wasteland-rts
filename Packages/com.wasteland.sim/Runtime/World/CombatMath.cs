#nullable disable

namespace Wasteland.Sim.World
{
    /// <summary>
    /// Integer damage chain from balance-notes section 1.1.
    /// Every step multiplies, then divides, then floors.
    /// Veterancy, reinforcement, deploy ticks, and ammo cost are not applied.
    /// </summary>
    /// <remarks>
    /// TODO: port tools/sim.py golden_expected.txt damage vectors when that
    /// file is on main. The formula below is the spec, covered by unit tests.
    /// </remarks>
    public static class CombatMath
    {
        public const int ColumnSoft = 0;
        public const int ColumnArmored = 1;

        public static int ColumnOf(bool armoredOrStructure)
        {
            return armoredOrStructure ? ColumnArmored : ColumnSoft;
        }

        public static int AttackPpt(
            int indirectVsStructureBonusPpt,
            int armourAttackInUrbanPpt,
            int attackPptMax,
            bool indirect,
            bool targetIsStructure,
            bool attackerArmored,
            bool targetUrban)
        {
            long bonus = 1000;
            if (indirect && targetIsStructure)
            {
                bonus += indirectVsStructureBonusPpt;
            }

            if (attackerArmored && !indirect && targetUrban)
            {
                bonus += armourAttackInUrbanPpt;
            }

            if (bonus < 0)
            {
                bonus = 0;
            }

            if (bonus > attackPptMax)
            {
                bonus = attackPptMax;
            }

            return (int)bonus;
        }

        public static int DefensePpt(
            int infantryInUrban,
            int infantryInForest,
            int slopeDefense,
            int perFortificationLevel,
            int fordDefense,
            int defenseMin,
            bool softUnit,
            bool urban,
            bool forest,
            int slopePpt,
            int fortificationLevel,
            bool ford)
        {
            long sum = 0;
            if (softUnit && urban)
            {
                sum += infantryInUrban;
            }

            if (softUnit && forest && !urban)
            {
                sum += infantryInForest;
            }

            if (slopePpt >= 300 && slopePpt <= 599)
            {
                sum += slopeDefense;
            }

            if (fortificationLevel > 0)
            {
                sum += (long)perFortificationLevel * fortificationLevel;
            }

            if (ford)
            {
                sum += fordDefense;
            }

            if (sum < defenseMin)
            {
                sum = defenseMin;
            }

            return (int)sum;
        }

        public static int Volley(
            int baseDamage,
            int attackPpt,
            int armorPpt,
            int defensePpt,
            int maxDefensePpt,
            int attackerDm,
            int targetDm,
            int highGroundThresholdDm,
            int highGroundPpt,
            int minDamage)
        {
            long damage = baseDamage;
            damage = damage * attackPpt / 1000;
            damage = damage * (1000 - armorPpt) / 1000;
            int defense = defensePpt;
            if (defense > maxDefensePpt)
            {
                defense = maxDefensePpt;
            }

            long denom = 1000L + defense;
            if (denom < 1)
            {
                denom = 1;
            }

            damage = damage * 1000 / denom;
            if ((long)attackerDm - targetDm >= highGroundThresholdDm)
            {
                damage = damage * (1000L + highGroundPpt) / 1000;
            }

            if (damage < minDamage)
            {
                damage = minDamage;
            }

            if (damage > int.MaxValue)
            {
                damage = int.MaxValue;
            }

            return (int)damage;
        }

        public static int EffectiveReload(int reloadTicks, bool penalized, int fireRatePpt)
        {
            if (!penalized)
            {
                return reloadTicks;
            }

            int rate = fireRatePpt < 1 ? 1 : fireRatePpt;
            return (int)((long)reloadTicks * 1000 / rate);
        }

        public static int StrengthFromHp(int hp, int maxHp)
        {
            if (hp <= 0 || maxHp <= 0)
            {
                return 0;
            }

            return (int)((long)hp * 1000 / maxHp);
        }
    }
}
