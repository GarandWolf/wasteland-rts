using System;
using System.Collections.Generic;
using Wasteland.Sim.Hashing;

namespace Wasteland.Sim.Movement
{
    /// <summary>Per-class integer movement table, loaded from integer JSON at match load.</summary>
    public sealed class MovementClassTable
    {
        public readonly MovementClass Class;
        public readonly int BaseSpeedCmPerTick;
        public readonly int MaxOffRoadSlopePerMille;
        public readonly bool IgnoreTerrain;
        public readonly int[] LandCoverPpt;
        public readonly int[] RoadSurfacePpt;
        public readonly int[] SlopePpt;

        internal MovementClassTable(
            MovementClass clas,
            int baseSpeedCmPerTick,
            int maxOffRoadSlopePerMille,
            bool ignoreTerrain,
            int[] landCoverPpt,
            int[] roadSurfacePpt,
            int[] slopePpt)
        {
            Class = clas;
            BaseSpeedCmPerTick = baseSpeedCmPerTick;
            MaxOffRoadSlopePerMille = maxOffRoadSlopePerMille;
            IgnoreTerrain = ignoreTerrain;
            LandCoverPpt = landCoverPpt;
            RoadSurfacePpt = roadSurfacePpt;
            SlopePpt = slopePpt;
        }

        public int SlopePptAt(int slopePerMille)
        {
            if (slopePerMille < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slopePerMille));
            }

            int[] table = SlopePpt;
            if (slopePerMille >= table.Length)
            {
                return table[table.Length - 1];
            }

