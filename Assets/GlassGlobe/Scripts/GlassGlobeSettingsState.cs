using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Persistent source of truth for the settings UI. Keeping the state separate
    /// from the page renderer lets future settings pages reuse the same values
    /// without coupling themselves to the current immediate-mode UI.
    /// </summary>
    public static class GlassGlobeSettingsState
    {
        private const string Prefix = "GlassGlobe.Settings.";

        private const string CameraFeedKey = Prefix + "CameraFeed";
        private const string HideUserCoordinatesKey = Prefix + "HideUserCoordinates";
        private const string HideFarSideCoordinatesKey = Prefix + "HideFarSideCoordinates";
        private const string HideLocationAccuracyKey = Prefix + "HideLocationAccuracy";
        private const string HideViewedRegionKey = Prefix + "HideViewedRegion";
        private const string ShowViewedFromNameKey = Prefix + "ShowViewedFromName";
        private const string ViewpointOverrideKey = Prefix + "ViewpointOverride";
        private const string ViewpointLatitudeKey = Prefix + "ViewpointLatitude";
        private const string ViewpointLongitudeKey = Prefix + "ViewpointLongitude";
        private const string ViewpointLabelKey = Prefix + "ViewpointLabel";
        private const string CountryLabelsKey = Prefix + "CountryLabels";
        private const string MilkyWayKey = Prefix + "MilkyWay";
        private const string SunKey = Prefix + "Sun";
        private const string MoonKey = Prefix + "Moon";
        private const string WeatherCloudsKey = Prefix + "WeatherClouds";
        private const string WeatherRadarKey = Prefix + "WeatherRadar";

        private static bool loaded;

        public static bool CameraFeedEnabled { get; private set; }
        public static bool HideUserCoordinates { get; private set; }
        public static bool HideFarSideCoordinates { get; private set; }
        public static bool HideLocationAccuracy { get; private set; }
        public static bool HideViewedRegion { get; private set; }
        public static bool ShowViewedFromName { get; private set; }
        public static bool ViewpointOverrideEnabled { get; private set; }
        public static float ViewpointLatitude { get; private set; }
        public static float ViewpointLongitude { get; private set; }
        public static string ViewpointLabel { get; private set; }
        public static bool CountryLabelsVisible { get; private set; }
        public static bool MilkyWayEnabled { get; private set; }
        public static bool SunEnabled { get; private set; }
        public static bool MoonEnabled { get; private set; }
        public static bool WeatherCloudsEnabled { get; private set; }
        public static bool WeatherRadarEnabled { get; private set; }

        public static GeoCoordinate ViewpointCoordinate
        {
            get { return new GeoCoordinate(ViewpointLatitude, ViewpointLongitude); }
        }

        public static string ViewedFromLabel
        {
            get
            {
                Load();
                if (!ViewpointOverrideEnabled)
                {
                    return "Current location";
                }

                return string.IsNullOrWhiteSpace(ViewpointLabel)
                    ? "Selected viewpoint"
                    : ViewpointLabel;
            }
        }

        public static bool PrivacyModeEnabled
        {
            get
            {
                Load();
                return HideUserCoordinates &&
                    HideFarSideCoordinates &&
                    HideLocationAccuracy &&
                    HideViewedRegion &&
                    !ShowViewedFromName;
            }
        }

        public static void Load()
        {
            if (loaded)
            {
                return;
            }

            CameraFeedEnabled = ReadBool(CameraFeedKey, true);
            HideUserCoordinates = ReadBool(HideUserCoordinatesKey, false);
            HideFarSideCoordinates = ReadBool(HideFarSideCoordinatesKey, false);
            HideLocationAccuracy = ReadBool(HideLocationAccuracyKey, false);
            HideViewedRegion = ReadBool(HideViewedRegionKey, false);
            ShowViewedFromName = ReadBool(ShowViewedFromNameKey, true);
            ViewpointOverrideEnabled = ReadBool(ViewpointOverrideKey, false);
            ViewpointLatitude = PlayerPrefs.GetFloat(ViewpointLatitudeKey, 0f);
            ViewpointLongitude = PlayerPrefs.GetFloat(ViewpointLongitudeKey, 0f);
            ViewpointLabel = PlayerPrefs.GetString(ViewpointLabelKey, "Selected viewpoint");
            CountryLabelsVisible = ReadBool(CountryLabelsKey, false);
            MilkyWayEnabled = ReadBool(MilkyWayKey, true);
            SunEnabled = ReadBool(SunKey, true);
            MoonEnabled = ReadBool(MoonKey, true);
            WeatherCloudsEnabled = ReadBool(WeatherCloudsKey, true);
            WeatherRadarEnabled = ReadBool(WeatherRadarKey, true);
            loaded = true;
        }

        public static void SetCameraFeedEnabled(bool value)
        {
            Load();
            CameraFeedEnabled = value;
            Save();
        }

        public static void SetHideUserCoordinates(bool value)
        {
            Load();
            HideUserCoordinates = value;
            Save();
        }

        public static void SetHideFarSideCoordinates(bool value)
        {
            Load();
            HideFarSideCoordinates = value;
            Save();
        }

        public static void SetHideLocationAccuracy(bool value)
        {
            Load();
            HideLocationAccuracy = value;
            Save();
        }

        public static void SetHideViewedRegion(bool value)
        {
            Load();
            HideViewedRegion = value;
            Save();
        }

        public static void SetShowViewedFromName(bool value)
        {
            Load();
            ShowViewedFromName = value;
            Save();
        }

        public static void SetPrivacyMode(bool value)
        {
            Load();
            HideUserCoordinates = value;
            HideFarSideCoordinates = value;
            HideLocationAccuracy = value;
            HideViewedRegion = value;
            ShowViewedFromName = !value;
            Save();
        }

        public static void SetViewpoint(GeoCoordinate coordinate, string label)
        {
            Load();
            ViewpointOverrideEnabled = true;
            ViewpointLatitude = coordinate.Latitude;
            ViewpointLongitude = coordinate.Longitude;
            ViewpointLabel = string.IsNullOrWhiteSpace(label) ? "Selected viewpoint" : label.Trim();
            Save();
        }

        public static void UseRealLocation()
        {
            Load();
            ViewpointOverrideEnabled = false;
            Save();
        }

        public static void SetCountryLabelsVisible(bool value)
        {
            Load();
            CountryLabelsVisible = value;
            Save();
        }

        public static void SetSunEnabled(bool value)
        {
            Load();
            SunEnabled = value;
            Save();
        }

        public static void SetMoonEnabled(bool value)
        {
            Load();
            MoonEnabled = value;
            Save();
        }

        public static void SetMilkyWayEnabled(bool value)
        {
            Load();
            MilkyWayEnabled = value;
            Save();
        }

        public static void SetWeatherCloudsEnabled(bool value)
        {
            Load();
            WeatherCloudsEnabled = value;
            Save();
        }

        public static void SetWeatherRadarEnabled(bool value)
        {
            Load();
            WeatherRadarEnabled = value;
            Save();
        }

        private static bool ReadBool(string key, bool defaultValue)
        {
            return PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) != 0;
        }

        private static void Save()
        {
            PlayerPrefs.SetInt(CameraFeedKey, CameraFeedEnabled ? 1 : 0);
            PlayerPrefs.SetInt(HideUserCoordinatesKey, HideUserCoordinates ? 1 : 0);
            PlayerPrefs.SetInt(HideFarSideCoordinatesKey, HideFarSideCoordinates ? 1 : 0);
            PlayerPrefs.SetInt(HideLocationAccuracyKey, HideLocationAccuracy ? 1 : 0);
            PlayerPrefs.SetInt(HideViewedRegionKey, HideViewedRegion ? 1 : 0);
            PlayerPrefs.SetInt(ShowViewedFromNameKey, ShowViewedFromName ? 1 : 0);
            PlayerPrefs.SetInt(ViewpointOverrideKey, ViewpointOverrideEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(ViewpointLatitudeKey, ViewpointLatitude);
            PlayerPrefs.SetFloat(ViewpointLongitudeKey, ViewpointLongitude);
            PlayerPrefs.SetString(ViewpointLabelKey, ViewpointLabel ?? string.Empty);
            PlayerPrefs.SetInt(CountryLabelsKey, CountryLabelsVisible ? 1 : 0);
            PlayerPrefs.SetInt(MilkyWayKey, MilkyWayEnabled ? 1 : 0);
            PlayerPrefs.SetInt(SunKey, SunEnabled ? 1 : 0);
            PlayerPrefs.SetInt(MoonKey, MoonEnabled ? 1 : 0);
            PlayerPrefs.SetInt(WeatherCloudsKey, WeatherCloudsEnabled ? 1 : 0);
            PlayerPrefs.SetInt(WeatherRadarKey, WeatherRadarEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
