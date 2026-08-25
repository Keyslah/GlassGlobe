using System;

namespace GlassGlobe
{
    /// <summary>
    /// Resolution levels derived from NASA's 86400x43200 Black Marble map.
    /// The numeric value is the source-pixel sampling step for the level.
    /// Every produced texture is 1080x1080 pixels.
    /// </summary>
    public enum NightMapTileLod
    {
        Sample4 = 4,
        Sample2 = 2,
        Sample1 = 1
    }

    /// <summary>
    /// Stable identity for one night-map texture. Rows run north to south and
    /// columns run west to east.
    /// </summary>
    public struct NightMapTileKey : IEquatable<NightMapTileKey>
    {
        public NightMapTileKey(NightMapTileLod lod, int column, int row)
        {
            Lod = lod;
            Column = column;
            Row = row;
        }

        public NightMapTileLod Lod { get; private set; }
        public int Column { get; private set; }
        public int Row { get; private set; }

        public bool Equals(NightMapTileKey other)
        {
            return Lod == other.Lod && Column == other.Column && Row == other.Row;
        }

        public override bool Equals(object obj)
        {
            return obj is NightMapTileKey && Equals((NightMapTileKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Lod;
                hash = (hash * 397) ^ Column;
                hash = (hash * 397) ^ Row;
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format("{0}/c{1:00}/r{2:00}", Lod, Column, Row);
        }

        public static bool operator ==(NightMapTileKey left, NightMapTileKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(NightMapTileKey left, NightMapTileKey right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Immutable description of one resolution grid.
    /// </summary>
    public struct NightMapLodInfo
    {
        internal NightMapLodInfo(
            NightMapTileLod lod,
            int columns,
            int rows,
            int sourceRegionSizePixels,
            int sampleStep)
        {
            Lod = lod;
            Columns = columns;
            Rows = rows;
            SourceRegionSizePixels = sourceRegionSizePixels;
            SampleStep = sampleStep;
        }

        public NightMapTileLod Lod { get; private set; }
        public int Columns { get; private set; }
        public int Rows { get; private set; }
        public int SourceRegionSizePixels { get; private set; }
        public int SampleStep { get; private set; }

        public int OutputTileSizePixels
        {
            get { return SourceRegionSizePixels / SampleStep; }
        }

        public int CoverageCellsPerTile
        {
            get
            {
                return SourceRegionSizePixels /
                    NightMapTileLayout.CoverageCellSizePixels;
            }
        }

        public int TotalTiles
        {
            get { return Columns * Rows; }
        }
    }

    /// <summary>
    /// Integer rectangle with an exclusive maximum edge. It is used for both
    /// source pixels and coverage cells, as identified by the calling method.
    /// </summary>
    public struct NightMapIntRect : IEquatable<NightMapIntRect>
    {
        public NightMapIntRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; private set; }
        public int Y { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int XMax { get { return X + Width; } }
        public int YMax { get { return Y + Height; } }

        public bool Contains(int x, int y)
        {
            return x >= X && x < XMax && y >= Y && y < YMax;
        }

        public bool Equals(NightMapIntRect other)
        {
            return X == other.X && Y == other.Y &&
                Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object obj)
        {
            return obj is NightMapIntRect && Equals((NightMapIntRect)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ Width;
                hash = (hash * 397) ^ Height;
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format("({0},{1}) {2}x{3}", X, Y, Width, Height);
        }

        public static bool operator ==(NightMapIntRect left, NightMapIntRect right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(NightMapIntRect left, NightMapIntRect right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Geographic bounds for a tile. West/east span -180 through +180 while
    /// south/north span -90 through +90. East and north are the maximum edges.
    /// </summary>
    public struct NightMapGeoBounds
    {
        public NightMapGeoBounds(
            double westLongitude,
            double eastLongitude,
            double southLatitude,
            double northLatitude)
        {
            WestLongitude = westLongitude;
            EastLongitude = eastLongitude;
            SouthLatitude = southLatitude;
            NorthLatitude = northLatitude;
        }

        public double WestLongitude { get; private set; }
        public double EastLongitude { get; private set; }
        public double SouthLatitude { get; private set; }
        public double NorthLatitude { get; private set; }

        public double CenterLongitude
        {
            get { return (WestLongitude + EastLongitude) * 0.5; }
        }

        public double CenterLatitude
        {
            get { return (SouthLatitude + NorthLatitude) * 0.5; }
        }
    }

    /// <summary>
    /// One cell in the finest 80x40 coverage grid. A coverage cell corresponds
    /// to one 1080x1080 region of the full-resolution source.
    /// </summary>
    public struct NightMapCoverageCell : IEquatable<NightMapCoverageCell>
    {
        public NightMapCoverageCell(int column, int row)
        {
            Column = column;
            Row = row;
        }

        public int Column { get; private set; }
        public int Row { get; private set; }

        public bool Equals(NightMapCoverageCell other)
        {
            return Column == other.Column && Row == other.Row;
        }

        public override bool Equals(object obj)
        {
            return obj is NightMapCoverageCell && Equals((NightMapCoverageCell)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Column * 397) ^ Row;
            }
        }

        public override string ToString()
        {
            return string.Format("c{0:00}/r{1:00}", Column, Row);
        }

        public static bool operator ==(
            NightMapCoverageCell left,
            NightMapCoverageCell right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            NightMapCoverageCell left,
            NightMapCoverageCell right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Locates a derived tile inside one official NASA A1-D2 source JPEG.
    /// CropPixels uses the source image's top-left origin.
    /// </summary>
    public struct NightMapNasaSourceCrop
    {
        internal NightMapNasaSourceCrop(
            string sourceId,
            int sourceColumn,
            int sourceRow,
            NightMapIntRect cropPixels,
            int sampleStep,
            int outputWidth,
            int outputHeight)
        {
            SourceId = sourceId;
            SourceColumn = sourceColumn;
            SourceRow = sourceRow;
            CropPixels = cropPixels;
            SampleStep = sampleStep;
            OutputWidth = outputWidth;
            OutputHeight = outputHeight;
        }

        public string SourceId { get; private set; }
        public int SourceColumn { get; private set; }
        public int SourceRow { get; private set; }
        public NightMapIntRect CropPixels { get; private set; }
        public int SampleStep { get; private set; }
        public int OutputWidth { get; private set; }
        public int OutputHeight { get; private set; }
    }

    /// <summary>
    /// Pure layout contract for NASA's 2016 Black Marble 500 m color map.
    /// Source raster coordinates use a top-left origin. Tile rows therefore run
    /// north to south; tile columns run west to east and wrap at the antimeridian.
    /// </summary>
    public static class NightMapTileLayout
    {
        public const int GlobalWidthPixels = 86400;
        public const int GlobalHeightPixels = 43200;
        public const int PixelsPerDegree = 240;

        public const int NasaSourceColumns = 4;
        public const int NasaSourceRows = 2;
        public const int NasaSourceTileSizePixels = 21600;

        public const int OutputTileSizePixels = 1080;
        public const int CoverageCellSizePixels = 1080;
        public const int CoverageColumns = 80;
        public const int CoverageRows = 40;
        public const int LodCount = 3;

        private static readonly NightMapLodInfo Sample4Info =
            new NightMapLodInfo(NightMapTileLod.Sample4, 20, 10, 4320, 4);
        private static readonly NightMapLodInfo Sample2Info =
            new NightMapLodInfo(NightMapTileLod.Sample2, 40, 20, 2160, 2);
        private static readonly NightMapLodInfo Sample1Info =
            new NightMapLodInfo(NightMapTileLod.Sample1, 80, 40, 1080, 1);

        public static NightMapTileLod GetLodByIndex(int index)
        {
            switch (index)
            {
                case 0:
                    return NightMapTileLod.Sample4;
                case 1:
                    return NightMapTileLod.Sample2;
                case 2:
                    return NightMapTileLod.Sample1;
                default:
                    throw new ArgumentOutOfRangeException("index");
            }
        }

        public static NightMapLodInfo GetLodInfo(NightMapTileLod lod)
        {
            NightMapLodInfo info;
            if (!TryGetLodInfo(lod, out info))
            {
                throw new ArgumentOutOfRangeException("lod");
            }

            return info;
        }

        public static bool TryGetLodInfo(
            NightMapTileLod lod,
            out NightMapLodInfo info)
        {
            switch (lod)
            {
                case NightMapTileLod.Sample4:
                    info = Sample4Info;
                    return true;
                case NightMapTileLod.Sample2:
                    info = Sample2Info;
                    return true;
                case NightMapTileLod.Sample1:
                    info = Sample1Info;
                    return true;
                default:
                    info = default(NightMapLodInfo);
                    return false;
            }
        }

        public static bool IsValidKey(NightMapTileKey key)
        {
            NightMapLodInfo info;
            return TryGetLodInfo(key.Lod, out info) &&
                key.Column >= 0 && key.Column < info.Columns &&
                key.Row >= 0 && key.Row < info.Rows;
        }

        public static bool ValidateKey(NightMapTileKey key, out string error)
        {
            NightMapLodInfo info;
            if (!TryGetLodInfo(key.Lod, out info))
            {
                error = "Unsupported night-map LOD: " + key.Lod + ".";
                return false;
            }

            if (key.Column < 0 || key.Column >= info.Columns)
            {
                error = "Night-map tile column is outside [0," +
                    (info.Columns - 1) + "]: " + key.Column + ".";
                return false;
            }

            if (key.Row < 0 || key.Row >= info.Rows)
            {
                error = "Night-map tile row is outside [0," +
                    (info.Rows - 1) + "]: " + key.Row + ".";
                return false;
            }

            error = null;
            return true;
        }

        public static bool IsValidCoverageCell(NightMapCoverageCell cell)
        {
            return cell.Column >= 0 && cell.Column < CoverageColumns &&
                cell.Row >= 0 && cell.Row < CoverageRows;
        }

        public static NightMapIntRect GetGlobalPixelBounds(NightMapTileKey key)
        {
            RequireValidKey(key);
            NightMapLodInfo info = GetLodInfo(key.Lod);
            return new NightMapIntRect(
                key.Column * info.SourceRegionSizePixels,
                key.Row * info.SourceRegionSizePixels,
                info.SourceRegionSizePixels,
                info.SourceRegionSizePixels);
        }

        public static NightMapGeoBounds GetGeographicBounds(NightMapTileKey key)
        {
            NightMapIntRect pixels = GetGlobalPixelBounds(key);
            double west = -180.0 + pixels.X / (double)PixelsPerDegree;
            double east = -180.0 + pixels.XMax / (double)PixelsPerDegree;
            double north = 90.0 - pixels.Y / (double)PixelsPerDegree;
            double south = 90.0 - pixels.YMax / (double)PixelsPerDegree;
            return new NightMapGeoBounds(west, east, south, north);
        }

        public static NightMapTileKey GetTileForCoordinate(
            NightMapTileLod lod,
            double latitude,
            double longitude)
        {
            NightMapTileKey key;
            if (!TryGetTileForCoordinate(lod, latitude, longitude, out key))
            {
                throw new ArgumentOutOfRangeException(
                    "latitude",
                    "LOD must be supported and latitude/longitude must be finite.");
            }

            return key;
        }

        public static bool TryGetTileForCoordinate(
            NightMapTileLod lod,
            double latitude,
            double longitude,
            out NightMapTileKey key)
        {
            NightMapLodInfo info;
            if (!TryGetLodInfo(lod, out info) ||
                !IsFinite(latitude) ||
                !IsFinite(longitude))
            {
                key = default(NightMapTileKey);
                return false;
            }

            double safeLatitude = ClampLatitude(latitude);
            double safeLongitude = NormalizeLongitude(longitude);
            double horizontal = (safeLongitude + 180.0) / 360.0;
            double vertical = (90.0 - safeLatitude) / 180.0;

            int column = ClampIndex(
                (int)Math.Floor(horizontal * info.Columns),
                info.Columns);
            int row = ClampIndex(
                (int)Math.Floor(vertical * info.Rows),
                info.Rows);

            key = new NightMapTileKey(lod, column, row);
            return true;
        }

        public static NightMapTileKey GetTileForGlobalPixel(
            NightMapTileLod lod,
            int globalPixelX,
            int globalPixelY)
        {
            NightMapLodInfo info = GetLodInfo(lod);
            if (globalPixelX < 0 || globalPixelX >= GlobalWidthPixels)
            {
                throw new ArgumentOutOfRangeException("globalPixelX");
            }

            if (globalPixelY < 0 || globalPixelY >= GlobalHeightPixels)
            {
                throw new ArgumentOutOfRangeException("globalPixelY");
            }

            return new NightMapTileKey(
                lod,
                globalPixelX / info.SourceRegionSizePixels,
                globalPixelY / info.SourceRegionSizePixels);
        }

        public static NightMapCoverageCell GetCoverageCell(
            double latitude,
            double longitude)
        {
            NightMapTileKey finest = GetTileForCoordinate(
                NightMapTileLod.Sample1,
                latitude,
                longitude);
            return new NightMapCoverageCell(finest.Column, finest.Row);
        }

        public static NightMapTileKey GetTileForCoverageCell(
            NightMapTileLod lod,
            NightMapCoverageCell cell)
        {
            if (!IsValidCoverageCell(cell))
            {
                throw new ArgumentOutOfRangeException("cell");
            }

            NightMapLodInfo info = GetLodInfo(lod);
            int cellsPerTile = info.CoverageCellsPerTile;
            return new NightMapTileKey(
                lod,
                cell.Column / cellsPerTile,
                cell.Row / cellsPerTile);
        }

        public static NightMapIntRect GetCoverageCellBounds(NightMapTileKey key)
        {
            RequireValidKey(key);
            NightMapLodInfo info = GetLodInfo(key.Lod);
            int cellsPerTile = info.CoverageCellsPerTile;
            return new NightMapIntRect(
                key.Column * cellsPerTile,
                key.Row * cellsPerTile,
                cellsPerTile,
                cellsPerTile);
        }

        public static NightMapNasaSourceCrop GetNasaSourceCrop(
            NightMapTileKey key)
        {
            NightMapNasaSourceCrop crop;
            if (!TryGetNasaSourceCrop(key, out crop))
            {
                throw new ArgumentOutOfRangeException(
                    "key",
                    "The key is invalid or its region crosses a NASA source tile.");
            }

            return crop;
        }

        public static bool TryGetNasaSourceCrop(
            NightMapTileKey key,
            out NightMapNasaSourceCrop crop)
        {
            if (!IsValidKey(key))
            {
                crop = default(NightMapNasaSourceCrop);
                return false;
            }

            NightMapLodInfo info = GetLodInfo(key.Lod);
            NightMapIntRect global = GetGlobalPixelBounds(key);
            int sourceColumn = global.X / NasaSourceTileSizePixels;
            int sourceRow = global.Y / NasaSourceTileSizePixels;
            int finalSourceColumn = (global.XMax - 1) / NasaSourceTileSizePixels;
            int finalSourceRow = (global.YMax - 1) / NasaSourceTileSizePixels;

            if (sourceColumn != finalSourceColumn ||
                sourceRow != finalSourceRow ||
                sourceColumn < 0 || sourceColumn >= NasaSourceColumns ||
                sourceRow < 0 || sourceRow >= NasaSourceRows)
            {
                crop = default(NightMapNasaSourceCrop);
                return false;
            }

            char columnName = (char)('A' + sourceColumn);
            char rowName = (char)('1' + sourceRow);
            string sourceId = new string(new[] { columnName, rowName });
            NightMapIntRect local = new NightMapIntRect(
                global.X % NasaSourceTileSizePixels,
                global.Y % NasaSourceTileSizePixels,
                global.Width,
                global.Height);

            crop = new NightMapNasaSourceCrop(
                sourceId,
                sourceColumn,
                sourceRow,
                local,
                info.SampleStep,
                local.Width / info.SampleStep,
                local.Height / info.SampleStep);
            return true;
        }

        /// <summary>
        /// Returns a longitude in [-180, 180). Both -180 and +180 therefore
        /// select the first column, making the antimeridian unambiguous.
        /// </summary>
        public static double NormalizeLongitude(double longitude)
        {
            if (!IsFinite(longitude))
            {
                throw new ArgumentOutOfRangeException("longitude");
            }

            double normalized = (longitude + 180.0) % 360.0;
            if (normalized < 0.0)
            {
                normalized += 360.0;
            }

            return normalized - 180.0;
        }

        public static double ClampLatitude(double latitude)
        {
            if (!IsFinite(latitude))
            {
                throw new ArgumentOutOfRangeException("latitude");
            }

            return Math.Max(-90.0, Math.Min(90.0, latitude));
        }

        /// <summary>
        /// Exhaustively verifies all grid, source-crop, coverage, pole, and
        /// antimeridian invariants without touching Unity state or asset files.
        /// </summary>
        public static bool ValidateContract(out string error)
        {
            // Copy constants into runtime locals so player builds execute these
            // guardrails without reporting the failure branches as unreachable.
            int globalWidth = GlobalWidthPixels;
            int globalHeight = GlobalHeightPixels;
            if (globalWidth != NasaSourceColumns * NasaSourceTileSizePixels ||
                globalHeight != NasaSourceRows * NasaSourceTileSizePixels)
            {
                error = "NASA source grid does not cover the global raster.";
                return false;
            }

            if (globalWidth != CoverageColumns * CoverageCellSizePixels ||
                globalHeight != CoverageRows * CoverageCellSizePixels)
            {
                error = "Coverage cells do not cover the global raster.";
                return false;
            }

            for (int lodIndex = 0; lodIndex < LodCount; lodIndex++)
            {
                NightMapTileLod lod = GetLodByIndex(lodIndex);
                NightMapLodInfo info = GetLodInfo(lod);
                if (info.Columns * info.SourceRegionSizePixels != GlobalWidthPixels ||
                    info.Rows * info.SourceRegionSizePixels != GlobalHeightPixels)
                {
                    error = lod + " does not cover the global raster.";
                    return false;
                }

                if (info.SourceRegionSizePixels % info.SampleStep != 0 ||
                    info.OutputTileSizePixels != OutputTileSizePixels)
                {
                    error = lod + " does not produce 1080x1080 textures.";
                    return false;
                }

                if (NasaSourceTileSizePixels % info.SourceRegionSizePixels != 0)
                {
                    error = lod + " regions cross NASA source boundaries.";
                    return false;
                }

                if (CoverageColumns / info.Columns != info.CoverageCellsPerTile ||
                    CoverageRows / info.Rows != info.CoverageCellsPerTile ||
                    info.CoverageCellsPerTile != info.SampleStep)
                {
                    error = lod + " has inconsistent coverage-cell scaling.";
                    return false;
                }

                for (int row = 0; row < info.Rows; row++)
                {
                    for (int column = 0; column < info.Columns; column++)
                    {
                        NightMapTileKey key = new NightMapTileKey(lod, column, row);
                        NightMapIntRect global = GetGlobalPixelBounds(key);
                        NightMapIntRect coverage = GetCoverageCellBounds(key);
                        NightMapNasaSourceCrop source;

                        if (global.X < 0 || global.Y < 0 ||
                            global.XMax > GlobalWidthPixels ||
                            global.YMax > GlobalHeightPixels)
                        {
                            error = key + " escapes the global raster.";
                            return false;
                        }

                        if (coverage.X < 0 || coverage.Y < 0 ||
                            coverage.XMax > CoverageColumns ||
                            coverage.YMax > CoverageRows)
                        {
                            error = key + " escapes the coverage grid.";
                            return false;
                        }

                        if (!TryGetNasaSourceCrop(key, out source) ||
                            source.OutputWidth != OutputTileSizePixels ||
                            source.OutputHeight != OutputTileSizePixels)
                        {
                            error = key + " has an invalid NASA source crop.";
                            return false;
                        }
                    }
                }
            }

            NightMapTileKey northWest = GetTileForCoordinate(
                NightMapTileLod.Sample1,
                90.0,
                -180.0);
            NightMapTileKey wrappedNorthWest = GetTileForCoordinate(
                NightMapTileLod.Sample1,
                90.0,
                180.0);
            NightMapTileKey southEast = GetTileForCoordinate(
                NightMapTileLod.Sample1,
                -90.0,
                179.999999);

            if (northWest != new NightMapTileKey(NightMapTileLod.Sample1, 0, 0) ||
                wrappedNorthWest != northWest ||
                southEast != new NightMapTileKey(NightMapTileLod.Sample1, 79, 39))
            {
                error = "Pole or antimeridian coordinate mapping is inconsistent.";
                return false;
            }

            error = null;
            return true;
        }

        private static void RequireValidKey(NightMapTileKey key)
        {
            string error;
            if (!ValidateKey(key, out error))
            {
                throw new ArgumentOutOfRangeException("key", error);
            }
        }

        private static int ClampIndex(int value, int count)
        {
            return Math.Max(0, Math.Min(count - 1, value));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
