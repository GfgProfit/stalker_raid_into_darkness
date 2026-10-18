using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XRay.Importer.Tests
{
    public sealed class TestJson
    {
        private readonly string _text;
        private int _position;

        private TestJson(string text)
        {
            _text = text;
        }

        public static object Parse(string text)
        {
            var parser = new TestJson(text);
            object value = parser.ReadValue();
            parser.SkipWhitespace();
            if (parser._position != text.Length)
            {
                throw new FormatException($"Unexpected data at {parser._position}.");
            }

            return value;
        }

        public static double[] Doubles(object value)
        {
            var list = (List<object>)value;
            var result = new double[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                result[i] = Convert.ToDouble(list[i], CultureInfo.InvariantCulture);
            }

            return result;
        }

        private object ReadValue()
        {
            SkipWhitespace();
            char ch = _text[_position];
            switch (ch)
            {
                case '{':
                    return ReadObject();
                case '[':
                    return ReadArray();
                case '"':
                    return ReadString();
                case 't':
                    _position += 4;
                    return true;
                case 'f':
                    _position += 5;
                    return false;
                case 'n':
                    _position += 4;
                    return null;
                default:
                    return ReadNumber();
            }
        }

        private Dictionary<string, object> ReadObject()
        {
            var result = new Dictionary<string, object>();
            _position++;
            SkipWhitespace();
            if (_text[_position] == '}')
            {
                _position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                string key = ReadString();
                SkipWhitespace();
                _position++;
                result[key] = ReadValue();
                SkipWhitespace();
                char separator = _text[_position++];
                if (separator == '}')
                {
                    return result;
                }
            }
        }

        private List<object> ReadArray()
        {
            var result = new List<object>();
            _position++;
            SkipWhitespace();
            if (_text[_position] == ']')
            {
                _position++;
                return result;
            }

            while (true)
            {
                result.Add(ReadValue());
                SkipWhitespace();
                char separator = _text[_position++];
                if (separator == ']')
                {
                    return result;
                }
            }
        }

        private string ReadString()
        {
            var builder = new StringBuilder();
            _position++;
            while (_text[_position] != '"')
            {
                char ch = _text[_position++];
                if (ch != '\\')
                {
                    builder.Append(ch);
                    continue;
                }

                char escape = _text[_position++];
                switch (escape)
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'u':
                        builder.Append((char)Convert.ToInt32(_text.Substring(_position, 4), 16));
                        _position += 4;
                        break;
                    default:
                        builder.Append(escape);
                        break;
                }
            }

            _position++;
            return builder.ToString();
        }

        private double ReadNumber()
        {
            int start = _position;
            while (_position < _text.Length && "+-0123456789.eE".IndexOf(_text[_position]) >= 0)
            {
                _position++;
            }

            string token = _text.Substring(start, _position - start);
            if (token.Length == 0)
            {
                if (_text.Substring(_position).StartsWith("NaN", StringComparison.Ordinal))
                {
                    _position += 3;
                    return double.NaN;
                }

                throw new FormatException($"Invalid JSON value at {_position}.");
            }

            return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }
    }
}
