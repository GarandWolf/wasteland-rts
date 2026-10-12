using System;

namespace Wasteland.Sim.Scenario
{
    /// <summary>
    /// Immutable scenario loaded from strict integer JSON. Seat id lists keep
    /// the order written in the file. Derived territories are stored in stable-key
    /// order. <see cref="ContentHash"/> is xxHash64 over that stored order.
    /// <see cref="MapFileSha256"/> is the SHA-256 of the main map file that was
    /// checked at load. A Git LFS pointer is rejected as <see cref="LfsNotPulledException"/>
    /// before any hash compare. Callers must not write the returned arrays.
    /// </summary>
    public sealed class SimScenario
    {
        public const int SchemaVersion = 1;
        public const int MinDataVersion = 7;

        internal SimScenario(
            int dataVersion,
            string id,
            string mapFile,
            int mapFormatVersion,
            string mapFileSha256,
            ulong jsonHash,
            ulong contentHash,
            ScenarioSeat[] seats,
            ScenarioMatchRules matchRules,
            ScenarioDerived derived,
            ulong[] neutralTerritoryKeys,
            ScenarioGarrison[] garrisons)
        {
            DataVersion = dataVersion;
            Id = id;
            MapFile = mapFile;
            MapFormatVersion = mapFormatVersion;
            MapFileSha256 = mapFileSha256;
            JsonHash = jsonHash;
            ContentHash = contentHash;
            Seats = seats;
            MatchRules = matchRules;
            Derived = derived;
            NeutralTerritoryKeys = neutralTerritoryKeys;
            Garrisons = garrisons ?? Array.Empty<ScenarioGarrison>();
        }

        public int DataVersion { get; }

        public string Id { get; }

        public string MapFile { get; }

        public int MapFormatVersion { get; }

        /// <summary>Lowercase hex SHA-256 of the main <c>.wrmb</c> bytes passed to <see cref="Load"/>.</summary>
        public string MapFileSha256 { get; }

        /// <summary>xxHash64 of the scenario JSON UTF-8 bytes.</summary>
        public ulong JsonHash { get; }

        /// <summary>xxHash64 of the loaded model in id order.</summary>
        public ulong ContentHash { get; }

        public ScenarioSeat[] Seats { get; }

        public ScenarioMatchRules MatchRules { get; }

        public ScenarioDerived Derived { get; }

        /// <summary>Map territories no seat holds at start, sorted by stable key.</summary>
        public ulong[] NeutralTerritoryKeys { get; }

        /// <summary>Neutral garrisons in file order. Empty when the scenario lists none.</summary>
        public ScenarioGarrison[] Garrisons { get; }

        public int SeatCount
        {
            get { return Seats.Length; }
        }

        public static SimScenario Load(
            string scenarioJson,
            BalanceCatalog balance,
            byte[] mapFileBytes,
            string mapFileName,
            string expectedScenarioId)
        {
            return ScenarioLoader.Load(scenarioJson, balance, mapFileBytes, mapFileName, expectedScenarioId);
        }

        /// <summary>
        /// Logical <c>mapFile</c> id to the main WRMB file name.
        /// <c>golden_ci_columbia_gorge_100_v1_marsh</c> becomes
        /// <c>golden-ci-columbia-gorge-100.v1-marsh.wrmb</c>.
        /// </summary>
        public static string ExpectedWrmbFileName(string mapFile)
        {
            if (mapFile == null)
            {
                throw new ArgumentNullException(nameof(mapFile));
            }

            ScenarioText.RequireSnake(mapFile, "$.mapFile");
            int versionAt = mapFile.LastIndexOf("_v", StringComparison.Ordinal);
            if (versionAt <= 0 || versionAt + 2 >= mapFile.Length || mapFile[versionAt + 2] < '0' || mapFile[versionAt + 2] > '9')
            {
                throw ScenarioErrors.At("$.mapFile", "expected a _v<digit> version suffix.");
            }

            string prefix = mapFile.Substring(0, versionAt).Replace('_', '-');
            string version = mapFile.Substring(versionAt + 1).Replace('_', '-');
            return prefix + "." + version + ".wrmb";
        }

        public ScenarioSeat SeatAt(int slot)
        {
            if (slot < 0 || slot >= Seats.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            return Seats[slot];
        }

        public bool TryGetSeatByFaction(string factionId, out ScenarioSeat seat)
        {
            if (factionId == null)
            {
                throw new ArgumentNullException(nameof(factionId));
            }

            for (int i = 0; i < Seats.Length; i++)
            {
                if (Seats[i].Faction == factionId)
                {
                    seat = Seats[i];
                    return true;
                }
            }

            seat = default;
            return false;
        }

        public bool TryGetTerritory(ulong stableKey, out ScenarioDerivedTerritory territory)
        {
            ScenarioDerivedTerritory[] rows = Derived.Territories;
            int lo = 0;
            int hi = rows.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                ulong key = rows[mid].MapStableKey;
                if (key == stableKey)
                {
                    territory = rows[mid];
                    return true;
                }

                if (key < stableKey)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            territory = default;
            return false;
        }

        public bool IsNeutralTerritory(ulong stableKey)
        {
            int lo = 0;
            int hi = NeutralTerritoryKeys.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                ulong key = NeutralTerritoryKeys[mid];
                if (key == stableKey)
                {
                    return true;
                }

                if (key < stableKey)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return false;
        }
    }
}
