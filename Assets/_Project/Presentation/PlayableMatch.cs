using System;
using System.IO;
using System.Text;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Default match the Windows player boots on the Columbia Gorge scenario.
    /// Remnant (faction A, scenario slot 0) is the human seat.
    /// Compact (faction B, scenario slot 1) is the only AI seat.
    /// No UnityEngine types. C# 9.
    /// </summary>
    public static class PlayableMatch
    {
        public const ulong MatchSeed = 0xC01055A1UL;
        public const string ScenarioId = "columbia_gorge_100mi_mvp";

        // Lengths are not in the .sha256 pins. They match the v1-marsh pair in data/golden/README.md.
        public const int MainMapBytes = 607152;
        public const int TerrainMapBytes = 19872744;

        public const int HumanFactionId = AiConstants.DefaultAiFactionA;
        public const int AiFactionId = AiConstants.DefaultAiFactionB;

        const string LfsMessage =
            "Git LFS map files are pointer stubs. Run `git lfs pull`. The main map is 607,152 bytes and the terrain is 19,872,744 bytes.";

        public static int[] AiFactionIds()
        {
            return new int[] { AiFactionId };
        }

        /// <summary>
        /// Same file reads and <see cref="AiMatchHost.Start"/> call the player uses.
        /// </summary>
        public static PlayableMatchBoot CreateHost(string root)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            string json = File.ReadAllText(Path.Combine(root, "data", "balance", "scenarios", ScenarioId + ".json"));
            string mapName = SimScenario.ExpectedWrmbFileName(JsonString(json, "mapFile"));
            string golden = Path.Combine(root, "data", "golden");
            byte[] map = ReadMaterialized(Path.Combine(golden, mapName), MainMapBytes);
            byte[] terrain = ReadMaterialized(Path.Combine(golden, TerrainSibling(mapName)), TerrainMapBytes);
            string balanceDir = Path.Combine(root, "data", "balance");
            BalanceCatalog catalog = BalanceCatalog.LoadFromDirectory(balanceDir);
            SimScenario scenario = SimScenario.Load(json, catalog, map, mapName, ScenarioId);
            AiBalanceTables balance = AiBalanceTables.FromDirectory(balanceDir);
            ulong mapKey = MapKey.For(WrmbFile.Parse(map), AiConstants.MapKeySectionTags());
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(MatchSeed).WithKeys(balance.BalanceKey, mapKey),
                map,
                AiFactionIds(),
                AiConstants.DifficultyNormal,
                balance);
            return new PlayableMatchBoot(host, map, terrain, scenario, balance);
        }

        static string JsonString(string json, string field)
        {
            string key = "\"" + field + "\"";
            int at = json.IndexOf(key, StringComparison.Ordinal);
            int open = json.IndexOf('"', json.IndexOf(':', at + key.Length) + 1);
            int close = json.IndexOf('"', open + 1);
            return json.Substring(open + 1, close - open - 1);
        }

        static string TerrainSibling(string mapFileName)
        {
            int dot = mapFileName.IndexOf('.');
            return mapFileName.Substring(0, dot) + ".terrain" + mapFileName.Substring(dot);
        }

        static byte[] ReadMaterialized(string path, int expectedBytes)
        {
            byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : new byte[0];
            if (bytes.Length != expectedBytes || IsLfsPointer(bytes) || !IsWrmb(bytes))
            {
                throw new InvalidOperationException(LfsMessage);
            }

            return bytes;
        }

        static bool IsWrmb(byte[] bytes)
        {
            return bytes.Length >= 4
                && bytes[0] == (byte)'W'
                && bytes[1] == (byte)'R'
                && bytes[2] == (byte)'M'
                && bytes[3] == (byte)'B';
        }

        static bool IsLfsPointer(byte[] bytes)
        {
            const string prefix = "version https://git-lfs.github.com/spec/v1";
            return bytes.Length >= prefix.Length
                && bytes.Length < 1024
                && Encoding.ASCII.GetString(bytes, 0, prefix.Length) == prefix;
        }
    }

    /// <summary>Files and host from <see cref="PlayableMatch.CreateHost"/>.</summary>
    public sealed class PlayableMatchBoot
    {
        public readonly AiMatchHost Host;
        public readonly byte[] Map;
        public readonly byte[] Terrain;
        public readonly SimScenario Scenario;
        public readonly AiBalanceTables Balance;

        public PlayableMatchBoot(
            AiMatchHost host,
            byte[] map,
            byte[] terrain,
            SimScenario scenario,
            AiBalanceTables balance)
        {
            Host = host;
            Map = map;
            Terrain = terrain;
            Scenario = scenario;
            Balance = balance;
        }
    }
}
