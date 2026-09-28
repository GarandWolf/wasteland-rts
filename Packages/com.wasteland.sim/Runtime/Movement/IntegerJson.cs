using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Wasteland.Sim.Movement
{
    internal enum JsonKind : byte
    {
        Null = 0,
        Bool = 1,
        Int = 2,
        String = 3,
        Array = 4,
        Object = 5
    }

    internal sealed class JsonValue
    {
        public JsonKind Kind;
        public bool BoolValue;
        public long IntValue;
        public string StringValue = string.Empty;
        public List<JsonValue> ArrayValue = new List<JsonValue>();
        public List<JsonField> ObjectValue = new List<JsonField>();
    }

    internal struct JsonField
    {
        public string Name;
        public JsonValue Value;
    }

    /// <summary>
    /// Strict integer JSON. Rejects fractions, exponents, comments, and trailing commas.
    /// Object fields are stored in parse order and looked up by linear scan.
    /// </summary>
    internal sealed class IntegerJsonParser
    {
        private readonly string _text;
        private int _i;

        private IntegerJsonParser(string text)
        {
            _text = text;
        }

        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            IntegerJsonParser parser = new IntegerJsonParser(text);
            JsonValue value = parser.ParseValue();
            parser.SkipWs();
            if (parser._i != parser._text.Length)
            {
                throw parser.Error("Trailing content after JSON value.");
            }

            return value;
        }

        private JsonValue ParseValue()
        {
            SkipWs();
            if (_i >= _text.Length)
            {
                throw Error("Unexpected end of JSON.");
            }

            char c = _text[_i];
            if (c == '{')
            {
                return ParseObject();
            }

            if (c == '[')
            {
                return ParseArray();
            }

            if (c == '"')
            {
                return ParseString();
            }

            if (c == 't' || c == 'f')
            {
                return ParseBool();
            }

            if (c == 'n')
            {
                return ParseNull();
            }

            if (c == '-' || (c >= '0' && c <= '9'))
            {
                return ParseInt();
            }

            throw Error("Unexpected character '" + c + "'.");
        }

        private JsonValue ParseObject()
        {
            _i++;
            JsonValue obj = new JsonValue
            {
                Kind = JsonKind.Object,
                ObjectValue = new List<JsonField>()
            };

            SkipWs();
            if (Peek('}'))
            {
                _i++;
                return obj;
            }

            while (true)
            {
                SkipWs();
                if (_i >= _text.Length || _text[_i] != '"')
                {
                    throw Error("Expected object key.");
                }

                JsonValue key = ParseString();
                SkipWs();
                if (!Peek(':'))
                {
                    throw Error("Expected ':' after key.");
                }

                _i++;
                JsonValue value = ParseValue();
                obj.ObjectValue.Add(new JsonField { Name = key.StringValue, Value = value });
                SkipWs();
                if (Peek('}'))
                {
                    _i++;
                    return obj;
                }

                if (!Peek(','))
                {
                    throw Error("Expected ',' or '}' in object.");
                }

                _i++;
                SkipWs();
                if (Peek('}'))
                {
                    throw Error("Trailing comma in object.");
                }
            }
        }

        private JsonValue ParseArray()
        {
            _i++;
            JsonValue arr = new JsonValue
            {
                Kind = JsonKind.Array,
                ArrayValue = new List<JsonValue>()
            };

            SkipWs();
            if (Peek(']'))
            {
                _i++;
                return arr;
            }

            while (true)
            {
                arr.ArrayValue.Add(ParseValue());
                SkipWs();
                if (Peek(']'))
                {
                    _i++;
                    return arr;
                }

                if (!Peek(','))
                {
                    throw Error("Expected ',' or ']' in array.");
                }

                _i++;
                SkipWs();
                if (Peek(']'))
                {
                    throw Error("Trailing comma in array.");
                }
            }
        }

        private JsonValue ParseString()
        {
            _i++;
            StringBuilder sb = new StringBuilder();
            while (_i < _text.Length)
            {
                char c = _text[_i++];
                if (c == '"')
                {
                    return new JsonValue { Kind = JsonKind.String, StringValue = sb.ToString() };
                }

                if (c == '\\')
                {
                    if (_i >= _text.Length)
                    {
                        throw Error("Unterminated string escape.");
                    }

                    char e = _text[_i++];
                    switch (e)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            sb.Append(e);
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        default:
                            throw Error("Unsupported string escape.");
                    }
                }
                else if (c < 32)
                {
                    throw Error("Control character in string.");
                }
                else
                {
                    sb.Append(c);
                }
            }

            throw Error("Unterminated string.");
        }

        private JsonValue ParseInt()
        {
            int start = _i;
            if (Peek('-'))
            {
                _i++;
            }

            if (_i >= _text.Length || _text[_i] < '0' || _text[_i] > '9')
            {
                throw Error("Expected digit.");
            }

            if (_text[_i] == '0' && _i + 1 < _text.Length && _text[_i + 1] >= '0' && _text[_i + 1] <= '9')
            {
                throw Error("Leading zeros are not allowed.");
            }

            while (_i < _text.Length && _text[_i] >= '0' && _text[_i] <= '9')
            {
                _i++;
            }

            if (_i < _text.Length && (_text[_i] == '.' || _text[_i] == 'e' || _text[_i] == 'E'))
            {
                throw Error("Fractional and exponent JSON numbers are not allowed.");
            }

            string slice = _text.Substring(start, _i - start);
            if (!long.TryParse(slice, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            {
                throw Error("Integer out of range.");
            }

            return new JsonValue { Kind = JsonKind.Int, IntValue = value };
        }

        private JsonValue ParseBool()
        {
            if (Match("true"))
            {
                return new JsonValue { Kind = JsonKind.Bool, BoolValue = true };
            }

            if (Match("false"))
            {
                return new JsonValue { Kind = JsonKind.Bool, BoolValue = false };
            }

            throw Error("Expected true or false.");
        }

        private JsonValue ParseNull()
        {
            if (!Match("null"))
            {
                throw Error("Expected null.");
            }

            return new JsonValue { Kind = JsonKind.Null };
        }

        private void SkipWs()
        {
            while (_i < _text.Length)
            {
                char c = _text[_i];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                {
                    return;
                }

                _i++;
            }
        }

        private bool Peek(char c)
        {
            return _i < _text.Length && _text[_i] == c;
        }

        private bool Match(string token)
        {
            if (_i + token.Length > _text.Length)
            {
                return false;
            }

            for (int k = 0; k < token.Length; k++)
            {
                if (_text[_i + k] != token[k])
                {
                    return false;
                }
            }

            _i += token.Length;
            return true;
        }

        private FormatException Error(string message)
        {
            return new FormatException("JSON at " + _i + ": " + message);
        }
    }

    internal static class JsonUtil
    {
        public static JsonValue Field(JsonValue obj, string name)
        {
            if (obj.Kind != JsonKind.Object)
            {
                throw new FormatException("Expected JSON object.");
            }

            List<JsonField> fields = obj.ObjectValue;
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Name == name)
                {
                    return fields[i].Value;
                }
            }

            throw new FormatException("Missing JSON field '" + name + "'.");
        }

        public static bool TryField(JsonValue obj, string name, out JsonValue value)
        {
            value = new JsonValue();
            if (obj.Kind != JsonKind.Object)
            {
                return false;
            }

            List<JsonField> fields = obj.ObjectValue;
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Name == name)
                {
                    value = fields[i].Value;
                    return true;
                }
            }

            return false;
        }

        public static int Int32(JsonValue value, string name)
        {
            if (value.Kind != JsonKind.Int)
            {
                throw new FormatException("Field '" + name + "' must be an integer.");
            }

            if (value.IntValue < int.MinValue || value.IntValue > int.MaxValue)
            {
                throw new FormatException("Field '" + name + "' does not fit in int32.");
            }

            return (int)value.IntValue;
        }

        public static string String(JsonValue value, string name)
        {
            if (value.Kind != JsonKind.String)
            {
                throw new FormatException("Field '" + name + "' must be a string.");
            }

            return value.StringValue;
        }

        public static bool Bool(JsonValue value, string name)
        {
            if (value.Kind != JsonKind.Bool)
            {
                throw new FormatException("Field '" + name + "' must be a boolean.");
            }

            return value.BoolValue;
        }
    }
}
