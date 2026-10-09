using System;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Ai
{
    /// <summary>
        /// Read-only building roles. Parses an already-loaded
        /// <c>buildings.json</c> document — this type never opens that file
        /// and keeps no static cache. Missing or unknown <c>role</c> throws.
    /// </summary>
    public sealed class AiBuildingCatalog : IAiBuildingCatalog
    {
        public static readonly AiBuildingCatalog Empty = new AiBuildingCatalog(
            Array.Empty<string>(),
            Array.Empty<string>(),
            0,
            0,
            0,
            false);

        private readonly string[] _ids;
        private readonly string[] _roles;
        private readonly int _depotManpower;
        private readonly int _depotMateriel;
        private readonly int _depotFuel;
        private readonly bool _haveDepotCost;

        private AiBuildingCatalog(
            string[] ids,
            string[] roles,
            int depotManpower,
            int depotMateriel,
            int depotFuel,
            bool haveDepotCost)
        {
            _ids = ids;
            _roles = roles;
            _depotManpower = depotManpower;
            _depotMateriel = depotMateriel;
            _depotFuel = depotFuel;
            _haveDepotCost = haveDepotCost;
        }

        public int Count
        {
            get { return _ids.Length; }
        }

        public static AiBuildingCatalog Parse(string buildingsJson)
        {
            if (buildingsJson == null)
            {
                throw new ArgumentNullException(nameof(buildingsJson));
            }

            JsonValue root = IntegerJsonParser.Parse(buildingsJson);
            JsonValue buildings = JsonUtil.Field(root, "buildings");
            if (buildings.Kind != JsonKind.Object || buildings.ObjectValue.Count < 1)
            {
                throw new FormatException("buildings.json $.buildings must be a non-empty object.");
            }

            int count = buildings.ObjectValue.Count;
            string[] ids = new string[count];
            string[] roles = new string[count];
            for (int i = 0; i < count; i++)
            {
                JsonField field = buildings.ObjectValue[i];
                string id = field.Name;
                if (string.IsNullOrEmpty(id))
                {
                    throw new FormatException("buildings.json building id must be a non-empty string.");
                }

                string role = RequireFieldString(field.Value, id, "role");
                RequireKnownRole(id, role);
                ids[i] = id;
                roles[i] = role;
            }

            SortRows(ids, roles);
            RequireUniqueIds(ids);
            int depotManpower;
            int depotMateriel;
            int depotFuel;
            bool haveDepotCost = TryReadDepotCost(
                buildingsJson,
                out depotManpower,
                out depotMateriel,
                out depotFuel);
            return new AiBuildingCatalog(ids, roles, depotManpower, depotMateriel, depotFuel, haveDepotCost);
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

        internal static bool TryReadDepotCost(
            string buildingsJson,
            out int manpower,
            out int materiel,
            out int fuel)
        {
            manpower = 0;
            materiel = 0;
            fuel = 0;
            if (buildingsJson == null)
            {
                return false;
            }

            JsonValue root = IntegerJsonParser.Parse(buildingsJson);
            if (!JsonUtil.TryField(root, "buildings", out JsonValue buildings)
                || buildings.Kind != JsonKind.Object)
            {
                return false;
            }

            if (!JsonUtil.TryField(buildings, AiConstants.BuildingIdSupplyDepot, out JsonValue row))
            {
                return false;
            }

            if (!JsonUtil.TryField(row, "cost", out JsonValue cost) || cost.Kind != JsonKind.Object)
            {
                return false;
            }

            manpower = JsonUtil.Int32(JsonUtil.Field(cost, "manpower"), "buildings.json $.buildings.supply_depot.cost.manpower");
            materiel = JsonUtil.Int32(JsonUtil.Field(cost, "materiel"), "buildings.json $.buildings.supply_depot.cost.materiel");
            fuel = JsonUtil.Int32(JsonUtil.Field(cost, "fuel"), "buildings.json $.buildings.supply_depot.cost.fuel");
            return true;
        }

        public bool TryGetBuildingRole(string buildingId, out string role)
        {
            int index = IndexOf(buildingId);
            if (index < 0)
            {
                role = string.Empty;
                return false;
            }

            role = _roles[index];
            return true;
        }

        internal static string RequireFieldString(JsonValue row, string buildingId, string field)
        {
            string path = "buildings.json $.buildings." + buildingId;
            if (!JsonUtil.TryField(row, field, out JsonValue value))
            {
                throw new FormatException(path + " missing " + field + ".");
            }

            return JsonUtil.String(value, path + "." + field);
        }

        internal static void RequireKnownRole(string buildingId, string role)
        {
            for (int i = 0; i < AiConstants.BuildingRoles.Length; i++)
            {
                if (role == AiConstants.BuildingRoles[i])
                {
                    return;
                }
            }

            throw new FormatException(
                "buildings.json $.buildings." + buildingId + " unknown role '" + role + "'.");
        }

        private int IndexOf(string buildingId)
        {
            if (buildingId == null)
            {
                throw new ArgumentNullException(nameof(buildingId));
            }

            int lo = 0;
            int hi = _ids.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int cmp = string.CompareOrdinal(_ids[mid], buildingId);
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

        private static void SortRows(string[] ids, string[] roles)
        {
            for (int i = 1; i < ids.Length; i++)
            {
                string id = ids[i];
                string role = roles[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(ids[j], id) > 0)
                {
                    ids[j + 1] = ids[j];
                    roles[j + 1] = roles[j];
                    j--;
                }

                ids[j + 1] = id;
                roles[j + 1] = role;
            }
        }

        private static void RequireUniqueIds(string[] ids)
        {
            for (int i = 1; i < ids.Length; i++)
            {
                if (ids[i] == ids[i - 1])
                {
                    throw new FormatException("Duplicate buildings.json id '" + ids[i] + "'.");
                }
            }
        }
    }
}
