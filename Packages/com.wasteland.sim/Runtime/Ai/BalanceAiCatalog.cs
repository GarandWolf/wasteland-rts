using System;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// <see cref="IAiUnitCatalog"/> backed by Friday's
    /// <see cref="BalanceCatalog"/> getters. Does not parse
    /// <c>units.json</c>.
    /// </summary>
    public sealed class BalanceAiUnitCatalog : IAiUnitCatalog
    {
        private readonly BalanceCatalog _inner;

        public BalanceAiUnitCatalog(BalanceCatalog inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public bool TryGetTier(string unitId, out int tier)
        {
            return _inner.TryGetUnitTier(unitId, out tier);
        }

        public bool TryGetRole(string unitId, out string role)
        {
            return _inner.TryGetUnitRole(unitId, out role);
        }

        public bool TryGetTargetClass(string unitId, out string targetClass)
        {
            return _inner.TryGetUnitTargetClass(unitId, out targetClass);
        }

        public bool TryGetManpowerCost(string unitId, out int manpower)
        {
            return _inner.TryGetManpowerCost(unitId, out manpower);
        }

        public bool TryGetPopCost(string unitId, out int pop)
        {
            if (!_inner.TryGetUnitIndex(unitId, out int index))
            {
                pop = 0;
                return false;
            }

            pop = _inner.UnitPopAt(index);
            return true;
        }
    }

    /// <summary>
    /// <see cref="IAiBuildingCatalog"/> backed by Friday's
    /// <see cref="BalanceCatalog.TryGetBuildingRole"/>. Does not parse
    /// <c>buildings.json</c>.
    /// </summary>
    public sealed class BalanceAiBuildingCatalog : IAiBuildingCatalog
    {
        private readonly BalanceCatalog _inner;

        public BalanceAiBuildingCatalog(BalanceCatalog inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public bool TryGetBuildingRole(string buildingId, out string role)
        {
            return _inner.TryGetBuildingRole(buildingId, out role);
        }
    }

    /// <summary>
    /// Closed 10-role / 7-role allowlists over a loaded
    /// <see cref="BalanceCatalog"/>. Missing roles already throw inside
    /// <see cref="BalanceCatalog"/>; unknown roles do not.
    /// </summary>
    public static class BalanceAiCatalog
    {
        public static void RequireClosedRoles(BalanceCatalog catalog, string buildingsJson)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (buildingsJson == null)
            {
                throw new ArgumentNullException(nameof(buildingsJson));
            }

            for (int i = 0; i < catalog.UnitCount; i++)
            {
                string id = catalog.UnitIdAt(i);
                if (!catalog.TryGetUnitRole(id, out string role))
                {
                    throw new FormatException("units.json $.units." + id + " missing role.");
                }

                AiUnitCatalog.RequireKnownRole(id, role);
                if (!catalog.TryGetUnitTargetClass(id, out string targetClass))
                {
                    throw new FormatException("units.json $.units." + id + " missing targetClass.");
                }

                AiUnitCatalog.RequireKnownTargetClass(id, targetClass);
            }

            JsonValue root = IntegerJsonParser.Parse(buildingsJson);
            JsonValue buildings = JsonUtil.Field(root, "buildings");
            if (buildings.Kind != JsonKind.Object)
            {
                throw new FormatException("buildings.json $.buildings must be an object.");
            }

            for (int i = 0; i < buildings.ObjectValue.Count; i++)
            {
                string id = buildings.ObjectValue[i].Name;
                if (!catalog.TryGetBuildingRole(id, out string role))
                {
                    throw new FormatException("buildings.json $.buildings." + id + " missing role.");
                }

                AiBuildingCatalog.RequireKnownRole(id, role);
            }
        }
    }
}
