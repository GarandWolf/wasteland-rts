using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Scenario
{
    internal static class ScenarioLoader
    {
        private const int ContentHashVersion = 1;
        private const string PopulationDesignerOverride = "designer_override";
        private const string PopulationCensus = "us_census_2020_pl";

        public static SimScenario Load(
            string scenarioJson,
            BalanceCatalog balance,
            byte[] mapFileBytes,
            string mapFileName,
            string expectedScenarioId)
        {
            if (scenarioJson == null)
            {
                throw new ArgumentNullException(nameof(scenarioJson));
            }

            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            if (mapFileBytes == null)
            {
                throw new ArgumentNullException(nameof(mapFileBytes));
            }

            if (mapFileName == null)
            {
                throw new ArgumentNullException(nameof(mapFileName));
            }

            if (expectedScenarioId == null)
            {
                throw new ArgumentNullException(nameof(expectedScenarioId));
            }

            if (expectedScenarioId.Length == 0)
            {
                throw new ArgumentException("Scenario id is required.", nameof(expectedScenarioId));
            }

            string actualName = Path.GetFileName(mapFileName);
            if (string.IsNullOrEmpty(actualName))
            {
                throw new ArgumentException("Map file name is required.", nameof(mapFileName));
            }

            Draft draft = Parse(scenarioJson);
            if (draft.Id != expectedScenarioId)
            {
                throw ScenarioErrors.At("$.id", "expected '" + expectedScenarioId + "'.");
            }

            if (draft.DataVersion < SimScenario.MinDataVersion)
            {
                throw ScenarioErrors.At("$.dataVersion", "must be >= " + SimScenario.MinDataVersion + ".");
            }

            if (draft.DataVersion != balance.DataVersion)
            {
                throw ScenarioErrors.At(
                    "$.dataVersion",
                    "is " + draft.DataVersion + " but balance dataVersion is " + balance.DataVersion + ".");
            }

            string expectedMapName = SimScenario.ExpectedWrmbFileName(draft.MapFile);
            if (actualName != expectedMapName)
            {
                throw ScenarioErrors.At(
                    "$.mapFile",
                    "loaded file '" + actualName + "' does not match '" + expectedMapName + "'.");
            }

            byte[] actualSha = Sha256(mapFileBytes);
            if (!SameBytes(actualSha, draft.MapSha256))
            {
                throw ScenarioErrors.At(
                    "$.mapFileSha256",
                    "does not match SHA-256 of map file '" + actualName + "'. Scenario has "
                    + draft.MapFileSha256 + " but the file is " + ToHex(actualSha) + ".");
            }

            ScenarioMapIndex map = ScenarioMapIndex.Read(mapFileBytes);
            if (draft.MapFormatVersion != map.FormatVersion)
            {
                throw ScenarioErrors.At(
                    "$.mapFormatVersion",
                    "loaded map formatVersion is " + map.FormatVersion + ".");
            }

            ScenarioDerived derived = ResolveDerived(draft, map, balance);
            ScenarioSeat[] seats = ResolveSeats(draft, map, balance, derived);
            ulong[] neutralKeys = NeutralKeys(derived, seats);
            ResolvePrize(draft.Rules, seats, neutralKeys);
            ulong jsonHash = XxHash64.Hash(Encoding.UTF8.GetBytes(scenarioJson), 0UL);
            ulong contentHash = Hash(draft, seats, derived, neutralKeys);
            return new SimScenario(
                draft.DataVersion,
                draft.Id,
                draft.MapFile,
                draft.MapFormatVersion,
                draft.MapFileSha256,
                jsonHash,
                contentHash,
                seats,
                draft.Rules,
                derived,
                neutralKeys);
        }

        private static Draft Parse(string scenarioJson)
        {
            JsonValue rootValue = IntegerJsonParser.Parse(scenarioJson);
            ScenarioObject root = new ScenarioObject(rootValue, "$");
            int schema = root.Int("schemaVersion", 0);
            if (schema != SimScenario.SchemaVersion)
            {
                throw ScenarioErrors.At("$.schemaVersion", "expected " + SimScenario.SchemaVersion + ".");
            }

            int dataVersion = root.Int("dataVersion", 0);
            string id = root.String("id");
            ScenarioText.RequireSnake(id, "$.id");
            string mapFile = root.String("mapFile");
            ScenarioText.RequireSnake(mapFile, "$.mapFile");
            int mapFormatVersion = root.Int("mapFormatVersion", 0);
            if (mapFormatVersion > ushort.MaxValue)
            {
                throw ScenarioErrors.At("$.mapFormatVersion", "integer out of range.");
            }

            string shaText = root.String("mapFileSha256");
            byte[] sha = ParseSha256(shaText, "$.mapFileSha256");
            DraftSeat[] seats = ParseSeats(root);
            ScenarioMatchRules rules = ParseRules(root);
            DraftDerived derived = ParseDerived(root);
            root.End();
            return new Draft(dataVersion, id, mapFile, mapFormatVersion, shaText, sha, seats, rules, derived);
        }

        private static DraftSeat[] ParseSeats(ScenarioObject root)
        {
            JsonValue array = root.Array("seats");
            if (array.ArrayValue.Count < 1)
            {
                throw ScenarioErrors.At("$.seats", "expected a non-empty array.");
            }

            DraftSeat[] seats = new DraftSeat[array.ArrayValue.Count];
            for (int i = 0; i < seats.Length; i++)
            {
                string path = "$.seats[" + i + "]";
                ScenarioObject row = new ScenarioObject(array.ArrayValue[i], path);
                int slot = row.Int("slot", 0);
                string faction = row.String("faction");
                ScenarioText.RequireSnake(faction, row.Child("faction"));
                string personality = row.NullableString("aiPersonality", out bool personalityNull);
                if (!personalityNull)
                {
                    ScenarioText.RequireSnake(personality, row.Child("aiPersonality"));
                }

                ulong home = TerritoryKeys.Parse(row.String("homeTerritoryKey"), row.Child("homeTerritoryKey"));
                int manpower = row.Int("startingManpower", 0);
                NamedId[] buildings = ParseIds(row, "startingBuildings", false);
                NamedCount[] units = ParseCounts(row, "startingUnits");
                DraftHolding[] holdings = ParseHoldings(row);
                NamedId[] raisable = ParseIds(row, "raisableUnits", false);
                DraftResearch[] research = ParseResearch(row);
                NamedId[] buildable = ParseIds(row, "buildableBuildings", false);
                row.End();
                seats[i] = new DraftSeat(
                    path,
                    slot,
                    faction,
                    personalityNull ? ScenarioController.Player : ScenarioController.Ai,
                    personality,
                    home,
                    manpower,
                    buildings,
                    units,
                    holdings,
                    raisable,
                    research,
                    buildable);
            }

            return seats;
        }

        private static NamedId[] ParseIds(ScenarioObject parent, string name, bool allowEmpty)
        {
            JsonValue array = parent.Array(name);
            string path = parent.Child(name);
            if (!allowEmpty && array.ArrayValue.Count < 1)
            {
                throw ScenarioErrors.At(path, "expected a non-empty array.");
            }

            NamedId[] items = new NamedId[array.ArrayValue.Count];
            for (int i = 0; i < items.Length; i++)
            {
                string itemPath = path + "[" + i + "]";
                JsonValue value = array.ArrayValue[i];
                if (value.Kind != JsonKind.String || value.StringValue.Length == 0)
                {
                    throw ScenarioErrors.At(itemPath, "expected a non-empty string.");
                }

                ScenarioText.RequireSnake(value.StringValue, itemPath);
                items[i] = new NamedId(itemPath, value.StringValue);
            }

            return items;
        }

        private static NamedCount[] ParseCounts(ScenarioObject parent, string name)
        {
            ScenarioObject obj = parent.Object(name);
            if (obj.Count < 1)
            {
                throw ScenarioErrors.At(obj.Path, "expected a non-empty object.");
            }

            NamedCount[] items = new NamedCount[obj.Count];
            for (int i = 0; i < items.Length; i++)
            {
                string id = obj.NameAt(i);
                string path = obj.Child(id);
                ScenarioText.RequireSnake(id, path);
                JsonValue value = obj.ValueAt(i);
                if (value.Kind != JsonKind.Int)
                {
                    throw ScenarioErrors.At(path, "expected an integer.");
                }

                if (value.IntValue < 1 || value.IntValue > int.MaxValue)
                {
                    throw ScenarioErrors.At(path, "integer out of range.");
                }

                items[i] = new NamedCount(path, id, (int)value.IntValue);
            }

            obj.End();
            return items;
        }

        private static DraftHolding[] ParseHoldings(ScenarioObject seat)
        {
            JsonValue array = seat.Array("startingHoldings");
            if (array.ArrayValue.Count < 1)
            {
                throw ScenarioErrors.At(seat.Child("startingHoldings"), "expected a non-empty array.");
            }

            DraftHolding[] holdings = new DraftHolding[array.ArrayValue.Count];
            for (int i = 0; i < holdings.Length; i++)
            {
                string path = seat.Child("startingHoldings") + "[" + i + "]";
                ScenarioObject row = new ScenarioObject(array.ArrayValue[i], path);
                ulong key = TerritoryKeys.Parse(row.String("territoryKey"), row.Child("territoryKey"));
                ScenarioPlaceCounts places = ParsePlaces(row, "places");
                row.End();
                holdings[i] = new DraftHolding(path, key, places);
            }

            return holdings;
        }

        private static DraftResearch[] ParseResearch(ScenarioObject seat)
        {
            JsonValue array = seat.Array("autoResearch");
            string path = seat.Child("autoResearch");
            DraftResearch[] items = new DraftResearch[array.ArrayValue.Count];
            for (int i = 0; i < items.Length; i++)
            {
                string itemPath = path + "[" + i + "]";
                ScenarioObject row = new ScenarioObject(array.ArrayValue[i], itemPath);
                string item = row.String("item");
                ScenarioText.RequireSnake(item, row.Child("item"));
                int tick = row.Int("notBeforeTick", 0);
                row.End();
                items[i] = new DraftResearch(itemPath, item, tick);
            }

            return items;
        }

        private static ScenarioMatchRules ParseRules(ScenarioObject root)
        {
            ScenarioObject rules = root.Object("matchRules");
            int factions = rules.Int("allowedFactionCount", 1);
            int aiSeats = rules.Int("allowedAiSeats", 0);
            string victory = rules.String("victory");
            ScenarioText.RequireToken(victory, rules.Child("victory"), ScenarioMatchRules.VictoryCaptureEnemyHq);
            string ownership = rules.String("territoryOwnershipRule");
            ScenarioText.RequireToken(ownership, rules.Child("territoryOwnershipRule"), ScenarioMatchRules.OwnershipHoldSeat);
            bool hasPrize = false;
            ulong prize = 0UL;
            if (rules.TryValue("neutralPrizeTerritoryKey", out JsonValue prizeValue))
            {
                if (prizeValue.Kind != JsonKind.String)
                {
                    throw ScenarioErrors.At(rules.Child("neutralPrizeTerritoryKey"), "expected a string.");
                }

                prize = TerritoryKeys.Parse(prizeValue.StringValue, rules.Child("neutralPrizeTerritoryKey"));
                hasPrize = true;
            }

            bool fuel = rules.Bool("fuelEnabled");
            string loss = rules.String("simultaneousHqLoss");
            ScenarioText.RequireToken(loss, rules.Child("simultaneousHqLoss"), ScenarioMatchRules.SimultaneousHqLossDraw);
            rules.End();
            return new ScenarioMatchRules(factions, aiSeats, victory, ownership, hasPrize, prize, fuel, loss);
        }

        private static DraftDerived ParseDerived(ScenarioObject root)
        {
            ScenarioObject derived = root.Object("derived");
            ScenarioPlaceCounts holdings = ParsePlaces(derived, "holdingCounts");
            int capturable = derived.Int("capturablePlaces", 0);
            int fromMap = derived.Int("capturablePlacesFromMap", 0);
            int overrides = derived.Int("designerOverridePlaces", 0);
            int scenery = derived.Int("sceneryPlaces", 0);
            int sceneryPending = derived.Int("sceneryPlacesPendingPopulationFix", 0);
            ScenarioSiteCounts sites = ParseSites(derived, "resourceSiteCounts");
            DraftTerritory[] territories = ParseTerritories(derived);
            DraftPopulation[] populationOverrides = ParsePopulation(derived, "populationOverrides");
            DraftPopulation[] census = ParsePopulation(derived, "censusFallbackPlaces");
            derived.End();
            return new DraftDerived(
                holdings,
                capturable,
                fromMap,
                overrides,
                scenery,
                sceneryPending,
                sites,
                territories,
                populationOverrides,
                census);
        }

        private static DraftTerritory[] ParseTerritories(ScenarioObject derived)
        {
            JsonValue array = derived.Array("territories");
            if (array.ArrayValue.Count < 1)
            {
                throw ScenarioErrors.At(derived.Child("territories"), "expected a non-empty array.");
            }

            DraftTerritory[] rows = new DraftTerritory[array.ArrayValue.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                string path = derived.Child("territories") + "[" + i + "]";
                ScenarioObject row = new ScenarioObject(array.ArrayValue[i], path);
                string id = row.String("id");
                ScenarioText.RequireSnake(id, row.Child("id"));
                int bakeIndex = row.Int("bakeIndex", 1);
                ulong key = TerritoryKeys.Parse(row.String("mapStableKey"), row.Child("mapStableKey"));
                string seatTier = row.String("seatTier");
                ScenarioText.RequireSnake(seatTier, row.Child("seatTier"));
                ScenarioPlaceCounts places = ParsePlaces(row, "places");
                int scenery = row.Int("sceneryPlaces", 0);
                int designerOverride = row.Int("designerOverridePlaces", 0);
                ScenarioSiteCounts sites = ParseSites(row, "sites");
                row.End();
                rows[i] = new DraftTerritory(path, id, bakeIndex, key, seatTier, places, scenery, designerOverride, sites);
            }

            return rows;
        }

        private static DraftPopulation[] ParsePopulation(ScenarioObject derived, string name)
        {
            JsonValue array = derived.Array(name);
            string path = derived.Child(name);
            DraftPopulation[] rows = new DraftPopulation[array.ArrayValue.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                string itemPath = path + "[" + i + "]";
                ScenarioObject row = new ScenarioObject(array.ArrayValue[i], itemPath);
                string place = row.String("place");
                ScenarioText.RequireSnake(place, row.Child("place"));
                int townId = row.Int("mapTownId", 0);
                ulong key = TerritoryKeys.Parse(row.String("territoryKey"), row.Child("territoryKey"));
                int population = row.Int("population", 0);
                string tier = row.String("tier");
                ScenarioText.RequireSnake(tier, row.Child("tier"));
                string source = row.String("populationSource");
                ScenarioText.RequireSnake(source, row.Child("populationSource"));
                if (source != PopulationDesignerOverride && source != PopulationCensus)
                {
                    throw ScenarioErrors.At(
                        row.Child("populationSource"),
                        "expected '" + PopulationDesignerOverride + "' or '" + PopulationCensus + "'.");
                }

                row.End();
                rows[i] = new DraftPopulation(itemPath, place, townId, key, population, tier, source);
            }

            return rows;
        }

        private static ScenarioPlaceCounts ParsePlaces(ScenarioObject parent, string name)
        {
            ScenarioObject places = parent.Object(name);
            int hamlet = places.Int("hamlet", 0);
            int village = places.Int("village", 0);
            int town = places.Int("town", 0);
            int city = places.Int("city", 0);
            places.End();
            return new ScenarioPlaceCounts(hamlet, village, town, city);
        }

        private static ScenarioSiteCounts ParseSites(ScenarioObject parent, string name)
        {
            ScenarioObject sites = parent.Object(name);
            int fuelStation = sites.Int("fuel_station", 0);
            int fuelDepot = sites.Int("fuel_storage_depot", 0);
            int refinery = sites.Int("refinery", 0);
            int industrial = sites.Int("industrial_area", 0);
            sites.End();
            return new ScenarioSiteCounts(fuelStation, fuelDepot, refinery, industrial);
        }

        private static ScenarioDerived ResolveDerived(Draft draft, ScenarioMapIndex map, BalanceCatalog balance)
        {
            DraftDerived source = draft.Derived;
            ScenarioDerivedTerritory[] territories = new ScenarioDerivedTerritory[source.Territories.Length];
            long hamlet = 0;
            long village = 0;
            long town = 0;
            long city = 0;
            long scenery = 0;
            long overrides = 0;
            long fuelStation = 0;
            long fuelDepot = 0;
            long refinery = 0;
            long industrial = 0;
            for (int i = 0; i < source.Territories.Length; i++)
            {
                DraftTerritory row = source.Territories[i];
                if (!balance.HasHoldingTier(row.SeatTier))
                {
                    throw ScenarioErrors.At(row.Path + ".seatTier", "unknown holding tier '" + row.SeatTier + "'.");
                }

                if (!map.TryGetTerritory(row.Key, out ScenarioMapTerritory onMap))
                {
                    throw ScenarioErrors.At(
                        row.Path + ".mapStableKey",
                        "unknown territory " + TerritoryKeys.Format(row.Key) + ".");
                }

                if (!map.TryGetTown(onMap.SeatTownId, out int townTerritoryId, out string townName))
                {
                    throw ScenarioErrors.At(row.Path + ".mapStableKey", "seat town " + onMap.SeatTownId + " is not on the map.");
                }

                if (townTerritoryId != onMap.TerritoryId)
                {
                    throw ScenarioErrors.At(
                        row.Path + ".mapStableKey",
                        "seat town " + onMap.SeatTownId + " is not in territory " + onMap.TerritoryId + ".");
                }

                string label = ScenarioText.TownLabel(townName);
                if (label != row.Id)
                {
                    throw ScenarioErrors.At(
                        row.Path + ".id",
                        "seat town '" + townName + "' does not match label '" + row.Id + "'.");
                }

                hamlet += row.Places.Hamlet;
                village += row.Places.Village;
                town += row.Places.Town;
                city += row.Places.City;
                scenery += row.SceneryPlaces;
                overrides += row.DesignerOverridePlaces;
                fuelStation += row.Sites.FuelStation;
                fuelDepot += row.Sites.FuelStorageDepot;
                refinery += row.Sites.Refinery;
                industrial += row.Sites.IndustrialArea;
                territories[i] = new ScenarioDerivedTerritory(
                    row.Id,
                    row.BakeIndex,
                    row.Key,
                    onMap.TerritoryId,
                    onMap.SeatTownId,
                    row.SeatTier,
                    row.Places,
                    row.SceneryPlaces,
                    row.DesignerOverridePlaces,
                    row.Sites);
            }

            Array.Sort(territories, CompareDerivedTerritory);
            for (int i = 1; i < territories.Length; i++)
            {
                if (territories[i].MapStableKey == territories[i - 1].MapStableKey)
                {
                    throw ScenarioErrors.At("$.derived.territories", "duplicate territory " + TerritoryKeys.Format(territories[i].MapStableKey) + ".");
                }
            }

            string[] labels = new string[territories.Length];
            for (int i = 0; i < territories.Length; i++)
            {
                labels[i] = territories[i].Id;
            }

            ScenarioSort.Ordinal(labels);
            ScenarioSort.RequireUnique(labels, "$.derived.territories");

            int[] bakeIndexes = new int[territories.Length];
            for (int i = 0; i < territories.Length; i++)
            {
                bakeIndexes[i] = territories[i].BakeIndex;
            }

            Array.Sort(bakeIndexes);
            for (int i = 1; i < bakeIndexes.Length; i++)
            {
                if (bakeIndexes[i] == bakeIndexes[i - 1])
                {
                    throw ScenarioErrors.At("$.derived.territories", "duplicate bakeIndex " + bakeIndexes[i] + ".");
                }
            }

            if (map.TerritoryCount != territories.Length)
            {
                throw ScenarioErrors.At(
                    "$.derived.territories",
                    "expected " + map.TerritoryCount + " map territories.");
            }

            for (int i = 0; i < territories.Length; i++)
            {
                if (territories[i].MapStableKey != map.TerritoryAt(i).StableKey)
                {
                    throw ScenarioErrors.At(
                        "$.derived.territories",
                        "map territory " + TerritoryKeys.Format(map.TerritoryAt(i).StableKey) + " is not listed.");
                }
            }

            RequireSum(hamlet, source.HoldingCounts.Hamlet, "$.derived.holdingCounts.hamlet");
            RequireSum(village, source.HoldingCounts.Village, "$.derived.holdingCounts.village");
            RequireSum(town, source.HoldingCounts.Town, "$.derived.holdingCounts.town");
            RequireSum(city, source.HoldingCounts.City, "$.derived.holdingCounts.city");
            RequireSum(scenery, source.SceneryPlaces, "$.derived.sceneryPlaces");
            RequireSum(overrides, source.DesignerOverridePlaces, "$.derived.designerOverridePlaces");
            RequireSum(fuelStation, source.Sites.FuelStation, "$.derived.resourceSiteCounts.fuel_station");
            RequireSum(fuelDepot, source.Sites.FuelStorageDepot, "$.derived.resourceSiteCounts.fuel_storage_depot");
            RequireSum(refinery, source.Sites.Refinery, "$.derived.resourceSiteCounts.refinery");
            RequireSum(industrial, source.Sites.IndustrialArea, "$.derived.resourceSiteCounts.industrial_area");
            long capturable = hamlet + village + town + city;
            RequireSum(capturable, source.CapturablePlaces, "$.derived.capturablePlaces");
            if ((long)source.CapturablePlacesFromMap + source.DesignerOverridePlaces != source.CapturablePlaces)
            {
                throw ScenarioErrors.At(
                    "$.derived.capturablePlaces",
                    "expected capturablePlacesFromMap + designerOverridePlaces.");
            }

            ScenarioPopulationPlace[] populationOverrides = ResolvePopulation(source.PopulationOverrides, map, balance);
            ScenarioPopulationPlace[] census = ResolvePopulation(source.CensusFallbackPlaces, map, balance);
            if (populationOverrides.Length != source.DesignerOverridePlaces)
            {
                throw ScenarioErrors.At(
                    "$.derived.populationOverrides",
                    "expected " + source.DesignerOverridePlaces + " designer override places.");
            }

            return new ScenarioDerived(
                source.HoldingCounts,
                source.CapturablePlaces,
                source.CapturablePlacesFromMap,
                source.DesignerOverridePlaces,
                source.SceneryPlaces,
                source.SceneryPlacesPendingPopulationFix,
                source.Sites,
                territories,
                populationOverrides,
                census);
        }

        private static ScenarioPopulationPlace[] ResolvePopulation(DraftPopulation[] rows, ScenarioMapIndex map, BalanceCatalog balance)
        {
            ScenarioPopulationPlace[] places = new ScenarioPopulationPlace[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                DraftPopulation row = rows[i];
                if (!balance.HasHoldingTier(row.Tier))
                {
                    throw ScenarioErrors.At(row.Path + ".tier", "unknown holding tier '" + row.Tier + "'.");
                }

                if (!map.TryGetTerritory(row.Key, out ScenarioMapTerritory territory))
                {
                    throw ScenarioErrors.At(row.Path + ".territoryKey", "unknown territory " + TerritoryKeys.Format(row.Key) + ".");
                }

                if (!map.TryGetTown(row.MapTownId, out int townTerritoryId, out string townName))
                {
                    throw ScenarioErrors.At(row.Path + ".mapTownId", "unknown town " + row.MapTownId + ".");
                }

                if (townTerritoryId != territory.TerritoryId)
                {
                    throw ScenarioErrors.At(
                        row.Path + ".territoryKey",
                        "town '" + townName + "' is not in territory " + TerritoryKeys.Format(row.Key) + ".");
                }

                places[i] = new ScenarioPopulationPlace(row.Place, row.MapTownId, row.Key, row.Population, row.Tier, row.Source);
            }

            Array.Sort(places, ComparePopulation);
            for (int i = 1; i < places.Length; i++)
            {
                if (places[i].TerritoryKey == places[i - 1].TerritoryKey
                    && places[i].MapTownId == places[i - 1].MapTownId
                    && places[i].Place == places[i - 1].Place)
                {
                    throw ScenarioErrors.At("$.derived", "duplicate place '" + places[i].Place + "'.");
                }
            }

            return places;
        }

        private static ScenarioSeat[] ResolveSeats(
            Draft draft,
            ScenarioMapIndex map,
            BalanceCatalog balance,
            ScenarioDerived derived)
        {
            DraftSeat[] source = draft.Seats;
            Array.Sort(source, CompareSeatSlot);
            if (source.Length != draft.Rules.AllowedFactionCount)
            {
                throw ScenarioErrors.At(
                    "$.matchRules.allowedFactionCount",
                    "expected " + source.Length + ".");
            }

            int aiSeats = 0;
            ScenarioSeat[] seats = new ScenarioSeat[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                DraftSeat row = source[i];
                if (row.Slot != i)
                {
                    throw ScenarioErrors.At(row.Path + ".slot", "expected " + i + ".");
                }

                if (!balance.HasFaction(row.FactionId))
                {
                    throw ScenarioErrors.At(row.Path + ".faction", "unknown faction '" + row.FactionId + "'.");
                }

                for (int j = 0; j < i; j++)
                {
                    if (seats[j].FactionId == row.FactionId)
                    {
                        throw ScenarioErrors.At(row.Path + ".faction", "duplicate faction '" + row.FactionId + "'.");
                    }
                }

                if (row.Controller == ScenarioController.Ai)
                {
                    aiSeats++;
                    if (!balance.TryGetPersonalityFaction(row.PersonalityId, out string personalityFaction))
                    {
                        throw ScenarioErrors.At(row.Path + ".aiPersonality", "unknown personality '" + row.PersonalityId + "'.");
                    }

                    if (personalityFaction != row.FactionId)
                    {
                        throw ScenarioErrors.At(
                            row.Path + ".aiPersonality",
                            "personality '" + row.PersonalityId + "' is bound to faction '" + personalityFaction
                            + "', not '" + row.FactionId + "'.");
                    }
                }

                if (row.Manpower != balance.StartingManpower)
                {
                    throw ScenarioErrors.At(
                        row.Path + ".startingManpower",
                        "expected " + balance.StartingManpower + " from economy.json startingResources.manpower.");
                }

                if (!map.TryGetTerritory(row.HomeKey, out ScenarioMapTerritory home))
                {
                    throw ScenarioErrors.At(
                        row.Path + ".homeTerritoryKey",
                        "unknown territory " + TerritoryKeys.Format(row.HomeKey) + ".");
                }

                if (!FindDerived(derived, row.HomeKey, out ScenarioDerivedTerritory homeDerived))
                {
                    throw ScenarioErrors.At(
                        row.Path + ".homeTerritoryKey",
                        "unknown territory " + TerritoryKeys.Format(row.HomeKey) + ".");
                }

                if (home.TerritoryId != homeDerived.MapTerritoryId || home.SeatTownId != homeDerived.SeatTownId)
                {
                    throw ScenarioErrors.At(row.Path + ".homeTerritoryKey", "home territory does not match the map.");
                }

                string[] buildings = SortIds(row.Buildings, row.Path + ".startingBuildings");
                string[] raisable = SortIds(row.Raisable, row.Path + ".raisableUnits");
                string[] buildable = SortIds(row.Buildable, row.Path + ".buildableBuildings");
                RequireBuildings(balance, row.FactionId, row.Buildings);
                RequireBuildings(balance, row.FactionId, row.Buildable);
                ScenarioUnitCount[] units = SortUnits(row.Units);
                RequireUnits(balance, row.FactionId, row.Units);
                RequireUnits(balance, row.FactionId, IdsOf(raisable, row.Raisable));
                ScenarioAutoResearch[] research = SortResearch(row.Research);
                RequireTech(balance, row.Research);
                ScenarioHolding[] holdings = ResolveHoldings(row, map, derived);
                bool holdsHome = false;
                for (int h = 0; h < holdings.Length; h++)
                {
                    if (holdings[h].TerritoryKey == row.HomeKey)
                    {
                        holdsHome = true;
                    }
                }

                if (!holdsHome)
                {
                    throw ScenarioErrors.At(row.Path + ".homeTerritoryKey", "home territory is not in startingHoldings.");
                }

                seats[i] = new ScenarioSeat(
                    row.Slot,
                    row.FactionId,
                    row.Controller,
                    row.PersonalityId,
                    row.HomeKey,
                    home.TerritoryId,
                    home.SeatTownId,
                    row.Manpower,
                    buildings,
                    units,
                    holdings,
                    raisable,
                    research,
                    buildable);
            }

            if (aiSeats != draft.Rules.AllowedAiSeats)
            {
                throw ScenarioErrors.At("$.matchRules.allowedAiSeats", "expected " + aiSeats + ".");
            }

            for (int i = 0; i < seats.Length; i++)
            {
                for (int h = 0; h < seats[i].StartingHoldings.Length; h++)
                {
                    ulong key = seats[i].StartingHoldings[h].TerritoryKey;
                    for (int j = 0; j < i; j++)
                    {
                        for (int k = 0; k < seats[j].StartingHoldings.Length; k++)
                        {
                            if (seats[j].StartingHoldings[k].TerritoryKey == key)
                            {
                                throw ScenarioErrors.At(
                                    "$.seats[" + seats[i].Slot + "].startingHoldings",
                                    "territory " + TerritoryKeys.Format(key) + " is already held by seat " + seats[j].Slot + ".");
                            }
                        }
                    }
                }
            }

            return seats;
        }

        private static ScenarioHolding[] ResolveHoldings(
            DraftSeat seat,
            ScenarioMapIndex map,
            ScenarioDerived derived)
        {
            ScenarioHolding[] holdings = new ScenarioHolding[seat.Holdings.Length];
            for (int i = 0; i < seat.Holdings.Length; i++)
            {
                DraftHolding row = seat.Holdings[i];
                if (!map.TryGetTerritory(row.Key, out ScenarioMapTerritory onMap))
                {
                    throw ScenarioErrors.At(row.Path + ".territoryKey", "unknown territory " + TerritoryKeys.Format(row.Key) + ".");
                }

                if (!FindDerived(derived, row.Key, out ScenarioDerivedTerritory listed))
                {
                    throw ScenarioErrors.At(row.Path + ".territoryKey", "unknown territory " + TerritoryKeys.Format(row.Key) + ".");
                }

                if (!row.Places.EqualsCounts(listed.Places))
                {
                    throw ScenarioErrors.At(row.Path + ".places", "does not match the derived territory places.");
                }

                holdings[i] = new ScenarioHolding(row.Key, onMap.TerritoryId, row.Places);
            }

            Array.Sort(holdings, CompareHolding);
            for (int i = 1; i < holdings.Length; i++)
            {
                if (holdings[i].TerritoryKey == holdings[i - 1].TerritoryKey)
                {
                    throw ScenarioErrors.At(seat.Path + ".startingHoldings", "duplicate territory " + TerritoryKeys.Format(holdings[i].TerritoryKey) + ".");
                }
            }

            return holdings;
        }

        private static ulong[] NeutralKeys(ScenarioDerived derived, ScenarioSeat[] seats)
        {
            int count = 0;
            ulong[] keys = new ulong[derived.Territories.Length];
            for (int i = 0; i < derived.Territories.Length; i++)
            {
                ulong key = derived.Territories[i].MapStableKey;
                if (!IsHeld(seats, key))
                {
                    keys[count] = key;
                    count++;
                }
            }

            ulong[] trimmed = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                trimmed[i] = keys[i];
            }

            return trimmed;
        }

        private static void ResolvePrize(ScenarioMatchRules rules, ScenarioSeat[] seats, ulong[] neutralKeys)
        {
            if (!rules.HasNeutralPrize)
            {
                return;
            }

            bool listed = false;
            for (int i = 0; i < neutralKeys.Length; i++)
            {
                if (neutralKeys[i] == rules.NeutralPrizeTerritoryKey)
                {
                    listed = true;
                }
            }

            if (!listed)
            {
                string reason = IsHeld(seats, rules.NeutralPrizeTerritoryKey)
                    ? "territory is held by a seat at start."
                    : "unknown territory " + TerritoryKeys.Format(rules.NeutralPrizeTerritoryKey) + ".";
                for (int i = 0; i < seats.Length; i++)
                {
                    if (seats[i].HomeTerritoryKey == rules.NeutralPrizeTerritoryKey)
                    {
                        reason = "territory is a seat home.";
                    }
                }

                throw ScenarioErrors.At("$.matchRules.neutralPrizeTerritoryKey", reason);
            }

            for (int i = 0; i < seats.Length; i++)
            {
                if (seats[i].HomeTerritoryKey == rules.NeutralPrizeTerritoryKey)
                {
                    throw ScenarioErrors.At("$.matchRules.neutralPrizeTerritoryKey", "territory is a seat home.");
                }
            }
        }

        private static bool IsHeld(ScenarioSeat[] seats, ulong key)
        {
            for (int i = 0; i < seats.Length; i++)
            {
                ScenarioHolding[] holdings = seats[i].StartingHoldings;
                for (int h = 0; h < holdings.Length; h++)
                {
                    if (holdings[h].TerritoryKey == key)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void RequireBuildings(BalanceCatalog balance, string factionId, NamedId[] buildings)
        {
            for (int i = 0; i < buildings.Length; i++)
            {
                if (!balance.HasBuilding(buildings[i].Id))
                {
                    throw ScenarioErrors.At(buildings[i].Path, "unknown building id '" + buildings[i].Id + "'.");
                }

                if (!balance.BuildingAllowsFaction(buildings[i].Id, factionId))
                {
                    throw ScenarioErrors.At(
                        buildings[i].Path,
                        "building '" + buildings[i].Id + "' is not buildable by faction '" + factionId + "'.");
                }
            }
        }

        private static void RequireUnits(BalanceCatalog balance, string factionId, NamedCount[] units)
        {
            for (int i = 0; i < units.Length; i++)
            {
                if (!balance.TryGetUnitFaction(units[i].Id, out string owner))
                {
                    throw ScenarioErrors.At(units[i].Path, "unknown unit id '" + units[i].Id + "'.");
                }

                if (owner != factionId)
                {
                    throw ScenarioErrors.At(
                        units[i].Path,
                        "unit '" + units[i].Id + "' belongs to faction '" + owner + "', not '" + factionId + "'.");
                }
            }
        }

        private static void RequireTech(BalanceCatalog balance, DraftResearch[] research)
        {
            for (int i = 0; i < research.Length; i++)
            {
                if (!balance.HasTech(research[i].Item))
                {
                    throw ScenarioErrors.At(research[i].Path + ".item", "unknown tech id '" + research[i].Item + "'.");
                }
            }
        }

        private static NamedCount[] IdsOf(string[] sortedIds, NamedId[] source)
        {
            NamedCount[] units = new NamedCount[sortedIds.Length];
            for (int i = 0; i < sortedIds.Length; i++)
            {
                string path = source[0].Path;
                for (int j = 0; j < source.Length; j++)
                {
                    if (source[j].Id == sortedIds[i])
                    {
                        path = source[j].Path;
                    }
                }

                units[i] = new NamedCount(path, sortedIds[i], 1);
            }

            return units;
        }

        private static string[] SortIds(NamedId[] items, string path)
        {
            NamedId[] copy = new NamedId[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                copy[i] = items[i];
            }

            Array.Sort(copy, CompareNamedId);
            for (int i = 1; i < copy.Length; i++)
            {
                if (copy[i].Id == copy[i - 1].Id)
                {
                    throw ScenarioErrors.At(path, "duplicate id '" + copy[i].Id + "'.");
                }
            }

            string[] ids = new string[copy.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                ids[i] = copy[i].Id;
            }

            return ids;
        }

        private static ScenarioUnitCount[] SortUnits(NamedCount[] items)
        {
            NamedCount[] copy = new NamedCount[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                copy[i] = items[i];
            }

            Array.Sort(copy, CompareNamedCount);
            for (int i = 1; i < copy.Length; i++)
            {
                if (copy[i].Id == copy[i - 1].Id)
                {
                    throw ScenarioErrors.At(copy[i].Path, "duplicate id '" + copy[i].Id + "'.");
                }
            }

            ScenarioUnitCount[] units = new ScenarioUnitCount[copy.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                units[i] = new ScenarioUnitCount(copy[i].Id, copy[i].Count);
            }

            return units;
        }

        private static ScenarioAutoResearch[] SortResearch(DraftResearch[] items)
        {
            DraftResearch[] copy = new DraftResearch[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                copy[i] = items[i];
            }

            Array.Sort(copy, CompareResearch);
            for (int i = 1; i < copy.Length; i++)
            {
                if (copy[i].Item == copy[i - 1].Item)
                {
                    throw ScenarioErrors.At(copy[i].Path + ".item", "duplicate id '" + copy[i].Item + "'.");
                }
            }

            ScenarioAutoResearch[] research = new ScenarioAutoResearch[copy.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                research[i] = new ScenarioAutoResearch(copy[i].Item, copy[i].NotBeforeTick);
            }

            return research;
        }

        private static bool FindDerived(ScenarioDerived derived, ulong key, out ScenarioDerivedTerritory territory)
        {
            for (int i = 0; i < derived.Territories.Length; i++)
            {
                if (derived.Territories[i].MapStableKey == key)
                {
                    territory = derived.Territories[i];
                    return true;
                }
            }

            territory = default;
            return false;
        }

        private static void RequireSum(long actual, int expected, string path)
        {
            if (actual != expected)
            {
                throw ScenarioErrors.At(path, "expected " + actual + " from territory rows.");
            }
        }

        private static ulong Hash(Draft draft, ScenarioSeat[] seats, ScenarioDerived derived, ulong[] neutralKeys)
        {
            ScenarioHasher hash = new ScenarioHasher();
            hash.Int(ContentHashVersion);
            hash.Int(SimScenario.SchemaVersion);
            hash.Int(draft.DataVersion);
            hash.Text(draft.Id);
            hash.Text(draft.MapFile);
            hash.Int(draft.MapFormatVersion);
            hash.Bytes(draft.MapSha256);
            hash.Int(seats.Length);
            for (int i = 0; i < seats.Length; i++)
            {
                HashSeat(hash, seats[i]);
            }

            HashRules(hash, draft.Rules);
            HashDerived(hash, derived);
            hash.Int(neutralKeys.Length);
            for (int i = 0; i < neutralKeys.Length; i++)
            {
                hash.ULong(neutralKeys[i]);
            }

            return hash.Digest();
        }

        private static void HashSeat(ScenarioHasher hash, ScenarioSeat seat)
        {
            hash.Int(seat.Slot);
            hash.Byte((byte)seat.Controller);
            hash.Text(seat.FactionId);
            hash.Text(seat.PersonalityId);
            hash.ULong(seat.HomeTerritoryKey);
            hash.Int(seat.HomeTerritoryId);
            hash.Int(seat.HomeTownId);
            hash.Int(seat.StartingManpower);
            HashIds(hash, seat.StartingBuildings);
            hash.Int(seat.StartingUnits.Length);
            for (int i = 0; i < seat.StartingUnits.Length; i++)
            {
                hash.Text(seat.StartingUnits[i].UnitId);
                hash.Int(seat.StartingUnits[i].Count);
            }

            hash.Int(seat.StartingHoldings.Length);
            for (int i = 0; i < seat.StartingHoldings.Length; i++)
            {
                hash.ULong(seat.StartingHoldings[i].TerritoryKey);
                hash.Int(seat.StartingHoldings[i].TerritoryId);
                HashPlaces(hash, seat.StartingHoldings[i].Places);
            }

            HashIds(hash, seat.RaisableUnits);
            hash.Int(seat.AutoResearch.Length);
            for (int i = 0; i < seat.AutoResearch.Length; i++)
            {
                hash.Text(seat.AutoResearch[i].Item);
                hash.Int(seat.AutoResearch[i].NotBeforeTick);
            }

            HashIds(hash, seat.BuildableBuildings);
        }

        private static void HashRules(ScenarioHasher hash, ScenarioMatchRules rules)
        {
            hash.Int(rules.AllowedFactionCount);
            hash.Int(rules.AllowedAiSeats);
            hash.Text(rules.Victory);
            hash.Text(rules.TerritoryOwnershipRule);
            hash.Bool(rules.HasNeutralPrize);
            hash.ULong(rules.NeutralPrizeTerritoryKey);
            hash.Bool(rules.FuelEnabled);
            hash.Text(rules.SimultaneousHqLoss);
        }

        private static void HashDerived(ScenarioHasher hash, ScenarioDerived derived)
        {
            HashPlaces(hash, derived.HoldingCounts);
            hash.Int(derived.CapturablePlaces);
            hash.Int(derived.CapturablePlacesFromMap);
            hash.Int(derived.DesignerOverridePlaces);
            hash.Int(derived.SceneryPlaces);
            hash.Int(derived.SceneryPlacesPendingPopulationFix);
            HashSites(hash, derived.ResourceSiteCounts);
            hash.Int(derived.Territories.Length);
            for (int i = 0; i < derived.Territories.Length; i++)
            {
                ScenarioDerivedTerritory row = derived.Territories[i];
                hash.Text(row.Id);
                hash.Int(row.BakeIndex);
                hash.ULong(row.MapStableKey);
                hash.Int(row.MapTerritoryId);
                hash.Int(row.SeatTownId);
                hash.Text(row.SeatTier);
                HashPlaces(hash, row.Places);
                hash.Int(row.SceneryPlaces);
                hash.Int(row.DesignerOverridePlaces);
                HashSites(hash, row.Sites);
            }

            HashPopulation(hash, derived.PopulationOverrides);
            HashPopulation(hash, derived.CensusFallbackPlaces);
        }

        private static void HashPopulation(ScenarioHasher hash, ScenarioPopulationPlace[] rows)
        {
            hash.Int(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                hash.Text(rows[i].Place);
                hash.Int(rows[i].MapTownId);
                hash.ULong(rows[i].TerritoryKey);
                hash.Int(rows[i].Population);
                hash.Text(rows[i].Tier);
                hash.Text(rows[i].PopulationSource);
            }
        }

        private static void HashIds(ScenarioHasher hash, string[] ids)
        {
            hash.Int(ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                hash.Text(ids[i]);
            }
        }

        private static void HashPlaces(ScenarioHasher hash, ScenarioPlaceCounts counts)
        {
            hash.Text("city");
            hash.Int(counts.City);
            hash.Text("hamlet");
            hash.Int(counts.Hamlet);
            hash.Text("town");
            hash.Int(counts.Town);
            hash.Text("village");
            hash.Int(counts.Village);
        }

        private static void HashSites(ScenarioHasher hash, ScenarioSiteCounts counts)
        {
            hash.Text("fuel_station");
            hash.Int(counts.FuelStation);
            hash.Text("fuel_storage_depot");
            hash.Int(counts.FuelStorageDepot);
            hash.Text("industrial_area");
            hash.Int(counts.IndustrialArea);
            hash.Text("refinery");
            hash.Int(counts.Refinery);
        }

        private static byte[] ParseSha256(string text, string path)
        {
            if (text.Length != 64)
            {
                throw ScenarioErrors.At(path, "expected 64 lowercase hex digits.");
            }

            byte[] bytes = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                int hi = Hex(text[i * 2], path);
                int lo = Hex(text[(i * 2) + 1], path);
                bytes[i] = (byte)((hi << 4) | lo);
            }

            return bytes;
        }

        private static int Hex(char c, string path)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            throw ScenarioErrors.At(path, "expected 64 lowercase hex digits.");
        }

        private static byte[] Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        private static string ToHex(byte[] bytes)
        {
            const string hex = "0123456789abcdef";
            char[] text = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                text[i * 2] = hex[bytes[i] >> 4];
                text[(i * 2) + 1] = hex[bytes[i] & 15];
            }

            return new string(text);
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static int CompareSeatSlot(DraftSeat a, DraftSeat b)
        {
            return a.Slot.CompareTo(b.Slot);
        }

        private static int CompareNamedId(NamedId a, NamedId b)
        {
            return string.CompareOrdinal(a.Id, b.Id);
        }

        private static int CompareNamedCount(NamedCount a, NamedCount b)
        {
            return string.CompareOrdinal(a.Id, b.Id);
        }

        private static int CompareResearch(DraftResearch a, DraftResearch b)
        {
            return string.CompareOrdinal(a.Item, b.Item);
        }

        private static int CompareHolding(ScenarioHolding a, ScenarioHolding b)
        {
            if (a.TerritoryKey < b.TerritoryKey)
            {
                return -1;
            }

            if (a.TerritoryKey > b.TerritoryKey)
            {
                return 1;
            }

            return 0;
        }

        private static int CompareDerivedTerritory(ScenarioDerivedTerritory a, ScenarioDerivedTerritory b)
        {
            if (a.MapStableKey < b.MapStableKey)
            {
                return -1;
            }

            if (a.MapStableKey > b.MapStableKey)
            {
                return 1;
            }

            return string.CompareOrdinal(a.Id, b.Id);
        }

        private static int ComparePopulation(ScenarioPopulationPlace a, ScenarioPopulationPlace b)
        {
            if (a.TerritoryKey < b.TerritoryKey)
            {
                return -1;
            }

            if (a.TerritoryKey > b.TerritoryKey)
            {
                return 1;
            }

            if (a.MapTownId != b.MapTownId)
            {
                return a.MapTownId.CompareTo(b.MapTownId);
            }

            return string.CompareOrdinal(a.Place, b.Place);
        }

        private sealed class ScenarioHasher
        {
            private readonly XxHash64Builder _hash = new XxHash64Builder();

            public void Int(int value)
            {
                _hash.AddInt(value);
            }

            public void ULong(ulong value)
            {
                _hash.AddULong(value);
            }

            public void Bool(bool value)
            {
                _hash.AddBool(value);
            }

            public void Byte(byte value)
            {
                _hash.AddByte(value);
            }

            public void Text(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                _hash.AddInt(bytes.Length);
                _hash.AddBytes(bytes);
            }

            public void Bytes(byte[] value)
            {
                _hash.AddInt(value.Length);
                _hash.AddBytes(value);
            }

            public ulong Digest()
            {
                return _hash.Digest();
            }
        }

        private readonly struct NamedId
        {
            public readonly string Path;
            public readonly string Id;

            public NamedId(string path, string id)
            {
                Path = path;
                Id = id;
            }
        }

        private readonly struct NamedCount
        {
            public readonly string Path;
            public readonly string Id;
            public readonly int Count;

            public NamedCount(string path, string id, int count)
            {
                Path = path;
                Id = id;
                Count = count;
            }
        }

        private readonly struct DraftHolding
        {
            public readonly string Path;
            public readonly ulong Key;
            public readonly ScenarioPlaceCounts Places;

            public DraftHolding(string path, ulong key, ScenarioPlaceCounts places)
            {
                Path = path;
                Key = key;
                Places = places;
            }
        }

        private readonly struct DraftResearch
        {
            public readonly string Path;
            public readonly string Item;
            public readonly int NotBeforeTick;

            public DraftResearch(string path, string item, int notBeforeTick)
            {
                Path = path;
                Item = item;
                NotBeforeTick = notBeforeTick;
            }
        }

        private readonly struct DraftSeat
        {
            public readonly string Path;
            public readonly int Slot;
            public readonly string FactionId;
            public readonly ScenarioController Controller;
            public readonly string PersonalityId;
            public readonly ulong HomeKey;
            public readonly int Manpower;
            public readonly NamedId[] Buildings;
            public readonly NamedCount[] Units;
            public readonly DraftHolding[] Holdings;
            public readonly NamedId[] Raisable;
            public readonly DraftResearch[] Research;
            public readonly NamedId[] Buildable;

            public DraftSeat(
                string path,
                int slot,
                string factionId,
                ScenarioController controller,
                string personalityId,
                ulong homeKey,
                int manpower,
                NamedId[] buildings,
                NamedCount[] units,
                DraftHolding[] holdings,
                NamedId[] raisable,
                DraftResearch[] research,
                NamedId[] buildable)
            {
                Path = path;
                Slot = slot;
                FactionId = factionId;
                Controller = controller;
                PersonalityId = personalityId;
                HomeKey = homeKey;
                Manpower = manpower;
                Buildings = buildings;
                Units = units;
                Holdings = holdings;
                Raisable = raisable;
                Research = research;
                Buildable = buildable;
            }
        }

        private readonly struct DraftTerritory
        {
            public readonly string Path;
            public readonly string Id;
            public readonly int BakeIndex;
            public readonly ulong Key;
            public readonly string SeatTier;
            public readonly ScenarioPlaceCounts Places;
            public readonly int SceneryPlaces;
            public readonly int DesignerOverridePlaces;
            public readonly ScenarioSiteCounts Sites;

            public DraftTerritory(
                string path,
                string id,
                int bakeIndex,
                ulong key,
                string seatTier,
                ScenarioPlaceCounts places,
                int sceneryPlaces,
                int designerOverridePlaces,
                ScenarioSiteCounts sites)
            {
                Path = path;
                Id = id;
                BakeIndex = bakeIndex;
                Key = key;
                SeatTier = seatTier;
                Places = places;
                SceneryPlaces = sceneryPlaces;
                DesignerOverridePlaces = designerOverridePlaces;
                Sites = sites;
            }
        }

        private readonly struct DraftPopulation
        {
            public readonly string Path;
            public readonly string Place;
            public readonly int MapTownId;
            public readonly ulong Key;
            public readonly int Population;
            public readonly string Tier;
            public readonly string Source;

            public DraftPopulation(
                string path,
                string place,
                int mapTownId,
                ulong key,
                int population,
                string tier,
                string source)
            {
                Path = path;
                Place = place;
                MapTownId = mapTownId;
                Key = key;
                Population = population;
                Tier = tier;
                Source = source;
            }
        }

        private readonly struct DraftDerived
        {
            public readonly ScenarioPlaceCounts HoldingCounts;
            public readonly int CapturablePlaces;
            public readonly int CapturablePlacesFromMap;
            public readonly int DesignerOverridePlaces;
            public readonly int SceneryPlaces;
            public readonly int SceneryPlacesPendingPopulationFix;
            public readonly ScenarioSiteCounts Sites;
            public readonly DraftTerritory[] Territories;
            public readonly DraftPopulation[] PopulationOverrides;
            public readonly DraftPopulation[] CensusFallbackPlaces;

            public DraftDerived(
                ScenarioPlaceCounts holdingCounts,
                int capturablePlaces,
                int capturablePlacesFromMap,
                int designerOverridePlaces,
                int sceneryPlaces,
                int sceneryPlacesPendingPopulationFix,
                ScenarioSiteCounts sites,
                DraftTerritory[] territories,
                DraftPopulation[] populationOverrides,
                DraftPopulation[] censusFallbackPlaces)
            {
                HoldingCounts = holdingCounts;
                CapturablePlaces = capturablePlaces;
                CapturablePlacesFromMap = capturablePlacesFromMap;
                DesignerOverridePlaces = designerOverridePlaces;
                SceneryPlaces = sceneryPlaces;
                SceneryPlacesPendingPopulationFix = sceneryPlacesPendingPopulationFix;
                Sites = sites;
                Territories = territories;
                PopulationOverrides = populationOverrides;
                CensusFallbackPlaces = censusFallbackPlaces;
            }
        }

        private readonly struct Draft
        {
            public readonly int DataVersion;
            public readonly string Id;
            public readonly string MapFile;
            public readonly int MapFormatVersion;
            public readonly string MapFileSha256;
            public readonly byte[] MapSha256;
            public readonly DraftSeat[] Seats;
            public readonly ScenarioMatchRules Rules;
            public readonly DraftDerived Derived;

            public Draft(
                int dataVersion,
                string id,
                string mapFile,
                int mapFormatVersion,
                string mapFileSha256,
                byte[] mapSha256,
                DraftSeat[] seats,
                ScenarioMatchRules rules,
                DraftDerived derived)
            {
                DataVersion = dataVersion;
                Id = id;
                MapFile = mapFile;
                MapFormatVersion = mapFormatVersion;
                MapFileSha256 = mapFileSha256;
                MapSha256 = mapSha256;
                Seats = seats;
                Rules = rules;
                Derived = derived;
            }
        }
    }
}
