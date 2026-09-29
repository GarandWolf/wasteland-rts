using Wasteland.Sim.Ai;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiBalanceTablesTests
    {
        [Fact]
        public void LoadsPersonalitiesAndDifficulties_FromBalanceFiles()
        {
            AiBalanceTables tables = LoadBalance();
            Assert.Equal(3, tables.PersonalityCount);
            Assert.Equal(4, tables.DifficultyCount);

            AiPersonalityProfile settler = tables.GetPersonality(AiConstants.PersonalitySettler);
            Assert.False(settler.Stub);
            Assert.Equal(2, settler.MinGarrisonPerTown);
            Assert.Equal(AiPosture.Turtle, settler.DefaultPosture);

            AiPersonalityProfile warlord = tables.GetPersonality(AiConstants.PersonalityWarlord);
            Assert.True(warlord.Stub);
            Assert.Equal(AiConstants.PersonalityWarlord, tables.RequireBoundPersonalityId(1));
            Assert.Equal(AiConstants.PersonalitySettler, tables.RequireBoundPersonalityId(2));

            AiDifficultyProfile brutal = tables.GetDifficulty(AiConstants.DifficultyBrutal);
            Assert.Equal(100, brutal.EconomyBonusPpt);
            Assert.Equal(0, brutal.BuildSpeedBonusPpt);
            Assert.Equal(0, brutal.VisionRadiusBonusCm);
            Assert.Equal(AiConstants.FogCheatNone, brutal.FogCheat);
            Assert.Equal(60, brutal.ManpowerReserveSeconds);
            Assert.Equal(0, brutal.ReactionExtraTicks);

            AiDifficultyProfile normal = tables.GetDifficulty(AiConstants.DifficultyNormal);
            Assert.Equal(0, normal.EconomyBonusPpt);
            Assert.Equal(40, normal.StrategicCadenceTicks);
            Assert.Equal(60, normal.ManpowerReserveSeconds);
        }

        [Fact]
        public void FileHashes_AreStableAndSensitiveToBytes()
        {
            string personalities = TestDataPaths.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = TestDataPaths.ReadRepoText("data/balance/ai/ai_difficulty.json");
            AiBalanceTables first = AiBalanceTables.Parse(personalities, difficulty);
            AiBalanceTables second = AiBalanceTables.Parse(personalities, difficulty);
            Assert.Equal(first.PersonalityJsonHash, second.PersonalityJsonHash);
            Assert.Equal(first.DifficultyJsonHash, second.DifficultyJsonHash);

            AiBalanceTables mutated = AiBalanceTables.Parse(personalities, difficulty + "\n");
            Assert.NotEqual(first.DifficultyJsonHash, mutated.DifficultyJsonHash);
        }

        [Fact]
        public void RejectsFogCheatOtherThanNone()
        {
            string personalities = TestDataPaths.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = TestDataPaths.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("\"FogCheat\": \"none\"", "\"FogCheat\": \"omniscient\"");
            Assert.Throws<System.FormatException>(() => AiBalanceTables.Parse(personalities, difficulty));
        }

        [Fact]
        public void RejectsFractionalEconomyBonus()
        {
            string personalities = TestDataPaths.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = TestDataPaths.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("\"EconomyBonusPpt\": 100", "\"EconomyBonusPpt\": 10.0");
            Assert.Throws<System.FormatException>(() => AiBalanceTables.Parse(personalities, difficulty));
        }

        internal static AiBalanceTables LoadBalance()
        {
            return AiBalanceTables.Parse(
                TestDataPaths.ReadRepoText("data/balance/ai/ai_personalities.json"),
                TestDataPaths.ReadRepoText("data/balance/ai/ai_difficulty.json"));
        }
    }
}
