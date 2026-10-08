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
            Array.Empty<string>());

        private readonly string[] _ids;
        private readonly string[] _roles;

        private AiBuildingCatalog(string[] ids, string[] roles)
        {
            _ids = ids;
            _roles = roles;
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
            return new AiBuildingCatalog(ids, roles);
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
            if (role == AiConstants.BuildingRoleSupplyDepot
                || role == AiConstants.BuildingRoleWall
                || role == AiConstants.BuildingRoleProduction)
            {
                return;
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
