using System;
using System.Globalization;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Which map the glass globe surface wears. Blue Moon is the original
    /// tinted glass shell; Blue Marble layers a NASA daylight map over it.
    /// An enum rather than two flags so the selection can never be both or
    /// neither.
    /// </summary>
    public enum GlobeSurfaceMode
    {
        BlueMoon,
        BlueMarble
    }

    /// <summary>
    /// Which NASA Blue Marble monthly composite the surface uses.
    /// </summary>
    public enum BlueMarbleSeason
    {
        Spring,
        Summer,
        Fall,
        Winter
    }

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

        /// <summary>
        /// The one thickness country outlines use when they are switched on.
        /// </summary>
        public const float DefaultCountryOutlineThickness = 0.1f;

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
        private const string GlobeSurfaceModeKey = Prefix + "GlobeSurfaceMode";
        private const string BlueMarbleSeasonKey = Prefix + "BlueMarbleSeason";
        private const string BlueMarbleOpacityKey = Prefix + "BlueMarbleOpacity";
        private const string SeasonButtonKey = Prefix + "SeasonButton";
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
        private const string HeadingCalibrationActiveKey =
            Prefix + "HeadingCalibrationActiveV1";
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

        /// <summary>
        /// Saved settings live under their own prefix so a reset can wipe the
        /// live block without touching them.
        /// </summary>
        private const string SnapshotPrefix = Prefix + "Saved.";
        private const string SnapshotStampKey = SnapshotPrefix + "Stamp";
        private const string SavedSetCountKey = SnapshotPrefix + "Count";

        private static bool legacyMigrationChecked;

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
        public static GlobeSurfaceMode GlobeSurface { get; private set; }
        public static BlueMarbleSeason BlueMarbleSeasonChoice { get; private set; }
        public static float BlueMarbleOpacity { get; private set; }
        public static bool SeasonButtonVisible { get; private set; }
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
        public static bool HeadingCalibrationActive { get; private set; }
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

        public static bool EffectiveBlueMarbleEnabled
        {
            get
            {
                Load();
                return DisplayCategoryEnabled && GlobeSurface == GlobeSurfaceMode.BlueMarble;
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
                // The in-app privacy controls were retired. Legacy preferences
                // must not leave readouts hidden with no UI available to restore
                // them.
                return false;
            }
        }

        public static bool EffectiveHideFarSideCoordinates
        {
            get
            {
                return false;
            }
        }

        public static bool EffectiveHideLocationAccuracy
        {
            get
            {
                return false;
            }
        }

        public static bool EffectiveHideViewedRegion
        {
            get
            {
                return false;
            }
        }

        public static bool EffectiveShowViewedFromName
        {
            get
            {
                return true;
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

        /// <summary>
        /// How a key is stored, so reset/save/load can move values around
        /// without a hand-written line per setting.
        /// </summary>
        private enum PrefKind
        {
            Number,
            Decimal,
            Text
        }

        private struct PrefEntry
        {
            public readonly string Key;
            public readonly PrefKind Kind;

            /// <summary>
            /// False for values tied to where you physically are rather than
            /// how the app looks: the compass calibration and the viewpoint.
            /// Reset still clears them, but Save/Load leave them alone so a set
            /// saved at home can be loaded anywhere without dragging the old
            /// location and heading along with it.
            /// </summary>
            public readonly bool InSnapshot;

            public PrefEntry(string key, PrefKind kind)
                : this(key, kind, true)
            {
            }

            public PrefEntry(string key, PrefKind kind, bool inSnapshot)
            {
                Key = key;
                Kind = kind;
                InSnapshot = inSnapshot;
            }
        }

        /// <summary>
        /// Every key this class owns, with its storage type. PlayerPrefs cannot
        /// enumerate or delete by prefix, so reset, save, and load all work off
        /// this table; add new settings here too.
        /// </summary>
        private static readonly PrefEntry[] AllEntries =
        {
            new PrefEntry(CameraFeedKey, PrefKind.Number),
            new PrefEntry(MainHudKey, PrefKind.Number),
            new PrefEntry(CountryBannerKey, PrefKind.Number),
            new PrefEntry(CountryOutlineColorKey, PrefKind.Text),
            new PrefEntry(GridColorKey, PrefKind.Text),
            new PrefEntry(GridVisibleKey, PrefKind.Number),
            new PrefEntry(CountryOutlineThicknessKey, PrefKind.Decimal),
            new PrefEntry(GridThicknessKey, PrefKind.Decimal),
            new PrefEntry(ShowSetNorthButtonKey, PrefKind.Number),
            new PrefEntry(HideUserCoordinatesKey, PrefKind.Number),
            new PrefEntry(HideFarSideCoordinatesKey, PrefKind.Number),
            new PrefEntry(HideLocationAccuracyKey, PrefKind.Number),
            new PrefEntry(HideViewedRegionKey, PrefKind.Number),
            new PrefEntry(ShowViewedFromNameKey, PrefKind.Number),
            new PrefEntry(ViewpointOverrideKey, PrefKind.Number, false),
            new PrefEntry(ViewpointLatitudeKey, PrefKind.Decimal, false),
            new PrefEntry(ViewpointLongitudeKey, PrefKind.Decimal, false),
            new PrefEntry(ViewpointLabelKey, PrefKind.Text, false),
            new PrefEntry(CountryLabelsKey, PrefKind.Number),
            new PrefEntry(MilkyWayKey, PrefKind.Number),
            new PrefEntry(SunKey, PrefKind.Number),
            new PrefEntry(MoonKey, PrefKind.Number),
            new PrefEntry(GlobeSurfaceModeKey, PrefKind.Number),
            new PrefEntry(BlueMarbleSeasonKey, PrefKind.Number),
            new PrefEntry(BlueMarbleOpacityKey, PrefKind.Decimal),
            new PrefEntry(SeasonButtonKey, PrefKind.Number),
            new PrefEntry(NightLightsKey, PrefKind.Number),
            new PrefEntry(RimGlowKey, PrefKind.Number),
            new PrefEntry(WaterArtKey, PrefKind.Number),
            new PrefEntry(WaterArtOpacityKey, PrefKind.Decimal),
            new PrefEntry(LandArtKey, PrefKind.Number),
            new PrefEntry(LandArtOpacityKey, PrefKind.Decimal),
            new PrefEntry(OceanArtKey, PrefKind.Number),
            new PrefEntry(OceanArtOpacityKey, PrefKind.Decimal),
            new PrefEntry(ArtCloudsKey, PrefKind.Number),
            new PrefEntry(ArtCloudsOpacityKey, PrefKind.Decimal),
            new PrefEntry(WeatherCloudsKey, PrefKind.Number),
            new PrefEntry(WeatherRadarKey, PrefKind.Number),
            new PrefEntry(SatellitesKey, PrefKind.Number),
            new PrefEntry(EarthquakesKey, PrefKind.Number),
            new PrefEntry(HeadingFineOffsetKey, PrefKind.Decimal, false),
            new PrefEntry(HeadingCalibrationActiveKey, PrefKind.Number, false),
            new PrefEntry(LegacyHeadingOffsetKey, PrefKind.Decimal, false),
            new PrefEntry(LegacyManualHeadingKey, PrefKind.Number, false),
            new PrefEntry(ViewpointCategoryKey, PrefKind.Number),
            new PrefEntry(BackgroundCategoryKey, PrefKind.Number),
            new PrefEntry(DisplayCategoryKey, PrefKind.Number),
            new PrefEntry(EarthStylesCategoryKey, PrefKind.Number),
            new PrefEntry(WeatherCategoryKey, PrefKind.Number),
            new PrefEntry(LiveDataCategoryKey, PrefKind.Number),
            new PrefEntry(OrientCategoryKey, PrefKind.Number),
            new PrefEntry(PrivacyCategoryKey, PrefKind.Number)
        };

        /// <summary>
        /// How many named settings sets can be kept.
        /// </summary>
        public const int MaxSavedSets = 8;

        public static bool HasSavedSettings
        {
            get { return SavedSetCount > 0; }
        }

        public static int SavedSetCount
        {
            get
            {
                MigrateLegacySavedSet();
                return Mathf.Clamp(PlayerPrefs.GetInt(SavedSetCountKey, 0), 0, MaxSavedSets);
            }
        }

        public static string GetSavedSetName(int slot)
        {
            if (slot < 0 || slot >= SavedSetCount)
            {
                return string.Empty;
            }

            return PlayerPrefs.GetString(SlotNameKey(slot), "Set " + (slot + 1));
        }

        /// <summary>
        /// Copies the whole live settings block into a named set, replacing any
        /// set already using that name. Settings left untouched have no key at
        /// all, and that absence is copied too, so restoring reproduces the
        /// state exactly rather than freezing today's defaults in place.
        /// Returns false only when the name is empty or there is no room left.
        /// </summary>
        public static bool SaveSettingsAs(string name)
        {
            Load();
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            int slot = FindSavedSetByName(trimmed);
            int count = SavedSetCount;
            if (slot < 0)
            {
                if (count >= MaxSavedSets)
                {
                    return false;
                }

                slot = count;
                PlayerPrefs.SetInt(SavedSetCountKey, count + 1);
            }

            for (int index = 0; index < AllEntries.Length; index++)
            {
                PrefEntry entry = AllEntries[index];
                if (!entry.InSnapshot)
                {
                    continue;
                }

                CopyPref(entry.Key, SlotKey(slot, entry.Key), entry.Kind);
            }

            PlayerPrefs.SetString(SlotNameKey(slot), trimmed);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>
        /// Restores a named set. The caller is responsible for pushing the
        /// restored values back onto the scene.
        /// </summary>
        public static bool LoadSavedSet(int slot)
        {
            if (slot < 0 || slot >= SavedSetCount)
            {
                return false;
            }

            for (int index = 0; index < AllEntries.Length; index++)
            {
                PrefEntry entry = AllEntries[index];
                if (!entry.InSnapshot)
                {
                    continue;
                }

                CopyPref(SlotKey(slot, entry.Key), entry.Key, entry.Kind);
            }

            PlayerPrefs.Save();
            loaded = false;
            Load();
            return true;
        }

        private static int FindSavedSetByName(string name)
        {
            int count = SavedSetCount;
            for (int slot = 0; slot < count; slot++)
            {
                if (string.Equals(GetSavedSetName(slot), name, StringComparison.OrdinalIgnoreCase))
                {
                    return slot;
                }
            }

            return -1;
        }

        private static string SlotKey(int slot, string key)
        {
            return SnapshotPrefix + slot + "." + key;
        }

        private static string SlotNameKey(int slot)
        {
            return SnapshotPrefix + slot + ".Name";
        }

        /// <summary>
        /// The first version of this feature kept a single unnamed set. Fold it
        /// into slot 0 rather than silently dropping whatever was saved with it.
        /// </summary>
        private static void MigrateLegacySavedSet()
        {
            if (legacyMigrationChecked)
            {
                return;
            }

            legacyMigrationChecked = true;
            if (!PlayerPrefs.HasKey(SnapshotStampKey) ||
                PlayerPrefs.GetInt(SavedSetCountKey, 0) > 0)
            {
                return;
            }

            for (int index = 0; index < AllEntries.Length; index++)
            {
                PrefEntry entry = AllEntries[index];
                if (!entry.InSnapshot)
                {
                    continue;
                }

                CopyPref(SnapshotPrefix + entry.Key, SlotKey(0, entry.Key), entry.Kind);
                PlayerPrefs.DeleteKey(SnapshotPrefix + entry.Key);
            }

            PlayerPrefs.SetString(SlotNameKey(0), "Saved settings");
            PlayerPrefs.SetInt(SavedSetCountKey, 1);
            PlayerPrefs.DeleteKey(SnapshotStampKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Clears every live setting and reloads the shipping defaults. The
        /// saved slot is deliberately left alone, so a reset can be undone with
        /// Load Settings. The caller is responsible for pushing the reloaded
        /// values back onto the scene.
        /// </summary>
        public static void ResetToDefaults()
        {
            for (int index = 0; index < AllEntries.Length; index++)
            {
                PlayerPrefs.DeleteKey(AllEntries[index].Key);
            }

            PlayerPrefs.Save();
            loaded = false;
            Load();
        }

        private static void CopyPref(string fromKey, string toKey, PrefKind kind)
        {
            if (!PlayerPrefs.HasKey(fromKey))
            {
                // "Never set" is itself part of the state worth reproducing.
                PlayerPrefs.DeleteKey(toKey);
                return;
            }

            switch (kind)
            {
                case PrefKind.Decimal:
                    PlayerPrefs.SetFloat(toKey, PlayerPrefs.GetFloat(fromKey));
                    break;
                case PrefKind.Text:
                    PlayerPrefs.SetString(toKey, PlayerPrefs.GetString(fromKey));
                    break;
                default:
                    PlayerPrefs.SetInt(toKey, PlayerPrefs.GetInt(fromKey));
                    break;
            }
        }

        public static void Load()
        {
            if (loaded)
            {
                return;
            }

            CameraFeedEnabled = ReadBool(CameraFeedKey, false);
            MainHudVisible = ReadBool(MainHudKey, false);
            CountryBannerVisible = ReadBool(CountryBannerKey, true);
            CountryOutlineColor = ReadColor(CountryOutlineColorKey, new Color(0.25f, 1f, 0.4f, 1f));
            GridColor = ReadColor(GridColorKey, new Color(0.15f, 0.88f, 1f, 0.95f));
            GridVisible = ReadBool(GridVisibleKey, false);
            CountryOutlineThickness = Mathf.Clamp(
                PlayerPrefs.GetFloat(CountryOutlineThicknessKey, 0f), 0f, 3f);
            GridThickness = Mathf.Clamp(PlayerPrefs.GetFloat(GridThicknessKey, 0.25f), 0.25f, 3f);
            ShowSetNorthButton = ReadBool(ShowSetNorthButtonKey, false);
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
            GlobeSurface = (GlobeSurfaceMode)Mathf.Clamp(
                PlayerPrefs.GetInt(GlobeSurfaceModeKey, (int)GlobeSurfaceMode.BlueMarble),
                (int)GlobeSurfaceMode.BlueMoon,
                (int)GlobeSurfaceMode.BlueMarble);
            BlueMarbleSeasonChoice = (BlueMarbleSeason)Mathf.Clamp(
                PlayerPrefs.GetInt(BlueMarbleSeasonKey, (int)BlueMarbleSeason.Summer),
                (int)BlueMarbleSeason.Spring,
                (int)BlueMarbleSeason.Winter);
            BlueMarbleOpacity = Mathf.Clamp01(PlayerPrefs.GetFloat(BlueMarbleOpacityKey, 1f));
            SeasonButtonVisible = ReadBool(SeasonButtonKey, true);
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
            HeadingCalibrationActive = ReadBool(
                HeadingCalibrationActiveKey,
                Mathf.Abs(HeadingFineOffsetDegrees) > 0.001f);
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
            // Snap to whole percent so repeated 10% steps cannot drift on
            // floating point (0.1 + 0.1 + 0.1 = 0.30000004).
            CountryOutlineThickness = Mathf.Round(Mathf.Clamp(value, 0f, 3f) * 100f) / 100f;
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

        public static void SetGlobeSurface(GlobeSurfaceMode value)
        {
            Load();
            GlobeSurface = value;
            WriteInt(GlobeSurfaceModeKey, (int)value);
        }

        public static void SetBlueMarbleSeason(BlueMarbleSeason value)
        {
            Load();
            BlueMarbleSeasonChoice = value;
            WriteInt(BlueMarbleSeasonKey, (int)value);
        }

        public static void SetSeasonButtonVisible(bool value)
        {
            Load();
            SeasonButtonVisible = value;
            WriteBool(SeasonButtonKey, value);
        }

        /// <summary>
        /// Country outlines are either on at 10% or off entirely; the old
        /// free-running thickness is kept as the storage so existing saves keep
        /// working.
        /// </summary>
        public static bool CountryLinesVisible
        {
            get
            {
                Load();
                return CountryOutlineThickness > 0f;
            }
        }

        public static void SetCountryLinesVisible(bool value)
        {
            SetCountryOutlineThickness(value ? DefaultCountryOutlineThickness : 0f);
        }

        public static void SetBlueMarbleOpacity(float value)
        {
            Load();
            BlueMarbleOpacity = Mathf.Clamp01(value);
            WriteFloat(BlueMarbleOpacityKey, BlueMarbleOpacity);
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

        public static void SetHeadingCalibrationActive(bool value)
        {
            Load();
            HeadingCalibrationActive = value;
            WriteBool(HeadingCalibrationActiveKey, value);
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

        private static void WriteInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
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
