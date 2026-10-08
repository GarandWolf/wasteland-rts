using System;
using System.Collections.Generic;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Scenario
{
    internal static class ScenarioErrors
    {
        public static FormatException At(string path, string message)
        {
            return new FormatException(path + ": " + message);
        }
    }

    /// <summary>
    /// One JSON object with an exact key set. Unknown and duplicate keys are rejected.
    /// Every error names the JSON path.
    /// </summary>
    internal sealed class ScenarioObject
    {
        private readonly JsonValue _obj;
        private readonly string _path;
        private readonly bool[] _seen;

        public ScenarioObject(JsonValue obj, string path)
        {
            if (obj.Kind != JsonKind.Object)
            {
                throw ScenarioErrors.At(path, "expected an object.");
            }

            _obj = obj;
            _path = path;
            List<JsonField> fields = obj.ObjectValue;
            _seen = new bool[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (fields[i].Name == fields[j].Name)
                    {
                        throw ScenarioErrors.At(Child(fields[i].Name), "duplicate key '" + fields[i].Name + "'.");
                    }
                }
            }
        }

        public string Path
        {
            get { return _path; }
        }

        public int Count
        {
            get { return _obj.ObjectValue.Count; }
        }

        public string NameAt(int index)
        {
            return _obj.ObjectValue[index].Name;
        }

        public JsonValue ValueAt(int index)
        {
            _seen[index] = true;
            return _obj.ObjectValue[index].Value;
        }

        public string Child(string name)
        {
            return _path + "." + name;
        }

        public int Int(string name, int minInclusive)
        {
            JsonValue value = Require(name);
            string path = Child(name);
            if (value.Kind != JsonKind.Int)
            {
                throw ScenarioErrors.At(path, "expected an integer.");
            }

            if (value.IntValue < minInclusive || value.IntValue > int.MaxValue)
            {
                throw ScenarioErrors.At(path, "integer out of range.");
            }

            return (int)value.IntValue;
        }

        public bool Bool(string name)
        {
            JsonValue value = Require(name);
            string path = Child(name);
            if (value.Kind != JsonKind.Bool)
            {
                throw ScenarioErrors.At(path, "expected a boolean.");
            }

            return value.BoolValue;
        }

        public string String(string name)
        {
            JsonValue value = Require(name);
            return RequireText(value, Child(name));
        }

        public string NullableString(string name, out bool isNull)
        {
            JsonValue value = Require(name);
            string path = Child(name);
            if (value.Kind == JsonKind.Null)
            {
                isNull = true;
                return string.Empty;
            }

            isNull = false;
            return RequireText(value, path);
        }

        public ScenarioObject Object(string name)
        {
            JsonValue value = Require(name);
            return new ScenarioObject(value, Child(name));
        }

        public JsonValue Array(string name)
        {
            JsonValue value = Require(name);
            if (value.Kind != JsonKind.Array)
            {
                throw ScenarioErrors.At(Child(name), "expected an array.");
            }

            return value;
        }

        public string[] Strings(string name, bool allowEmpty)
        {
            JsonValue array = Array(name);
            string path = Child(name);
            if (!allowEmpty && array.ArrayValue.Count < 1)
            {
                throw ScenarioErrors.At(path, "expected a non-empty array.");
            }

            string[] items = new string[array.ArrayValue.Count];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = RequireText(array.ArrayValue[i], path + "[" + i + "]");
            }

            return items;
        }

        public bool TryValue(string name, out JsonValue value)
        {
            int index = Find(name);
            if (index < 0)
            {
                value = new JsonValue();
                return false;
            }

            _seen[index] = true;
            value = _obj.ObjectValue[index].Value;
            return true;
        }

        public void End()
        {
            List<JsonField> fields = _obj.ObjectValue;
            for (int i = 0; i < fields.Count; i++)
            {
                if (!_seen[i])
                {
                    throw ScenarioErrors.At(Child(fields[i].Name), "unknown key '" + fields[i].Name + "'.");
                }
            }
        }

        private JsonValue Require(string name)
        {
            int index = Find(name);
            if (index < 0)
            {
                throw ScenarioErrors.At(Child(name), "missing required key '" + name + "'.");
            }

            _seen[index] = true;
            return _obj.ObjectValue[index].Value;
        }

        private int Find(string name)
        {
            List<JsonField> fields = _obj.ObjectValue;
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Name == name)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string RequireText(JsonValue value, string path)
        {
            if (value.Kind != JsonKind.String)
            {
                throw ScenarioErrors.At(path, "expected a string.");
            }

            if (value.StringValue.Length == 0)
            {
                throw ScenarioErrors.At(path, "expected a non-empty string.");
            }

            return value.StringValue;
        }
    }

    internal static class ScenarioText
    {
        public static void RequireSnake(string value, string path)
        {
            if (value.Length < 1 || value[0] < 'a' || value[0] > 'z')
            {
                throw ScenarioErrors.At(path, "expected a snake_case id.");
            }

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') || c == '_' || (c >= '0' && c <= '9');
                if (!ok)
                {
                    throw ScenarioErrors.At(path, "expected a snake_case id.");
                }
            }
        }

        public static void RequireToken(string value, string path, string expected)
        {
            if (value != expected)
            {
                throw ScenarioErrors.At(path, "expected '" + expected + "'.");
            }
        }

        public static string TownLabel(string townName)
        {
            char[] chars = new char[townName.Length];
            for (int i = 0; i < townName.Length; i++)
            {
                char c = townName[i];
                if (c == ' ')
                {
                    chars[i] = '_';
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    chars[i] = (char)(c + 32);
                }
                else
                {
                    chars[i] = c;
                }
            }

            return new string(chars);
        }
    }

    internal static class TerritoryKeys
    {
        public const int TextLength = 18;

        public static ulong Parse(string text, string path)
        {
            if (text == null || text.Length != TextLength || text[0] != '0' || text[1] != 'x')
            {
                throw Bad(path);
            }

            ulong value = 0UL;
            for (int i = 2; i < TextLength; i++)
            {
                char c = text[i];
                int nibble;
                if (c >= '0' && c <= '9')
                {
                    nibble = c - '0';
                }
                else if (c >= 'A' && c <= 'F')
                {
                    nibble = c - 'A' + 10;
                }
                else
                {
                    throw Bad(path);
                }

                value = (value << 4) | (uint)nibble;
            }

            if (value == 0UL)
            {
                throw ScenarioErrors.At(path, "territory key must be non-zero.");
            }

            return value;
        }

        public static string Format(ulong key)
        {
            char[] text = new char[TextLength];
            text[0] = '0';
            text[1] = 'x';
            const string hex = "0123456789ABCDEF";
            for (int i = 0; i < 16; i++)
            {
                int shift = (15 - i) * 4;
                text[2 + i] = hex[(int)((key >> shift) & 15UL)];
            }

            return new string(text);
        }

        private static FormatException Bad(string path)
        {
            return ScenarioErrors.At(path, "territory key must be 0x followed by 16 uppercase hex digits.");
        }
    }

    internal static class ScenarioSort
    {
        public static void Ordinal(string[] items)
        {
            Array.Sort(items, Compare);
        }

        public static int Compare(string a, string b)
        {
            return string.CompareOrdinal(a, b);
        }

        public static void RequireUnique(string[] items, string path)
        {
            for (int i = 1; i < items.Length; i++)
            {
                if (items[i] == items[i - 1])
                {
                    throw ScenarioErrors.At(path, "duplicate id '" + items[i] + "'.");
                }
            }
        }
    }
}
