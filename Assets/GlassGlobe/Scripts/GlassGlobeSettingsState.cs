using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Persistent source of truth for the settings UI. Keeping the state separate
    /// from the page renderer lets future settings pages reuse the same values
    /// without coupling themselves to the current immediate-mode UI. Each setter
    /// persists only its own key(s), so a single toggle no longer rewrites and
    /// flushes the entire preference block.
    /// </summary>
    public static class GlassGlobeSettingsState
    {
        private const string Prefix = "GlassGlobe.Settings.";

        private const string CameraFeedKey = Prefix + "CameraFeed";
        private const string MainHudKey = Prefix + "MainHud";
        private const string CountryBannerKey = Prefix + "CountryBanner";
        private const string CountryOutlineColorKey = Prefix + "CountryOutlineColor";
        private const string GridColorKey = Prefix + "GridColor";
        private const string GridVisibleKey = Prefix + "GridVisible";
        private const string CountryOutlineThicknessKey = Prefix + "CountryOutlineThickness";
        private const string GridThicknessKey = Prefix + "GridThickness";
        private const string ShowSetNorthButtonKey = Prefix + "ShowSetNorthButton";
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
        private const string NightLightsKey = Prefix + "NightLights";
        private const string RimGlowKey = Prefix + "RimGlow";
        private const string WaterArtKey = Prefix + "WaterArt";
        private const string WaterArtOpacityKey = Prefix + "WaterArtOpacity";
        private const string LandArtKey = Prefix + "LandArt";
        private const string LandArtOpacityKey = Prefix + "LandArtOpacity";
        private const string OceanArtKey = Prefix + "OceanArt";
        private const string OceanArtOpacityKey = Prefix + "OceanArtOpacity";
        private const string ArtCloudsKey = Prefix + "ArtClouds";
        private const string ArtCloudsOpacityKey = Prefix + "ArtCloudsOpacity";
        private const string WeatherCloudsKey = Prefix + "WeatherClouds";
        private const string WeatherRadarKey = Prefix + "WeatherRadar";
        private const string SatellitesKey = Prefix + "Satellites";
        private const string EarthquakesKey = Prefix + "Earthquakes";
        private const string HeadingFineOffsetKey = Prefix + "HeadingFineOffsetV4";
        private const string LegacyHeadingOffsetKey = Prefix + "HeadingOffsetV3";
        private const string LegacyManualHeadingKey = Prefix + "ManualHeadingCalibrationV3";
        private const string ViewpointCategoryKey = Prefix + "Category.Viewpoint";
        private const string BackgroundCategoryKey = Prefix + "Category.Background";
        private const string DisplayCategoryKey = Prefix + "Category.Display";
        private const string EarthStylesCategoryKey = Prefix + "Category.EarthStyles";
        private const string WeatherCategoryKey = Prefix + "Category.Weather";
        private const string LiveDataCategoryKey = Prefix + "Category.LiveData";
        private const string OrientCategoryKey = Prefix + "Category.Orient";
        private const string PrivacyCategoryKey = Prefix + "Category.Privacy";

        private static bool loaded;

        public static bool CameraFeedEnabled { get; private set; }
        public static bool MainHudVisible { get; private set; }
        public static bool CountryBannerVisible { get; private set; }
        public static Color CountryOutlineColor { get; private set; }
        public static Color GridColor { get; private set; }
        public static bool GridVisible { get; private set; }
        public static float CountryOutlineThickness { get; private set; }
        public static float GridThickness { get; private set; }
        public static bool ShowSetNorthButton { get; private set; }
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
        public static bool NightLightsEnabled { get; private set; }
        public static bool RimGlowEnabled { get; private set; }
        public static bool WaterArtEnabled { get; private set; }
        public static float WaterArtOpacity { get; private set; }
        public static bool LandArtEnabled { get; private set; }
        public static float LandArtOpacity { get; private set; }
        public static bool OceanArtEnabled { get; private set; }
        public static float OceanArtOpacity { get; private set; }
        public static bool ArtCloudsEnabled { get; private set; }
        public static float ArtCloudsOpacity { get; private set; }
        public static bool WeatherCloudsEnabled { get; private set; }
        public static bool WeatherRadarEnabled { get; private set; }
        public static bool SatellitesEnabled { get; private set; }
        public static bool EarthquakesEnabled { get; private set; }
        public static float HeadingFineOffsetDegrees { get; private set; }
        public static bool ViewpointCategoryEnabled { get; private set; }
        public static bool BackgroundCategoryEnabled { get; private set; }
        public static bool DisplayCategoryEnabled { get; private set; }
        public static bool EarthStylesCategoryEnabled { get; private set; }
        public static bool WeatherCategoryEnabled { get; private set; }
        public static bool LiveDataCategoryEnabled { get; private set; }
        public static bool OrientCategoryEnabled { get; private set; }
        public static bool PrivacyCategoryEnabled { get; private set; }

        public static bool EffectiveViewpointOverrideEnabled
        {
            get
            {
                Load();
                return ViewpointCategoryEnabled && ViewpointOverrideEnabled;
            }
        }

        public static bool EffectiveShowSetNorthButton
        {
            get
            {
                Load();
                return DisplayCategoryEnabled && OrientCategoryEnabled && ShowSetNorthButton;
            }
        }

        public static bool EffectiveHideUserCoordinates
        {
            get
            {
                Load();
                return PrivacyCategoryEnabled && HideUserCoordinates;
            }
        }

        public static bool EffectiveHideFarSideCoordinates
        {
            get
            {
                Load();
                return PrivacyCategoryEnabled && HideFarSideCoordinates;
            }
        }

        public static bool EffectiveHideLocationAccuracy
        {
            get
            {
                Load();
                return PrivacyCategoryEnabled && HideLocationAccuracy;
            }
        }

        public static bool EffectiveHideViewedRegion
        {
            get
            {
                Load();
                return PrivacyCategoryEnabled && HideViewedRegion;
            }
        }

        public static bool EffectiveShowViewedFromName
        {
            get
            {
                Load();
                return !PrivacyCategoryEnabled || ShowViewedFromName;
            }
        }

        public static GeoCoordinate ViewpointCoordinate
        {
            get { return new GeoCoordinate(ViewpointLatitude, ViewpointLongitude); }
        }

        public static string ViewedFromLabel
        {
            get
            {
                Load();
                if (!EffectiveViewpointOverrideEnabled)
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

            CameraFeedEnabled = ReadBool(CameraFeedKey, false);
            MainHudVisible = ReadBool(MainHudKey, true);
            CountryBannerVisible = ReadBool(CountryBannerKey, true);
            CountryOutlineColor = ReadColor(CountryOutlineColorKey, new Color(0.25f, 1f, 0.4f, 1f));
            GridColor = ReadColor(GridColorKey, new Color(0.15f, 0.88f, 1f, 0.95f));
            GridVisible = ReadBool(GridVisibleKey, true);
            CountryOutlineThickness = Mathf.Clamp(PlayerPrefs.GetFloat(CountryOutlineThicknessKey, 0.25f), 0.25f, 3f);
            GridThickness = Mathf.Clamp(PlayerPrefs.GetFloat(GridThicknessKey, 0.25f), 0.25f, 3f);
            ShowSetNorthButton = ReadBool(ShowSetNorthButtonKey, true);
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
            NightLightsEnabled = false;
            RimGlowEnabled = false;
            WaterArtEnabled = ReadBool(WaterArtKey, true);
            WaterArtOpacity = Mathf.Clamp01(PlayerPrefs.GetFloat(WaterArtOpacityKey, 0.35f));
            LandArtEnabled = ReadBool(LandArtKey, false);
            LandArtOpacity = Mathf.Clamp01(PlayerPrefs.GetFloat(LandArtOpacityKey, 0.65f));
            OceanArtEnabled = false;
            OceanArtOpacity = Mathf.Clamp01(PlayerPrefs.GetFloat(OceanArtOpacityKey, 0.85f));
            ArtCloudsEnabled = false;
            ArtCloudsOpacity = Mathf.Clamp01(PlayerPrefs.GetFloat(ArtCloudsOpacityKey, 0.8f));
            WeatherCloudsEnabled = ReadBool(WeatherCloudsKey, false);
            WeatherRadarEnabled = ReadBool(WeatherRadarKey, false);
            SatellitesEnabled = ReadBool(SatellitesKey, true);
            EarthquakesEnabled = ReadBool(EarthquakesKey, false);
            HeadingFineOffsetDegrees = ReadHeadingFineOffset();
            ViewpointCategoryEnabled = ReadBool(ViewpointCategoryKey, true);
            BackgroundCategoryEnabled = ReadBool(BackgroundCategoryKey, true);
            DisplayCategoryEnabled = ReadBool(DisplayCategoryKey, true);
            EarthStylesCategoryEnabled = ReadBool(EarthStylesCategoryKey, true);
            WeatherCategoryEnabled = ReadBool(WeatherCategoryKey, true);
            LiveDataCategoryEnabled = ReadBool(LiveDataCategoryKey, true);
            OrientCategoryEnabled = ReadBool(OrientCategoryKey, true);
            PrivacyCategoryEnabled = ReadBool(PrivacyCategoryKey, true);
            loaded = true;
        }

        public static void SetCameraFeedEnabled(bool value)
        {
            Load();
            CameraFeedEnabled = value;
            WriteBool(CameraFeedKey, value);
        }

        public static void SetMainHudVisible(bool value)
        {
            Load();
            MainHudVisible = value;
            WriteBool(MainHudKey, value);
        }

        public static void SetCountryBannerVisible(bool value)
        {
            Load();
            CountryBannerVisible = value;
            WriteBool(CountryBannerKey, value);
        }

        public static void SetCountryOutlineColor(Color value)
        {
            Load();
            CountryOutlineColor = value;
            WriteColor(CountryOutlineColorKey, value);
        }

        public static void SetGridColor(Color value)
        {
            Load();
            GridColor = value;
            WriteColor(GridColorKey, value);
        }

        public static void SetGridVisible(bool value)
        {
            Load();
            GridVisible = value;
            WriteBool(GridVisibleKey, value);
        }

        public static void SetCountryOutlineThickness(float value)
        {
            Load();
            CountryOutlineThickness = Mathf.Clamp(value, 0.25f, 3f);
            WriteFloat(CountryOutlineThicknessKey, CountryOutlineThickness);
        }

        public static void SetGridThickness(float value)
        {
            Load();
            GridThickness = Mathf.Clamp(value, 0.25f, 3f);
            WriteFloat(GridThicknessKey, GridThickness);
        }

        public static void SetShowSetNorthButton(bool value)
        {
            Load();
            ShowSetNorthButton = value;
            WriteBool(ShowSetNorthButtonKey, value);
        }

        public static void SetHideUserCoordinates(bool value)
        {
            Load();
            HideUserCoordinates = value;
            WriteBool(HideUserCoordinatesKey, value);
        }

        public static void SetHideFarSideCoordinates(bool value)
        {
            Load();
            HideFarSideCoordinates = value;
            WriteBool(HideFarSideCoordinatesKey, value);
        }

        public static void SetHideLocationAccuracy(bool value)
        {
            Load();
            HideLocationAccuracy = value;
            WriteBool(HideLocationAccuracyKey, value);
        }

        public static void SetHideViewedRegion(bool value)
        {
            Load();
            HideViewedRegion = value;
            WriteBool(HideViewedRegionKey, value);
        }

        public static void SetShowViewedFromName(bool value)
        {
            Load();
            ShowViewedFromName = value;
            WriteBool(ShowViewedFromNameKey, value);
        }

        public static void SetPrivacyMode(bool value)
        {
            Load();
            HideUserCoordinates = value;
            HideFarSideCoordinates = value;
            HideLocationAccuracy = value;
            HideViewedRegion = value;
            ShowViewedFromName = !value;
            PlayerPrefs.SetInt(HideUserCoordinatesKey, value ? 1 : 0);
            PlayerPrefs.SetInt(HideFarSideCoordinatesKey, value ? 1 : 0);
            PlayerPrefs.SetInt(HideLocationAccuracyKey, value ? 1 : 0);
            PlayerPrefs.SetInt(HideViewedRegionKey, value ? 1 : 0);
            PlayerPrefs.SetInt(ShowViewedFromNameKey, value ? 0 : 1);
            PlayerPrefs.Save();
        }

        public static void SetViewpoint(GeoCoordinate coordinate, string label)
        {
            Load();
            ViewpointOverrideEnabled = true;
            ViewpointLatitude = coordinate.Latitude;
            ViewpointLongitude = coordinate.Longitude;
            ViewpointLabel = string.IsNullOrWhiteSpace(label) ? "Selected viewpoint" : label.Trim();
            PlayerPrefs.SetInt(ViewpointOverrideKey, 1);
            PlayerPrefs.SetFloat(ViewpointLatitudeKey, ViewpointLatitude);
            PlayerPrefs.SetFloat(ViewpointLongitudeKey, ViewpointLongitude);
            PlayerPrefs.SetString(ViewpointLabelKey, ViewpointLabel);
            PlayerPrefs.Save();
        }

        public static void UseRealLocation()
        {
            Load();
            ViewpointOverrideEnabled = false;
            WriteBool(ViewpointOverrideKey, false);
        }

        public static void SetCountryLabelsVisible(bool value)
        {
            Load();
            CountryLabelsVisible = value;
            WriteBool(CountryLabelsKey, value);
        }

        public static void SetSunEnabled(bool value)
        {
            Load();
            SunEnabled = value;
            WriteBool(SunKey, value);
        }

        public static void SetMoonEnabled(bool value)
        {
            Load();
            MoonEnabled = value;
            WriteBool(MoonKey, value);
        }

        public static void SetMilkyWayEnabled(bool value)
        {
            Load();
            MilkyWayEnabled = value;
            WriteBool(MilkyWayKey, value);
        }

        public static void SetWaterArtEnabled(bool value)
        {
            Load();
            WaterArtEnabled = value;
            WriteBool(WaterArtKey, value);
        }

        public static void SetWaterArtOpacity(float value)
        {
            Load();
            WaterArtOpacity = Mathf.Clamp01(value);
            WriteFloat(WaterArtOpacityKey, WaterArtOpacity);
        }

        public static void SetLandArtEnabled(bool value)
        {
            Load();
            LandArtEnabled = value;
            WriteBool(LandArtKey, value);
        }

        public static void SetLandArtOpacity(float value)
        {
            Load();
            LandArtOpacity = Mathf.Clamp01(value);
            WriteFloat(LandArtOpacityKey, LandArtOpacity);
        }

        public static void SetOceanArtOpacity(float value)
        {
            Load();
            OceanArtOpacity = Mathf.Clamp(value, 0.05f, 1f);
            WriteFloat(OceanArtOpacityKey, OceanArtOpacity);
        }

        public static void SetArtCloudsOpacity(float value)
        {
            Load();
            ArtCloudsOpacity = Mathf.Clamp(value, 0.05f, 1f);
            WriteFloat(ArtCloudsOpacityKey, ArtCloudsOpacity);
        }

        public static void SetWeatherCloudsEnabled(bool value)
        {
            Load();
            WeatherCloudsEnabled = value;
            WriteBool(WeatherCloudsKey, value);
        }

        public static void SetWeatherRadarEnabled(bool value)
        {
            Load();
            WeatherRadarEnabled = value;
            WriteBool(WeatherRadarKey, value);
        }

        public static void SetSatellitesEnabled(bool value)
        {
            Load();
            SatellitesEnabled = value;
            WriteBool(SatellitesKey, value);
        }

        public static void SetEarthquakesEnabled(bool value)
        {
            Load();
            EarthquakesEnabled = value;
            WriteBool(EarthquakesKey, value);
        }

        public static void SetHeadingFineOffset(float offsetDegrees)
        {
            Load();
            if (float.IsNaN(offsetDegrees) || float.IsInfinity(offsetDegrees))
            {
                return;
            }

            HeadingFineOffsetDegrees = Mathf.Repeat(offsetDegrees + 180f, 360f) - 180f;
            WriteFloat(HeadingFineOffsetKey, HeadingFineOffsetDegrees);
        }

        public static void SetViewpointCategoryEnabled(bool value)
        {
            Load();
            ViewpointCategoryEnabled = value;
            WriteBool(ViewpointCategoryKey, value);
        }

        public static void SetBackgroundCategoryEnabled(bool value)
        {
            Load();
            BackgroundCategoryEnabled = value;
            WriteBool(BackgroundCategoryKey, value);
        }

        public static void SetDisplayCategoryEnabled(bool value)
        {
            Load();
            DisplayCategoryEnabled = value;
            WriteBool(DisplayCategoryKey, value);
        }

        public static void SetEarthStylesCategoryEnabled(bool value)
        {
            Load();
            EarthStylesCategoryEnabled = value;
            WriteBool(EarthStylesCategoryKey, value);
        }

        public static void SetWeatherCategoryEnabled(bool value)
        {
            Load();
            WeatherCategoryEnabled = value;
            WriteBool(WeatherCategoryKey, value);
        }

        public static void SetLiveDataCategoryEnabled(bool value)
        {
            Load();
            LiveDataCategoryEnabled = value;
            WriteBool(LiveDataCategoryKey, value);
        }

        public static void SetOrientCategoryEnabled(bool value)
        {
            Load();
            OrientCategoryEnabled = value;
            WriteBool(OrientCategoryKey, value);
        }

        public static void SetPrivacyCategoryEnabled(bool value)
        {
            Load();
            PrivacyCategoryEnabled = value;
            WriteBool(PrivacyCategoryKey, value);
        }

        private static float ReadHeadingFineOffset()
        {
            if (PlayerPrefs.HasKey(HeadingFineOffsetKey))
            {
                return PlayerPrefs.GetFloat(HeadingFineOffsetKey, 0f);
            }

            // V3 mixed persistent fine adjustments with a north lock tied to a
            // different sensor frame. Preserve only offsets that were not saved
            // as a manual north calibration.
            return ReadBool(LegacyManualHeadingKey, false)
                ? 0f
                : PlayerPrefs.GetFloat(LegacyHeadingOffsetKey, 0f);
        }

        private static bool ReadBool(string key, bool defaultValue)
        {
            return PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) != 0;
        }

        private static Color ReadColor(string key, Color defaultValue)
        {
            Color value;
            return ColorUtility.TryParseHtmlString(PlayerPrefs.GetString(key, string.Empty), out value)
                ? value
                : defaultValue;
        }

        private static void WriteBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void WriteFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
        }

        private static void WriteColor(string key, Color value)
        {
            PlayerPrefs.SetString(key, "#" + ColorUtility.ToHtmlStringRGBA(value));
            PlayerPrefs.Save();
        }
    }
}
