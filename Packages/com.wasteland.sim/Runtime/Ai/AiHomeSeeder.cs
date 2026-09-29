namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Personality → home territory and starting manpower until the sim owns
    /// spawn/HQ placement and the per-map scenario file. This seam is the only
    /// place that knows seats and starting manpower. It does not read
    /// <c>rules.json</c> scenario.
    /// </summary>
    public sealed class AiHomeSeeder : IAiHomeSeeder
    {
        public const int RemnantHomeTerritoryId = 11;
        public const int CompactHomeTerritoryId = 6;
        public const int StartingManpower = 300;

        public int TerritoryIdFor(string personalityId)
        {
            if (personalityId == AiConstants.PersonalityWarlord)
            {
                return RemnantHomeTerritoryId;
            }

            if (personalityId == AiConstants.PersonalitySettler)
            {
                return CompactHomeTerritoryId;
            }

            return 0;
        }

        public void Seed(AiOwnedState owned, AiMapSnapshot map)
        {
            for (int i = 0; i < owned.FactionCount; i++)
            {
                AiFactionState faction = owned.FactionAt(i);
                faction.Manpower = StartingManpower;
                int territoryId = TerritoryIdFor(faction.PersonalityId);
                if (!map.TryGetTerritory(territoryId, out AiTerritoryRecord territory))
                {
                    continue;
                }

                int seatIndex = map.IndexOfTown(territory.SeatTownId);
                if (seatIndex < 0)
                {
                    continue;
                }

                faction.HomeTerritoryId = territory.Id;
                faction.HomeTownId = territory.SeatTownId;
                faction.HqNodeId = map.TownAt(seatIndex).RoadNodeId;
                owned.SetTownOwner(seatIndex, faction.FactionId);
            }
        }
    }
}
