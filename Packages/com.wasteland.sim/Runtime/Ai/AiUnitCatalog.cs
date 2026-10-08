using System;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Fixture unit rows for tests. Production load uses
    /// <see cref="BalanceAiUnitCatalog"/> over <c>BalanceCatalog</c>.
    /// </summary>
    public sealed class AiUnitCatalog : IAiUnitCatalog
    {
        public static readonly AiUnitCatalog Empty = new AiUnitCatalog(
            Array.Empty<string>(),
            Array.Empty<int>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<int>(),
            Array.Empty<int>());

        private readonly string[] _ids;
        private readonly int[] _tiers;
        private readonly string[] _roles;
        private readonly string[] _targetClasses;
        private readonly int[] _manpower;
        private readonly int[] _pop;

        private AiUnitCatalog(
            string[] ids,
            int[] tiers,
            string[] roles,
            string[] targetClasses,
            int[] manpower,
            int[] pop)
        {
            _ids = ids;
            _tiers = tiers;
            _roles = roles;
            _targetClasses = targetClasses;
            _manpower = manpower;
            _pop = pop;
        }

        public int Count
        {
            get { return _ids.Length; }
        }

        public static AiUnitCatalog FromRows(params AiUnitCatalogRow[] rows)
        {
            if (rows == null || rows.Length < 1)
            {
                return Empty;
            }

            string[] ids = new string[rows.Length];
            int[] tiers = new int[rows.Length];
            string[] roles = new string[rows.Length];
            string[] targetClasses = new string[rows.Length];
            int[] manpower = new int[rows.Length];
            int[] pop = new int[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                AiUnitCatalogRow row = rows[i];
                if (string.IsNullOrEmpty(row.Id))
                {
                    throw new FormatException("unit id must be a non-empty string.");
                }

                if (row.TechTier < 1)
                {
                    throw new FormatException("techTier must be >= 1.");
                }

                string role = row.Role ?? string.Empty;
                string targetClass = row.TargetClass ?? string.Empty;
                RequireKnownRole(row.Id, role);
                RequireKnownTargetClass(row.Id, targetClass);
                if (row.ManpowerCost < 0)
                {
                    throw new FormatException("units.json $.units." + row.Id + ".cost.manpower must be >= 0.");
                }

                ids[i] = row.Id;
                tiers[i] = row.TechTier;
                roles[i] = role;
                targetClasses[i] = targetClass;
                manpower[i] = row.ManpowerCost;
                pop[i] = 1;
            }

            SortRows(ids, tiers, roles, targetClasses, manpower, pop);
            RequireUniqueIds(ids);
            return new AiUnitCatalog(ids, tiers, roles, targetClasses, manpower, pop);
        }

        public bool TryGetTier(string unitId, out int tier)
        {
            int index = IndexOf(unitId);
            if (index < 0)
            {
                tier = 0;
                return false;
            }

            tier = _tiers[index];
            return true;
        }

        public bool TryGetRole(string unitId, out string role)
        {
            int index = IndexOf(unitId);
            if (index < 0)
            {
                role = string.Empty;
                return false;
            }

            role = _roles[index];
            return true;
        }

        public bool TryGetTargetClass(string unitId, out string targetClass)
        {
            int index = IndexOf(unitId);
            if (index < 0)
            {
                targetClass = string.Empty;
                return false;
            }

            targetClass = _targetClasses[index];
            return true;
        }

        public bool TryGetManpowerCost(string unitId, out int manpower)
        {
            int index = IndexOf(unitId);
            if (index < 0)
            {
                manpower = 0;
                return false;
            }

            manpower = _manpower[index];
            return true;
        }

        public bool TryGetPopCost(string unitId, out int pop)
        {
            int index = IndexOf(unitId);
            if (index < 0)
            {
                pop = 0;
                return false;
            }

            pop = _pop[index];
            return true;
        }

        /// <summary>
        /// Mix keys only on line / AT / garrison / engineer. Extra data-pack
        /// roles are accepted so the shared <c>units.json</c> document loads;
        /// they are never treated as rifle.
        /// </summary>
        internal static void RequireKnownRole(string unitId, string role)
        {
            for (int i = 0; i < AiConstants.UnitRoles.Length; i++)
            {
                if (role == AiConstants.UnitRoles[i])
                {
                    return;
                }
            }

            throw new FormatException(
                "units.json $.units." + unitId + " unknown role '" + role + "'.");
        }

        internal static void RequireKnownTargetClass(string unitId, string targetClass)
        {
            if (targetClass == AiConstants.TargetClassArmored
                || targetClass == AiConstants.TargetClassSoft)
            {
                return;
            }

            throw new FormatException(
                "units.json $.units." + unitId + " unknown targetClass '" + targetClass + "'.");
        }

        private int IndexOf(string unitId)
        {
            if (unitId == null)
            {
                throw new ArgumentNullException(nameof(unitId));
            }

            int lo = 0;
            int hi = _ids.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int cmp = string.CompareOrdinal(_ids[mid], unitId);
                if (cmp == 0)
                {
                    return mid;
                }

                if (cmp < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return -1;
        }

        private static void SortRows(
            string[] ids,
            int[] tiers,
            string[] roles,
            string[] targetClasses,
            int[] manpower,
            int[] pop)
        {
            for (int i = 1; i < ids.Length; i++)
            {
                string id = ids[i];
                int tier = tiers[i];
                string role = roles[i];
                string targetClass = targetClasses[i];
                int mp = manpower[i];
                int popCost = pop[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(ids[j], id) > 0)
                {
                    ids[j + 1] = ids[j];
                    tiers[j + 1] = tiers[j];
                    roles[j + 1] = roles[j];
                    targetClasses[j + 1] = targetClasses[j];
                    manpower[j + 1] = manpower[j];
                    pop[j + 1] = pop[j];
                    j--;
                }

                ids[j + 1] = id;
                tiers[j + 1] = tier;
                roles[j + 1] = role;
                targetClasses[j + 1] = targetClass;
                manpower[j + 1] = mp;
                pop[j + 1] = popCost;
            }
        }

        private static void RequireUniqueIds(string[] ids)
        {
            for (int i = 1; i < ids.Length; i++)
            {
                if (ids[i] == ids[i - 1])
                {
                    throw new FormatException("Duplicate units.json id '" + ids[i] + "'.");
                }
            }
        }
    }

    public readonly struct AiUnitCatalogRow
    {
        public readonly string Id;
        public readonly int TechTier;
        public readonly string Role;
        public readonly string TargetClass;
        public readonly int ManpowerCost;

        public AiUnitCatalogRow(
            string id,
            int techTier,
            string role,
            string targetClass,
            int manpowerCost)
        {
            Id = id;
            TechTier = techTier;
            Role = role;
            TargetClass = targetClass;
            ManpowerCost = manpowerCost;
        }
    }
}
