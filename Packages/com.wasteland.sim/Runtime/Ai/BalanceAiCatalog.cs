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

        public bool TryGetMaterielCost(string unitId, out int materiel)
        {
            return _inner.TryGetMaterielCost(unitId, out materiel);
        }

        public bool TryGetFuelCost(string unitId, out int fuel)
        {
            return _inner.TryGetFuelCost(unitId, out fuel);
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

        public bool TryGetMovementClass(string unitId, out MovementClass moveClass)
        {
            if (!_inner.TryGetUnitIndex(unitId, out int index))
            {
                moveClass = default;
                return false;
            }

            moveClass = _inner.UnitMoveClassAt(index);
            return true;
        }

        public bool TryGetCanCapture(string unitId, out bool canCapture)
        {
            if (!_inner.TryGetUnitIndex(unitId, out int index))
            {
                canCapture = false;
                return false;
            }

            canCapture = _inner.UnitCanCaptureAt(index);
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
        private readonly int _depotManpower;
        private readonly int _depotMateriel;
        private readonly int _depotFuel;
        private readonly bool _haveDepotCost;

        public BalanceAiBuildingCatalog(BalanceCatalog inner)
            : this(inner, 0, 0, 0, false)
        {
        }

        public BalanceAiBuildingCatalog(
            BalanceCatalog inner,
            int depotManpower,
            int depotMateriel,
            int depotFuel)
            : this(inner, depotManpower, depotMateriel, depotFuel, true)
        {
        }

        private BalanceAiBuildingCatalog(
            BalanceCatalog inner,
            int depotManpower,
            int depotMateriel,
            int depotFuel,
            bool haveDepotCost)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _depotManpower = depotManpower;
            _depotMateriel = depotMateriel;
            _depotFuel = depotFuel;
            _haveDepotCost = haveDepotCost;
        }

        public bool TryGetBuildingRole(string buildingId, out string role)
        {
            return _inner.TryGetBuildingRole(buildingId, out role);
        }

        public bool TryGetBuildCost(string buildingId, out int manpower, out int materiel, out int fuel)
        {
            manpower = 0;
            materiel = 0;
            fuel = 0;
            if (!_haveDepotCost || buildingId != AiConstants.BuildingIdSupplyDepot)
            {
                return false;
            }

            manpower = _depotManpower;
            materiel = _depotMateriel;
            fuel = _depotFuel;
            return true;
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
