using System;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Movement;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiBalanceTablesTests
    {
        /// <summary>
        /// Folder-wide <see cref="BalanceKey.For"/> of <c>data/balance</c> after
        /// v0.7.1 (AI limit keys in rules.json plus notes). Any file under
        /// <c>data/balance</c> moves this hash; re-pin when those files change.
        /// AI still only parses <c>ai/*.json</c>.
        /// </summary>
        public const ulong ExpectedFolderBalanceKey = 0xD58386EC7D063AB6UL;

        [Fact]
        public void LoadsPersonalitiesAndDifficulties_FromBalanceFiles()
        {
            AiBalanceTables tables = LoadBalance();
            Assert.Equal(3, tables.PersonalityCount);
            Assert.Equal(4, tables.DifficultyCount);
            Assert.Equal(ExpectedFolderBalanceKey, tables.BalanceKey);
            Assert.Equal(BalanceKey.For(System.IO.Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance")), tables.BalanceKey);

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
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json");
            AiBalanceTables first = AiBalanceTables.Parse(personalities, difficulty);
            AiBalanceTables second = AiBalanceTables.Parse(personalities, difficulty);
            Assert.Equal(first.PersonalityJsonHash, second.PersonalityJsonHash);
            Assert.Equal(first.DifficultyJsonHash, second.DifficultyJsonHash);

            AiBalanceTables mutated = AiBalanceTables.Parse(personalities, difficulty + "\n");
            Assert.NotEqual(first.DifficultyJsonHash, mutated.DifficultyJsonHash);
        }

        [Fact]
        public void RejectsMissingDataVersion()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("  \"dataVersion\": 1,\n", string.Empty);
            FormatException ex = Assert.Throws<FormatException>(() => AiBalanceTables.Parse(personalities, difficulty));
            Assert.Contains("dataVersion", ex.Message);
        }

        [Fact]
        public void RejectsNonIntegerDataVersion()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json");
            Assert.Throws<FormatException>(() => AiBalanceTables.Parse(
                personalities,
                difficulty.Replace("\"dataVersion\": 1", "\"dataVersion\": 1.0")));
            Assert.Throws<FormatException>(() => AiBalanceTables.Parse(
                personalities,
                difficulty.Replace("\"dataVersion\": 1", "\"dataVersion\": \"1\"")));
        }

        [Fact]
        public void RejectsDuplicatePersonalityId()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json")
                .Replace("\"id\": \"raider\"", "\"id\": \"warlord\"", StringComparison.Ordinal);
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json");
            FormatException ex = Assert.Throws<FormatException>(() => AiBalanceTables.Parse(personalities, difficulty));
            Assert.Contains("Duplicate personality id", ex.Message);
        }

        [Fact]
        public void RejectsFogCheatOtherThanNone()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("\"FogCheat\": \"none\"", "\"FogCheat\": \"omniscient\"");
            Assert.Throws<FormatException>(() => AiBalanceTables.Parse(personalities, difficulty));
        }

        [Fact]
        public void RejectsFractionalEconomyBonus()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("\"EconomyBonusPpt\": 100", "\"EconomyBonusPpt\": 10.0");
            Assert.Throws<FormatException>(() => AiBalanceTables.Parse(personalities, difficulty));
        }

        internal static AiBalanceTables LoadBalance()
        {
            return AiBalanceTables.FromDirectory(
                System.IO.Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance"));
        }
    }
}
