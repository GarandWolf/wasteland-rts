namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Personality → home territory until the sim owns starting placement.
    /// Warlord → Goldendale 11, Compact settler → Hood River 6.
    /// </summary>
    public sealed class AiHomeSeeder : IAiHomeSeeder
    {
        public int TerritoryIdFor(string personalityId)
        {
            if (personalityId == AiConstants.PersonalityWarlord)
            {
                return AiConstants.RemnantHomeTerritoryId;
            }

            if (personalityId == AiConstants.PersonalitySettler)
            {
                return AiConstants.CompactHomeTerritoryId;
            }

            return 0;
        }

        public void Seed(AiOwnedState owned, AiMapSnapshot map)
        {
            for (int i = 0; i < owned.FactionCount; i++)
            {
                AiFactionState faction = owned.FactionAt(i);
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