            return table[slopePerMille];
        }
    }

    /// <summary>
    /// Designer movement tables. JSON is parsed with a strict integer-only
    /// reader so load is bit-identical across runtimes.
    /// </summary>
    public sealed class MovementTables
    {
        public const int ClassCount = 4;
        public const int LandCoverCount = 7;
        public const int RoadSurfaceCount = 4;
        private const int SlopeTableCap = 10001;

        private readonly MovementClassTable[] _classes;

        private MovementTables(MovementClassTable[] classes)
        {
            _classes = classes;
        }

        public MovementClassTable this[MovementClass clas]
        {
            get { return _classes[(int)clas]; }
        }

        public ulong ContentHash()
        {
            XxHash64Builder hash = new XxHash64Builder();
            hash.AddInt(ClassCount);
            hash.AddInt(MovementConfig.HierarchyAlgorithmVersion);
            for (int c = 0; c < ClassCount; c++)
            {
                MovementClassTable table = _classes[c];
                hash.AddByte((byte)table.Class);
                hash.AddInt(table.BaseSpeedCmPerTick);
                hash.AddInt(table.MaxOffRoadSlopePerMille);
                hash.AddByte(table.IgnoreTerrain ? (byte)1 : (byte)0);
                AddInts(hash, table.LandCoverPpt);
                AddInts(hash, table.RoadSurfacePpt);
                AddInts(hash, table.SlopePpt);
            }

            return hash.Digest();
        }

        private static void AddInts(XxHash64Builder hash, int[] values)
        {
            hash.AddInt(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                hash.AddInt(values[i]);
            }
        }

        public static MovementTables Default()
        {
            return Parse(BuiltinJson);
        }

        public static MovementTables Parse(string json)
        {
            JsonValue root = IntegerJsonParser.Parse(json);
            if (JsonUtil.Int32(JsonUtil.Field(root, "schemaVersion"), "schemaVersion") != 1)
            {
                throw new FormatException("Unsupported movement-tables schema.");
            }

            JsonValue classes = JsonUtil.Field(root, "classes");
            if (classes.Kind != JsonKind.Array)
            {
                throw new FormatException("'classes' must be an array.");
            }

            MovementClassTable[] tables = new MovementClassTable[ClassCount];
            List<JsonValue> list = classes.ArrayValue;
            for (int i = 0; i < list.Count; i++)
            {
                MovementClassTable table = ParseClass(list[i]);
                int index = (int)table.Class;
                if (tables[index] != null)
                {
                    throw new FormatException("Duplicate movement class " + table.Class + ".");
                }

                tables[index] = table;
            }

            for (int i = 0; i < ClassCount; i++)
            {
                if (tables[i] == null)
                {
                    throw new FormatException("Missing movement class " + (MovementClass)i + ".");
                }
            }

            return new MovementTables(tables);
        }

        /// <summary>
        /// Off-road speed in cm/tick. Floor rounding. 0 is impassable.
        /// Air ignores cover and slope. Grade caps are inclusive of the listed max.
        /// </summary>
        public int OffRoadSpeedCmPerTick(MovementClass clas, LandCover cover, int slopePerMille)
        {
            MovementClassTable table = this[clas];
            if (table.IgnoreTerrain)
            {
                return table.BaseSpeedCmPerTick;
            }

            if (slopePerMille > table.MaxOffRoadSlopePerMille)
            {
                return 0;
            }

            int surfacePpt = table.LandCoverPpt[(int)cover];
            int slopePpt = table.SlopePptAt(slopePerMille);
            return Speed(table.BaseSpeedCmPerTick, surfacePpt, slopePpt);
        }

        /// <summary>Road speed in cm/tick. Slope is ignored (ppt = 1000). Air uses base speed.</summary>
        public int RoadSpeedCmPerTick(MovementClass clas, RoadSurface surface)
        {
            MovementClassTable table = this[clas];
            if (table.IgnoreTerrain)
            {
                return table.BaseSpeedCmPerTick;
            }

            int surfacePpt = table.RoadSurfacePpt[(int)surface];
            return Speed(table.BaseSpeedCmPerTick, surfacePpt, SimConfig.PptScale);
        }

        public static int Speed(int baseSpeedCmPerTick, int surfacePpt, int slopePpt)
        {
            if (baseSpeedCmPerTick < 0 || surfacePpt < 0 || slopePpt < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseSpeedCmPerTick));
            }

            long product = (long)baseSpeedCmPerTick * surfacePpt * slopePpt;
            return (int)(product / MovementConfig.SpeedPptDivisor);
        }

        public static int TicksToTraverse(int lengthCm, int speedCmPerTick)
        {
            if (speedCmPerTick <= 0)
            {
                return 0;
            }

            if (lengthCm <= 0)
            {
                return 0;
            }

            return (int)(((long)lengthCm + speedCmPerTick - 1) / speedCmPerTick);
        }

        private static MovementClassTable ParseClass(JsonValue obj)
        {
            MovementClass clas = ParseClassName(JsonUtil.String(JsonUtil.Field(obj, "class"), "class"));
            int baseSpeed = JsonUtil.Int32(JsonUtil.Field(obj, "baseSpeedCmPerTick"), "baseSpeedCmPerTick");
            int maxSlope = JsonUtil.Int32(JsonUtil.Field(obj, "maxOffRoadSlopePerMille"), "maxOffRoadSlopePerMille");
            bool ignore = false;
            if (JsonUtil.TryField(obj, "ignoreTerrain", out JsonValue ignoreField))
            {
                ignore = JsonUtil.Bool(ignoreField, "ignoreTerrain");
            }

            if (baseSpeed < 0)
            {
                throw new FormatException("baseSpeedCmPerTick must be >= 0.");
            }

            int[] land = ParseNamedInts(JsonUtil.Field(obj, "landCoverPpt"), LandCoverNames, LandCoverCount, "landCoverPpt");
            int[] road = ParseNamedInts(JsonUtil.Field(obj, "roadSurfacePpt"), RoadSurfaceNames, RoadSurfaceCount, "roadSurfacePpt");
            int[] slope = ParseSlopeBands(JsonUtil.Field(obj, "slopeBands"));
            return new MovementClassTable(clas, baseSpeed, maxSlope, ignore, land, road, slope);
        }

        private static int[] ParseNamedInts(JsonValue obj, string[] names, int count, string field)
        {
            if (obj.Kind != JsonKind.Object)
            {
                throw new FormatException(field + " must be an object.");
            }

            int[] values = new int[count];
            bool[] seen = new bool[count];
            for (int i = 0; i < names.Length; i++)
            {
                JsonValue v = JsonUtil.Field(obj, names[i]);
                int ppt = JsonUtil.Int32(v, names[i]);
                if (ppt < 0)
                {
                    throw new FormatException(names[i] + " ppt must be >= 0.");
                }

                values[i] = ppt;
                seen[i] = true;
            }

            for (int i = 0; i < count; i++)
            {
                if (!seen[i])
                {
                    throw new FormatException("Missing " + field + " entry.");
                }
            }

            return values;
        }

        private static int[] ParseSlopeBands(JsonValue array)
        {
            if (array.Kind != JsonKind.Array || array.ArrayValue.Count == 0)
            {
                throw new FormatException("slopeBands must be a non-empty array.");
            }

            List<JsonValue> bands = array.ArrayValue;
            int cursor = 0;
            int maxExclusive = 0;
            for (int i = 0; i < bands.Count; i++)
            {
                int hi = JsonUtil.Int32(JsonUtil.Field(bands[i], "maxExclusive"), "maxExclusive");
                if (hi <= cursor)
                {
                    throw new FormatException("slopeBands must be strictly increasing from 0.");
                }

                maxExclusive = hi;
                cursor = hi;
            }

            int length = maxExclusive;
            if (length > SlopeTableCap)
            {
                length = SlopeTableCap;
            }

            int[] table = new int[length];
            cursor = 0;
            for (int i = 0; i < bands.Count; i++)
            {
                int hi = JsonUtil.Int32(JsonUtil.Field(bands[i], "maxExclusive"), "maxExclusive");
                int ppt = JsonUtil.Int32(JsonUtil.Field(bands[i], "ppt"), "ppt");
                if (ppt < 0)
                {
                    throw new FormatException("slope band ppt must be >= 0.");
                }

                int fillTo = hi < length ? hi : length;
                for (int s = cursor; s < fillTo; s++)
                {
                    table[s] = ppt;
                }

                cursor = fillTo;
                if (cursor >= length)
                {
                    break;
                }
            }

            return table;
        }

        private static MovementClass ParseClassName(string name)
        {
            switch (name)
            {
                case "wheeled":
                    return MovementClass.Wheeled;
                case "tracked":
                    return MovementClass.Tracked;
                case "foot":
                    return MovementClass.Foot;
                case "air":
                    return MovementClass.Air;
                default:
                    throw new FormatException("Unknown movement class '" + name + "'.");
            }
        }

        private static readonly string[] LandCoverNames =
        {
            "field", "scrub", "urban", "forest", "marsh", "water", "ford"
        };

        private static readonly string[] RoadSurfaceNames =
        {
            "dirt", "gravel", "asphalt", "bridgeDeck"
        };

        /// <summary>Canonical v1 tables. Keep in lockstep with Data/movement-tables-v1.json.</summary>
        public const string BuiltinJson = @"{
  ""schemaVersion"": 1,
  ""classes"": [
    {
      ""class"": ""wheeled"",
      ""baseSpeedCmPerTick"": 80,
      ""maxOffRoadSlopePerMille"": 299,
      ""ignoreTerrain"": false,
      ""landCoverPpt"": {
        ""field"": 400,
        ""scrub"": 200,
        ""urban"": 700,
        ""forest"": 80,
        ""marsh"": 0,
        ""water"": 0,
        ""ford"": 200
      },
      ""roadSurfacePpt"": {
        ""dirt"": 700,
        ""gravel"": 850,
        ""asphalt"": 1000,
        ""bridgeDeck"": 900
      },
      ""slopeBands"": [
        { ""maxExclusive"": 50, ""ppt"": 1000 },
        { ""maxExclusive"": 100, ""ppt"": 850 },
        { ""maxExclusive"": 200, ""ppt"": 600 },
        { ""maxExclusive"": 300, ""ppt"": 350 },
        { ""maxExclusive"": 600, ""ppt"": 200 },
        { ""maxExclusive"": 1000, ""ppt"": 100 },
        { ""maxExclusive"": 100000, ""ppt"": 0 }
      ]
    },
    {
      ""class"": ""tracked"",
      ""baseSpeedCmPerTick"": 50,
      ""maxOffRoadSlopePerMille"": 599,
      ""ignoreTerrain"": false,
      ""landCoverPpt"": {
        ""field"": 700,
        ""scrub"": 500,
        ""urban"": 800,
        ""forest"": 400,
        ""marsh"": 200,
        ""water"": 0,
        ""ford"": 400
      },
      ""roadSurfacePpt"": {
        ""dirt"": 800,
        ""gravel"": 900,
        ""asphalt"": 1000,
        ""bridgeDeck"": 950
      },
      ""slopeBands"": [
        { ""maxExclusive"": 50, ""ppt"": 1000 },
        { ""maxExclusive"": 100, ""ppt"": 900 },
        { ""maxExclusive"": 200, ""ppt"": 750 },
        { ""maxExclusive"": 300, ""ppt"": 550 },
        { ""maxExclusive"": 600, ""ppt"": 350 },
        { ""maxExclusive"": 1000, ""ppt"": 150 },
        { ""maxExclusive"": 100000, ""ppt"": 0 }
      ]
    },
    {
      ""class"": ""foot"",
      ""baseSpeedCmPerTick"": 8,
      ""maxOffRoadSlopePerMille"": 999,
      ""ignoreTerrain"": false,
      ""landCoverPpt"": {
        ""field"": 1000,
        ""scrub"": 800,
        ""urban"": 1000,
        ""forest"": 700,
        ""marsh"": 400,
        ""water"": 0,
        ""ford"": 600
      },
      ""roadSurfacePpt"": {
        ""dirt"": 1000,
        ""gravel"": 1000,
        ""asphalt"": 1000,
        ""bridgeDeck"": 900
      },
      ""slopeBands"": [
        { ""maxExclusive"": 50, ""ppt"": 1000 },
        { ""maxExclusive"": 100, ""ppt"": 950 },
        { ""maxExclusive"": 200, ""ppt"": 850 },
        { ""maxExclusive"": 400, ""ppt"": 800 },
        { ""maxExclusive"": 1000, ""ppt"": 700 },
        { ""maxExclusive"": 100000, ""ppt"": 0 }
      ]
    },
    {
      ""class"": ""air"",
      ""baseSpeedCmPerTick"": 200,
      ""maxOffRoadSlopePerMille"": 100000,
      ""ignoreTerrain"": true,
      ""landCoverPpt"": {
        ""field"": 1000,
        ""scrub"": 1000,
        ""urban"": 1000,
        ""forest"": 1000,
        ""marsh"": 1000,
        ""water"": 1000,
        ""ford"": 1000
      },
      ""roadSurfacePpt"": {
        ""dirt"": 1000,
        ""gravel"": 1000,
        ""asphalt"": 1000,
        ""bridgeDeck"": 1000
      },
      ""slopeBands"": [
        { ""maxExclusive"": 100000, ""ppt"": 1000 }
      ]
    }
  ]
}
";
    }
}
