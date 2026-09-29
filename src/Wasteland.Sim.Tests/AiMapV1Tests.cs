using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Movement;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiMapV1Tests
    {
        /// <summary>
        /// Gorge merged-v1 wheeled-blocked count. AI tests load the v1 pair
        /// (<c>golden-ci-columbia-gorge-100.v1.wrmb</c>), not the marsh pair
        /// (29; chokepoints 5, 13, 21). Switching AI tests to marsh is a later step.
        /// </summary>
        public const int ExpectedWheeledBlockedChokepoints = 26;

        [Fact]
        public void NeighborLists_DoNotJoinGoldendaleAndHoodRiver()
        {
            AiMapSnapshot map = Load();
            Assert.Equal(WrmbFile.FormatVersionV1, map.FormatVersion);
            Assert.Equal(AiConstants.MapNeighborListAdjacencySource, map.Adjacency.SourceName);
            Assert.False(map.Adjacency.AreNeighbors(11, 6));
            Assert.False(map.Adjacency.AreNeighbors(6, 11));
            Assert.True(map.Adjacency.AreNeighbors(11, 4));
            Assert.True(map.Adjacency.AreNeighbors(6, 4));
            Assert.True(map.Adjacency.AreNeighbors(11, 9));
            Assert.True(map.Adjacency.AreNeighbors(6, 9));
        }

        [Fact]
        public void SeatsAndStableKeys_MatchGorgeV1()
        {
            AiMapSnapshot map = Load();
            Assert.Equal(14, map.TheDallesTownId);
            Assert.Equal(4, map.TheDallesTerritoryId);
            Assert.Equal(33, map.GoldendaleTownId);
            Assert.Equal(26, map.HoodRiverTownId);
            Assert.Equal(30, map.WhiteSalmonTownId);
            Assert.Equal(32, map.KlickitatTownId);

            Assert.True(map.TryGetTerritory(9, out AiTerritoryRecord whiteSalmon));
            Assert.Equal(30, whiteSalmon.SeatTownId);
            Assert.Equal(1152921504757793049UL, whiteSalmon.StableKey);

            Assert.True(map.TryGetTown(32, out AiTownRecord klickitat));
            Assert.Equal(9, klickitat.TerritoryId);
            Assert.NotEqual(whiteSalmon.SeatTownId, klickitat.Id);

            Assert.True(map.TryGetTerritory(11, out AiTerritoryRecord goldendale));
            Assert.Equal(33, goldendale.SeatTownId);
            Assert.Equal(1152921504757792391UL, goldendale.StableKey);
        }

        [Fact]
        public void WheeledBlockedAndDefenseGroups_MatchMapData()
        {
            AiMapSnapshot map = Load();
            Assert.Equal(ExpectedWheeledBlockedChokepoints, map.WheeledBlockedCount);

            int grouped = 0;
            HashSet<byte> groups = new HashSet<byte>();
            int wheeled = 0;
            for (int i = 0; i < map.ChokepointCount; i++)
            {
                AiChokepointRecord choke = map.ChokepointAt(i);
                if (choke.BypassWheeledBlocked != 0)
                {
                    wheeled++;
                }

                if (choke.HasDefenseGroup)
                {
                    grouped++;
                    groups.Add(choke.DefenseGroupId);
                    Assert.True(choke.IsBorder);
                }
            }

            Assert.Equal(ExpectedWheeledBlockedChokepoints, wheeled);
            Assert.Equal(GoldenMapV1Tests.ExpectedDefenseGroups, groups.Count);
            Assert.True(grouped >= groups.Count);
        }

        [Fact]
        public void NodeTerritoryIds_AreStableLookups()
        {
            AiMapSnapshot map = Load();
            Assert.Equal((ushort)1, map.Roads.GetNodeTerritoryId(0));
            Assert.Equal((ushort)13, map.Roads.GetNodeTerritoryId(map.Roads.NodeCount - 1));
            Assert.True(map.Roads.TryGetNodeIndex(191, out int n191));
            Assert.Equal((ushort)1, map.Roads.GetNodeTerritoryId(n191));
        }

        [Fact]
        public void PickedPosts_AreGroupedBorderChokepoints()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            host.Run(80);

            for (int f = 0; f < host.Ai.Owned.FactionCount; f++)
            {
                AiFactionState faction = host.Ai.Owned.FactionAt(f);
                Assert.True(faction.PickedPostCount > 0);
                for (int p = 0; p < faction.PickedPostCount; p++)
                {
                    int chokeId = faction.PickedPostIds[p];
                    Assert.True(host.Ai.PickedPostMatchesMap(faction.FactionId, chokeId));
                    Assert.True(host.Ai.Map.TryGetChokepoint(chokeId, out AiChokepointRecord choke));
                    Assert.True(choke.HasDefenseGroup);
                    Assert.True(choke.IsBorder);
                    Assert.Equal(choke.DefenseGroupId, (byte)faction.PickedGroupIds[p]);
                    bool ownA = host.Ai.OwnsTerritory(faction.FactionId, choke.SideATerritoryId);
                    bool ownB = host.Ai.OwnsTerritory(faction.FactionId, choke.SideBTerritoryId);
                    Assert.True(ownA != ownB);
                }
            }

            host.Shutdown();
        }

        [Fact]
        public void BrutalIncome_IsTenPercentOnly()
        {
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();
            AiMatchHost normal = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                map,
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                balance);
            AiMatchHost brutal = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                map,
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyBrutal,
                balance);

            Assert.Equal(0, normal.Ai.Difficulty.EconomyBonusPpt);
            Assert.Equal(100, brutal.Ai.Difficulty.EconomyBonusPpt);
            Assert.Equal(AiConstants.FogCheatNone, brutal.Ai.Difficulty.FogCheat);

            int normalIncome = normal.Ai.ManpowerIncomePerSecond(AiConstants.DefaultAiFactionA);
            int brutalIncome = brutal.Ai.ManpowerIncomePerSecond(AiConstants.DefaultAiFactionA);
            Assert.Equal((normalIncome * 1100) / 1000, brutalIncome);
            Assert.True(brutalIncome > normalIncome);

            normal.Shutdown();
            brutal.Shutdown();
        }

        [Fact]
        public void DepotSearch_RequiresOwnedSeat()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            host.Run(120);

            AiFactionState remnant = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionA);
            Assert.True(host.Ai.OwnsTerritory(remnant.FactionId, AiConstants.RemnantHomeTerritoryId));
            Assert.False(host.Ai.OwnsTerritory(remnant.FactionId, AiConstants.WhiteSalmonTerritoryId));
            for (int i = 0; i < remnant.DepotCount; i++)
            {
                Assert.NotEqual(AiConstants.WhiteSalmonTerritoryId, remnant.Depots[i].TerritoryId);
                Assert.True(host.Ai.OwnsTerritory(remnant.FactionId, remnant.Depots[i].TerritoryId));
            }

            host.Shutdown();
        }

        private static AiMapSnapshot Load()
        {
            return AiMapSnapshot.FromWrmb(AiSeamsTests.LoadGorgeV1Bytes());
        }
    }
}
