using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Wasteland.Audio;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Play-mode clock for the Columbia Gorge AI-vs-AI match.
    /// Each speed runs more <see cref="AiMatchHost.Step"/> calls per frame. The sim tick stays 20 Hz.
    /// </summary>
    public sealed class GorgeListeningHarness : MonoBehaviour
    {
        public const ulong MatchSeed = 0xC01055A1UL;
        public const int MaxTicksPerFrame = 64;

        // Lengths are not in the .sha256 pins (those files are the hash and the file name).
        // They match the v1-marsh pair in data/golden/README.md. A pin bump must update both.
        public const int MainMapBytes = 607152;
        public const int TerrainMapBytes = 19872744;

        const string ScenarioId = "columbia_gorge_100mi_mvp";
        const string LfsMessage =
            "Git LFS map files are pointer stubs. Run `git lfs pull`. The main map is 607,152 bytes and the terrain is 19,872,744 bytes.";

        AiMatchHost _host;
        TacticalMatchBridge _bridge;
        [SerializeField] CombatLoopManager _loops;
        int _cursor;
        int _ticksPerFrame = 1;
        int _firstContestTick = -1;
        int _firstDestroyedTick = -1;
        bool _over;
        string _error;

        public int TicksPerFrame
        {
            get { return _ticksPerFrame; }
            set { _ticksPerFrame = value < 0 ? 0 : value; }
        }

        public bool CombatBedActive
        {
            get { return _bridge != null && _bridge.BedActive; }
        }

        public int LoggerTick
        {
            get { return _host == null || _host.Tick < 1 ? 0 : _host.Tick - 1; }
        }

        public int FirstContestTick
        {
            get { return _firstContestTick; }
        }

        public int FirstBattalionDestroyedTick
        {
            get { return _firstDestroyedTick; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootBootstrap()
        {
            if (SceneManager.GetActiveScene().name != "Bootstrap")
            {
                return;
            }

            if (FindFirstObjectByType<GorgeListeningHarness>() != null)
            {
                return;
            }

            new GameObject("Gorge Listening").AddComponent<GorgeListeningHarness>();
        }

        void Awake()
        {
            try
            {
                Begin();
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                throw;
            }
        }

        void Update()
        {
            for (int i = 0; i < TicksPerFrame && _host != null && !_over; i++)
            {
                _host.Step();
                Drain();
            }

            if (_bridge != null && _host != null)
            {
                _bridge.Sync(_host.World);
            }
        }

        void OnDestroy()
        {
            if (_host != null)
            {
                _host.Shutdown();
                _host = null;
            }
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 460f, 90f));
            GUILayout.Label(_error ?? ("Gorge AI vs AI  tick " + LoggerTick + (CombatBedActive ? "  combat bed on" : "  combat bed quiet")));
            if (_error == null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("1x")) TicksPerFrame = 1;
                if (GUILayout.Button("4x")) TicksPerFrame = 4;
                if (GUILayout.Button("16x")) TicksPerFrame = 16;
                if (GUILayout.Button("max")) TicksPerFrame = MaxTicksPerFrame;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        public void StepOne()
        {
            if (_host == null || _over)
            {
                return;
            }

            _host.Step();
            Drain();
        }

        void Begin()
        {
            string root = FindRepoRoot();
            string json = File.ReadAllText(Path.Combine(root, "data", "balance", "scenarios", ScenarioId + ".json"));
            string mapName = SimScenario.ExpectedWrmbFileName(JsonString(json, "mapFile"));
            string golden = Path.Combine(root, "data", "golden");
            byte[] map = ReadMaterialized(Path.Combine(golden, mapName), MainMapBytes);
            byte[] terrain = ReadMaterialized(Path.Combine(golden, TerrainSibling(mapName)), TerrainMapBytes);
            string balanceDir = Path.Combine(root, "data", "balance");
            SimScenario scenario = SimScenario.Load(json, BalanceCatalog.LoadFromDirectory(balanceDir), map, mapName, ScenarioId);
            AiBalanceTables balance = AiBalanceTables.FromDirectory(balanceDir);
            ulong mapKey = MapKey.For(WrmbFile.Parse(map), AiConstants.MapKeySectionTags());
            _host = AiMatchHost.Start(
                MatchConfig.Default(MatchSeed).WithKeys(balance.BalanceKey, mapKey),
                map,
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                balance);
            if (_loops == null)
            {
                _loops = GetComponent<CombatLoopManager>();
            }

            if (_loops == null)
            {
                _loops = gameObject.AddComponent<CombatLoopManager>();
            }

            if (_loops.Bank == null)
            {
                AssignEditorBank(_loops);
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Tactical Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            TacticalCamera tactical = camera.GetComponent<TacticalCamera>();
            if (tactical == null)
            {
                tactical = camera.gameObject.AddComponent<TacticalCamera>();
            }

            TacticalMapView view = gameObject.GetComponent<TacticalMapView>();
            if (view == null)
            {
                view = gameObject.AddComponent<TacticalMapView>();
            }

            _bridge = gameObject.GetComponent<TacticalMatchBridge>();
            if (_bridge == null)
            {
                _bridge = gameObject.AddComponent<TacticalMatchBridge>();
            }

            _bridge.Open(MapDocument.Load(map, terrain), scenario, _loops, tactical, view);
        }

        // World.Events is append-only for the life of the world. SimWorld.Emit only
        // Add's to _events. PruneEventHistory drops _eventHistory (the last
        // EventHistoryTicks), which backs CopyEventsInRange, and does not touch Events.
        // An index cursor therefore cannot skip or re-read an event.
        void Drain()
        {
            if (_bridge != null)
            {
                _bridge.BeginTick(LoggerTick);
            }

            IReadOnlyList<SimEvent> events = _host.World.Events;
            for (int i = _cursor; i < events.Count; i++)
            {
                Apply(events[i]);
            }

            _cursor = events.Count;
            if (_bridge != null)
            {
                _bridge.EndTick();
            }
        }

        void Apply(SimEvent ev)
        {
            int kind = (int)ev.Kind;
            if (kind == (int)SimEventKind.TownContestStarted && _firstContestTick < 0)
            {
                _firstContestTick = ev.Tick;
            }

            if (kind == (int)SimEventKind.BattalionDestroyed && _firstDestroyedTick < 0)
            {
                _firstDestroyedTick = ev.Tick;
            }

            if (kind == (int)SimEventKind.TownContestStarted || kind == (int)SimEventKind.TownContestEnded)
            {
                if (_bridge != null)
                {
                    _bridge.Note(ev);
                }

                return;
            }

            if (kind == (int)SimEventKind.MatchEnded)
            {
                if (_bridge != null)
                {
                    _bridge.ClearBeds();
                }

                _over = true;
                return;
            }

            // Kind 15 is the garrison loss. Contest end/start on that same tick keeps the bed up.
            if (kind == (int)SimEventKind.BattalionDestroyed)
            {
                return;
            }
        }

        static void AssignEditorBank(CombatLoopManager loops)
        {
#if UNITY_EDITOR
            SoundBank bank = UnityEditor.AssetDatabase.LoadAssetAtPath<SoundBank>(SoundBank.PlaceholderAssetPath);
            if (bank == null)
            {
                throw new InvalidOperationException("Missing " + SoundBank.PlaceholderAssetPath + ".");
            }

            UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(loops);
            UnityEditor.SerializedProperty bankProp = so.FindProperty("_bank");
            if (bankProp == null)
            {
                throw new InvalidOperationException("CombatLoopManager has no _bank field.");
            }

            bankProp.objectReferenceValue = bank;
            so.ApplyModifiedPropertiesWithoutUndo();
#else
            throw new InvalidOperationException("The combat sound bank is not assigned on the listening object.");
#endif
        }

        static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "data", "balance", "scenarios", ScenarioId + ".json")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not find scenario " + ScenarioId + ".");
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
            byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
            if (bytes.Length != expectedBytes || IsLfsPointer(bytes) || !IsWrmb(bytes))
            {
                throw new InvalidOperationException(LfsMessage);
            }

            return bytes;
        }

        static bool IsWrmb(byte[] bytes)
        {
            return bytes.Length >= 4 && bytes[0] == (byte)'W' && bytes[1] == (byte)'R' && bytes[2] == (byte)'M' && bytes[3] == (byte)'B';
        }

        static bool IsLfsPointer(byte[] bytes)
        {
            const string prefix = "version https://git-lfs.github.com/spec/v1";
            return bytes.Length >= prefix.Length && bytes.Length < 1024
                && Encoding.ASCII.GetString(bytes, 0, prefix.Length) == prefix;
        }
    }
}
