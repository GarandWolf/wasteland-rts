using System;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Movement;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiBalanceTablesTests
    {
        /// <summary>
        /// AI-owned <see cref="BalanceKey.For"/> of <c>data/balance/ai</c> only
        /// (personalities + difficulty). Designer files under <c>data/balance</c>
        /// are not part of this pin. Re-pin when AI-owned files change.
        /// </summary>
        public const ulong ExpectedAiOwnedBalanceKey = 0x881F4713FEC0DA41UL;

        [Fact]
        public void AiOwnedFiles_MatchExpectedBalanceKey()
        {
            string folder = System.IO.Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance", "ai");
            Assert.Equal(ExpectedAiOwnedBalanceKey, BalanceKey.For(folder));
        }

        [Fact]
        public void LoadsPersonalitiesAndDifficulties_FromBalanceFiles()
        {
            AiBalanceTables tables = LoadBalance();
            Assert.Equal(3, tables.PersonalityCount);
            Assert.Equal(4, tables.DifficultyCount);
            string folder = System.IO.Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance");
            Assert.Equal(BalanceKey.For(folder), tables.BalanceKey);
            Assert.NotEqual(ExpectedAiOwnedBalanceKey, tables.BalanceKey);
            Assert.True(tables.HasCaptureTable);
            Assert.Equal(AiCaptureTests.ExpectedHamletTicks, tables.CaptureTicksForClass(0));
            Assert.Equal(AiCaptureTests.ExpectedVillageTicks, tables.CaptureTicksForClass(1));
            Assert.Equal(AiCaptureTests.ExpectedTownTicks, tables.CaptureTicksForClass(2));
            Assert.Equal(AiCaptureTests.ExpectedCityTicks, tables.CaptureTicksForClass(3));

            AiPersonalityProfile settler = tables.GetPersonality(AiConstants.PersonalitySettler);
            Assert.False(settler.Stub);
            Assert.Equal(2, settler.MinGarrisonPerTown);
            Assert.Equal(AiPosture.Turtle, settler.DefaultPosture);

            AiPersonalityProfile warlord = tables.GetPersonality(AiConstants.PersonalityWarlord);
            Assert.True(warlord.Stub);
            Assert.Equal(AiConstants.PersonalityWarlord, tables.RequireBoundPersonalityId(1));
            Assert.Equal(AiConstants.PersonalitySettler, tables.RequireBoundPersonalityId(2));

            AiDifficultyProfile brutal = tables.GetDifficulty(AiConstants.DifficultyBrutal);
            Assert.Equal(1100, tables.IncomeMultiplierPpt(AiConstants.DifficultyBrutal));
            Assert.Equal(0, brutal.BuildSpeedBonusPpt);
            Assert.Equal(0, brutal.VisionRadiusBonusCm);
            Assert.Equal(AiConstants.FogCheatNone, brutal.FogCheat);
            Assert.Equal(60, brutal.ManpowerReserveSeconds);
            Assert.Equal(0, brutal.ReactionExtraTicks);

            AiDifficultyProfile normal = tables.GetDifficulty(AiConstants.DifficultyNormal);
            Assert.Equal(1000, tables.IncomeMultiplierPpt(AiConstants.DifficultyNormal));
            Assert.Equal(40, normal.StrategicCadenceTicks);
            Assert.Equal(60, normal.ManpowerReserveSeconds);
            Assert.Equal(20, normal.BattalionManpowerCost);
            Assert.Equal(1500, normal.AttackStrengthRatioPpt);
            Assert.Equal(0, brutal.AttackStrengthRatioPpt);
            Assert.Equal(20, brutal.BattalionManpowerCost);

            AiPersonalityProfile warlordScores = tables.GetPersonality(AiConstants.PersonalityWarlord);
            Assert.Equal(200, warlordScores.NonSeatBonusPpt);
            Assert.Equal(200, warlordScores.BuildUpScoreBonus);
            Assert.Equal(1800, warlordScores.DepotBaseScore);
            Assert.Equal(400, warlordScores.AwayDepotScoreBonus);
            Assert.Equal(300, warlordScores.PrizeTargetBonusPpt);
            Assert.Equal(300, settler.PrizeTargetBonusPpt);
            Assert.Equal(300, tables.GetPersonality(AiConstants.PersonalityRaider).PrizeTargetBonusPpt);
            Assert.Equal("remnant_command", warlordScores.EconomyFactionId);
            Assert.Equal("homestead_compact", settler.EconomyFactionId);

            Assert.True(tables.HasEconomyTable);
            Assert.Equal(2, tables.IncomePerMinForClass(0));
            Assert.Equal(8, tables.IncomePerMinForClass(1));
            Assert.Equal(24, tables.IncomePerMinForClass(2));
            Assert.Equal(48, tables.IncomePerMinForClass(3));
            Assert.Equal(10, tables.HqManpowerPerMin("homestead_compact"));
            Assert.Equal(1150, tables.ManpowerModifierPpt("homestead_compact"));
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
            string difficultyLf = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("\r\n", "\n");
            AssertMissingDataVersion(personalities, difficultyLf);
            AssertMissingDataVersion(personalities, difficultyLf.Replace("\n", "\r\n"));
        }

        private static void AssertMissingDataVersion(string personalities, string difficulty)
        {
            string stripped = difficulty
                .Replace("  \"dataVersion\": 1,\r\n", string.Empty)
                .Replace("  \"dataVersion\": 1,\n", string.Empty);
            Assert.NotEqual(difficulty, stripped);
            FormatException ex = Assert.Throws<FormatException>(() => AiBalanceTables.Parse(personalities, stripped));
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
        public void RejectsMissingPrizeTargetBonusPpt()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json")
                .Replace(
                    "      \"awayDepotScoreBonus\": 400,\n      \"prizeTargetBonusPpt\": 300",
                    "      \"awayDepotScoreBonus\": 400");
            FormatException ex = Assert.Throws<FormatException>(() => AiBalanceTables.Parse(
                personalities,
                GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")));
            Assert.Contains(AiBalanceTables.RuleIdPrizeTargetBonusPpt, ex.Message);
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
                .Replace("\"fogCheat\": \"none\"", "\"fogCheat\": \"omniscient\"");
            Assert.Throws<FormatException>(() => AiBalanceTables.Parse(personalities, difficulty));
        }

        [Fact]
        public void RejectsFractionalBuildSpeedBonus()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("\"buildSpeedBonusPpt\": 0", "\"buildSpeedBonusPpt\": 10.0");
            Assert.Throws<FormatException>(() => AiBalanceTables.Parse(personalities, difficulty));
        }

        [Fact]
        public void RejectsDuplicateDifficultyId()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("\"id\": \"hard\"", "\"id\": \"easy\"", StringComparison.Ordinal);
            FormatException ex = Assert.Throws<FormatException>(() => AiBalanceTables.Parse(personalities, difficulty));
            Assert.Contains("Duplicate difficulty id", ex.Message);
        }

        [Fact]
        public void RejectsNegativePptFields()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("\"buildSpeedBonusPpt\": 0", "\"buildSpeedBonusPpt\": -1");
            FormatException difficultyEx = Assert.Throws<FormatException>(
                () => AiBalanceTables.Parse(personalities, difficulty));
            Assert.Contains("buildSpeedBonusPpt", difficultyEx.Message);

            FormatException personalityEx = Assert.Throws<FormatException>(() => AiBalanceTables.Parse(
                personalities.Replace("\"reservePpt\": 80", "\"reservePpt\": -1", StringComparison.Ordinal),
                GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")));
            Assert.Contains("reservePpt", personalityEx.Message);
        }

        [Fact]
        public void EconomyLoader_FailsWithKeyIdWhenHqMissing()
        {
            AiBalanceTables tables = AiBalanceTables.Parse(
                GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json"),
                GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json"));
            AiPersonalityProfile[] rows = new[]
            {
                tables.GetPersonality(AiConstants.PersonalityWarlord),
                tables.GetPersonality(AiConstants.PersonalitySettler),
                tables.GetPersonality(AiConstants.PersonalityRaider)
            };
            string economy = GoldenMapFiles.ReadRepoText("data/balance/economy.json")
                .Replace("\"remnant_command\": {", "\"not_remnant_command\": {", StringComparison.Ordinal);
            FormatException ex = Assert.Throws<FormatException>(() =>
            {
                AiBalanceTables.ParseEconomy(economy, rows, new AiBalanceTables.RulesState());
            });
            Assert.Contains("remnant_command", ex.Message);
        }

        internal static AiBalanceTables LoadBalance()
        {
            return AiBalanceTables.FromDirectory(
                System.IO.Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance"));
        }
    }
}
