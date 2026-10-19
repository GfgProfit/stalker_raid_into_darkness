using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class Utils
    {
        public static string ToHexRGB(Color color)
        {
            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);

            return $"#{r:X2}{g:X2}{b:X2}";
        }

        public static string ToHexRGBA(Color color)
        {
            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255);

            return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
        }

        public static string ToHexRGB(Color32 color) => $"#{color.r:X2}{color.g:X2}{color.b:X2}";

        public static string ToHexRGBA(Color32 color) => $"#{color.r:X2}{color.g:X2}{color.b:X2}{color.a:X2}";

        public static string FormatNumber(long value, char separator)
        {
            if (value < 1000)
            {
                return value.ToString();
            }

            List<char> chars = new();

            var stringValue = value.ToString();
            var offset = 0;

            for (var i = stringValue.Length - 1; i >= 0; i--)
            {
                chars.Add(stringValue[i]);

                if ((chars.Count - offset) % 3 == 0)
                {
                    chars.Add(separator);
                    offset++;
                }
            }

            chars.Reverse();

            return string.Join("", chars.ToArray()).Trim(separator);
        }
    }