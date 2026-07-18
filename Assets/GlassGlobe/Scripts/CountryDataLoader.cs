using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Loads real country border rings converted from Natural Earth 110m data.
    /// The text asset lives in Resources and holds one ring per line:
    /// name TAB labelLat,labelLon TAB lat,lon lat,lon ...
    /// </summary>
    public static class CountryDataLoader
    {
        public const string ResourceName = "GlassGlobeCountries110m";

        public static List<CountryBorderRenderer.GeoOutline> LoadOutlines()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null || string.IsNullOrEmpty(asset.text))
            {
                return null;
            }

            Color countryColor = new Color(1f, 0.92f, 0.42f, 1f);
            List<CountryBorderRenderer.GeoOutline> outlines = new List<CountryBorderRenderer.GeoOutline>();
            string[] lines = asset.text.Split('\n');

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] fields = line.Split('\t');
                if (fields.Length < 3)
                {
                    continue;
                }

                GeoCoordinate label;
                if (!TryParseCoordinate(fields[1], out label))
                {
                    continue;
                }

                string[] pointTokens = fields[2].Split(' ');
                List<GeoCoordinate> points = new List<GeoCoordinate>(pointTokens.Length);
                foreach (string token in pointTokens)
                {
                    GeoCoordinate point;
                    if (TryParseCoordinate(token, out point))
                    {
                        points.Add(point);
                    }
                }

                if (points.Count < 3)
                {
                    continue;
                }

                CountryBorderRenderer.GeoOutline outline = new CountryBorderRenderer.GeoOutline();
                outline.name = fields[0];
                outline.region = "Country";
                outline.isCountry = true;
                outline.closed = true;
                outline.color = countryColor;
                outline.lineWidth = 0.02f;
                outline.labelCoordinate = label;
                outline.points = points;
                outlines.Add(outline);
            }

            return outlines.Count > 0 ? outlines : null;
        }

        private static bool TryParseCoordinate(string token, out GeoCoordinate coordinate)
        {
            coordinate = new GeoCoordinate(0f, 0f);
            int comma = token.IndexOf(',');
            if (comma <= 0 || comma >= token.Length - 1)
            {
                return false;
            }

            float latitude;
            float longitude;
            if (!float.TryParse(token.Substring(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) ||
                !float.TryParse(token.Substring(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
            {
                return false;
            }

            coordinate = new GeoCoordinate(latitude, longitude);
            return true;
        }
    }
}
