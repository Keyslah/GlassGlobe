using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Offline city gazetteer for the viewpoint search, converted from GeoNames
    /// cities15000. One city per line: name TAB country TAB lat,lon, ordered by
    /// population with the largest first, so "most popular" needs no sort at
    /// runtime. Loaded lazily the first time somebody searches, because the
    /// table is about a megabyte and most sessions never open the page.
    /// </summary>
    public static class CityDataLoader
    {
        public const string ResourceName = "GlassGlobeCities";

        public struct City
        {
            public string Name;
            public string Country;
            public GeoCoordinate Coordinate;

            public City(string name, string country, GeoCoordinate coordinate)
            {
                Name = name;
                Country = country;
                Coordinate = coordinate;
            }
        }

        private static List<City> cities;
        private static bool loadAttempted;

        public static int Count
        {
            get
            {
                EnsureLoaded();
                return cities == null ? 0 : cities.Count;
            }
        }

        /// <summary>
        /// Fills <paramref name="results"/> with the best matches for a query,
        /// most relevant first. An empty query returns the largest cities in the
        /// world, which is a reasonable thing to offer before anyone types.
        /// </summary>
        public static void Search(string query, List<City> results, int maxResults)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();
            EnsureLoaded();
            if (cities == null || maxResults <= 0)
            {
                return;
            }

            string trimmed = (query ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                for (int index = 0; index < cities.Count && results.Count < maxResults; index++)
                {
                    results.Add(cities[index]);
                }

                return;
            }

            // Four relevance tiers. The table is already population-ordered, so
            // scanning once in file order keeps each tier sorted by size.
            List<City> startsWith = new List<City>();
            List<City> contains = new List<City>();
            List<City> byCountry = new List<City>();

            for (int index = 0; index < cities.Count; index++)
            {
                City city = cities[index];

                if (string.Equals(city.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    if (results.Count < maxResults)
                    {
                        results.Add(city);
                    }

                    continue;
                }

                if (city.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    if (startsWith.Count < maxResults)
                    {
                        startsWith.Add(city);
                    }

                    continue;
                }

                if (city.Name.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (contains.Count < maxResults)
                    {
                        contains.Add(city);
                    }

                    continue;
                }

                // Country match is deliberately two-way: the country list in
                // this app uses Natural Earth spellings ("United States of
                // America") while the gazetteer uses GeoNames ones ("United
                // States"), and either should find the other.
                if (byCountry.Count < maxResults && CountryMatches(city.Country, trimmed))
                {
                    byCountry.Add(city);
                }
            }

            AppendUpTo(results, startsWith, maxResults);
            AppendUpTo(results, contains, maxResults);
            AppendUpTo(results, byCountry, maxResults);
        }

        private static bool CountryMatches(string country, string query)
        {
            if (string.IsNullOrEmpty(country) || query.Length < 3)
            {
                return false;
            }

            return country.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                query.IndexOf(country, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AppendUpTo(List<City> results, List<City> source, int maxResults)
        {
            for (int index = 0; index < source.Count && results.Count < maxResults; index++)
            {
                results.Add(source[index]);
            }
        }

        private static void EnsureLoaded()
        {
            if (loadAttempted)
            {
                return;
            }

            loadAttempted = true;
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null || string.IsNullOrEmpty(asset.text))
            {
                Debug.LogWarning("CityDataLoader: no city table at Resources/" + ResourceName + ".");
                return;
            }

            string[] lines = asset.text.Split('\n');
            cities = new List<City>(lines.Length);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] fields = line.Split('\t');
                if (fields.Length < 3)
                {
                    continue;
                }

                GeoCoordinate coordinate;
                if (!TryParseCoordinate(fields[2], out coordinate))
                {
                    continue;
                }

                cities.Add(new City(fields[0], fields[1], coordinate));
            }

            // The text asset is the bulk of the memory here and nothing else
            // needs it once parsed.
            Resources.UnloadAsset(asset);
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
            if (!float.TryParse(
                    token.Substring(0, comma),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out latitude) ||
                !float.TryParse(
                    token.Substring(comma + 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out longitude))
            {
                return false;
            }

            coordinate = new GeoCoordinate(latitude, longitude);
            return true;
        }
    }
}
