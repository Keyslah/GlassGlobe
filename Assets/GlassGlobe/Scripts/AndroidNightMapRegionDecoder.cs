using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Worker-thread JNI bridge for Android's full-resolution Black Marble
    /// region decoder. This type never creates or mutates Unity textures; the
    /// caller uploads a validated payload on the Unity main thread.
    /// </summary>
    public static class AndroidNightMapRegionDecoder
    {
        public const int SourceTileSize = 21600;
        public const int SourceColumns = 4;
        public const int SourceRows = 2;
        public const int SourceWidth = SourceTileSize * SourceColumns;
        public const int SourceHeight = SourceTileSize * SourceRows;
        public const int LodCount = 3;
        public const int OutputInteriorSize = 1080;
        public const int OutputGutterSize = 1;
        public const int OutputSize = OutputInteriorSize + OutputGutterSize * 2;
        public const int PayloadHeaderSize = 16;
        public const int PayloadChannelCount = 3;

        private const string JavaClassName =
            "com.glassglobe.night.NightMapRegionDecoder";
        private const int PayloadVersion = 1;
        private const byte PayloadFlagBottomUp = 1;
        private const byte PayloadFlagSrgb = 2;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static readonly SemaphoreSlim WorkerSlots =
            new SemaphoreSlim(2, 2);
        private static readonly object ShutdownLock = new object();
        private static Task shutdownTask = Task.CompletedTask;
#endif

        public readonly struct DecodedTile
        {
            internal DecodedTile(
                int lod,
                int tileX,
                int tileY,
                int width,
                int height,
                byte[] payload)
            {
                Lod = lod;
                TileX = tileX;
                TileY = tileY;
                Width = width;
                Height = height;
                Payload = payload;
            }

            public int Lod { get; }
            public int TileX { get; }
            public int TileY { get; }
            public int Width { get; }
            public int Height { get; }

            /// <summary>
            /// GGNT header followed by bottom-up RGB24 pixels. Pass this array
            /// to Texture2D.SetPixelData with PixelDataOffset on the main thread.
            /// </summary>
            public byte[] Payload { get; }

            public int PixelDataOffset
            {
                get { return PayloadHeaderSize; }
            }

            public int PixelDataLength
            {
                get { return Width * Height * PayloadChannelCount; }
            }
        }

        public static bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public static int GetColumnCount(int lod)
        {
            ValidateLod(lod);
            return 20 << lod;
        }

        public static int GetRowCount(int lod)
        {
            ValidateLod(lod);
            return 10 << lod;
        }

        public static int GetSourceRegionSize(int lod)
        {
            ValidateLod(lod);
            return OutputInteriorSize * GetSampleSize(lod);
        }

        public static int GetSampleSize(int lod)
        {
            ValidateLod(lod);
            return 4 >> lod;
        }

        public static int NormalizeTileX(int lod, int tileX)
        {
            int columns = GetColumnCount(lod);
            int normalized = tileX % columns;
            return normalized < 0 ? normalized + columns : normalized;
        }

        public static Task<DecodedTile> DecodeTileAsync(
            int lod,
            int tileX,
            int tileY,
            CancellationToken cancellationToken)
        {
            ValidateTileCoordinates(lod, tileX, tileY);
#if UNITY_ANDROID && !UNITY_EDITOR
            return DecodeTileAndroidAsync(
                lod,
                tileX,
                tileY,
                cancellationToken);
#else
            return Task.FromException<DecodedTile>(
                new PlatformNotSupportedException(
                    "Full-resolution Earth at Night region decoding is Android-only."));
#endif
        }

        public static Task<int[]> ProbeSourceDimensionsAsync(
            CancellationToken cancellationToken)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return ProbeSourceDimensionsAndroidAsync(cancellationToken);
#else
            return Task.FromException<int[]>(
                new PlatformNotSupportedException(
                    "Full-resolution Earth at Night source probing is Android-only."));
#endif
        }

        /// <summary>
        /// Waits for both decoder slots, then closes Java's cached streams and
        /// native decoders. Call only after preventing new decode requests.
        /// </summary>
        public static Task ShutdownAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (ShutdownLock)
            {
                // Every NightTileSurface shares the same Java decoder cache and
                // worker semaphore. Coalesce concurrent scene/app teardown so
                // two callers cannot each hold one slot while waiting forever
                // for the other.
                if (!shutdownTask.IsCompleted)
                {
                    return shutdownTask;
                }

                shutdownTask = ShutdownAndroidAsync();
                return shutdownTask;
            }
#else
            return Task.CompletedTask;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static async Task<DecodedTile> DecodeTileAndroidAsync(
            int lod,
            int tileX,
            int tileY,
            CancellationToken cancellationToken)
        {
            await WorkerSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(
                    () => DecodeTileOnAttachedWorker(
                        lod,
                        tileX,
                        tileY,
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                WorkerSlots.Release();
            }
        }

        private static DecodedTile DecodeTileOnAttachedWorker(
            int lod,
            int tileX,
            int tileY,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sbyte[] signedPayload = RunOnAttachedWorker(
                javaClass => javaClass.CallStatic<sbyte[]>(
                    "decodeTile",
                    lod,
                    tileX,
                    tileY));
            cancellationToken.ThrowIfCancellationRequested();

            byte[] payload = null;
            if (signedPayload != null)
            {
                payload = new byte[signedPayload.Length];
                Buffer.BlockCopy(
                    signedPayload,
                    0,
                    payload,
                    0,
                    signedPayload.Length);
            }

            int width;
            int height;
            ValidatePayload(payload, out width, out height);
            return new DecodedTile(
                lod,
                tileX,
                tileY,
                width,
                height,
                payload);
        }

        private static async Task<int[]> ProbeSourceDimensionsAndroidAsync(
            CancellationToken cancellationToken)
        {
            await WorkerSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int[] dimensions = RunOnAttachedWorker(
                            javaClass => javaClass.CallStatic<int[]>(
                                "probeSourceDimensions"));
                        ValidateSourceDimensions(dimensions);
                        cancellationToken.ThrowIfCancellationRequested();
                        return dimensions;
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                WorkerSlots.Release();
            }
        }

        private static async Task ShutdownAndroidAsync()
        {
            await WorkerSlots.WaitAsync().ConfigureAwait(false);
            await WorkerSlots.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(
                    () => RunOnAttachedWorker<object>(
                        javaClass =>
                        {
                            javaClass.CallStatic("shutdown");
                            return null;
                        })).ConfigureAwait(false);
            }
            finally
            {
                WorkerSlots.Release(2);
            }
        }

        private static T RunOnAttachedWorker<T>(
            Func<AndroidJavaClass, T> operation)
        {
            bool attached = false;
            try
            {
                AndroidJNI.AttachCurrentThread();
                attached = true;
                using (AndroidJavaClass javaClass =
                    new AndroidJavaClass(JavaClassName))
                {
                    return operation(javaClass);
                }
            }
            finally
            {
                if (attached)
                {
                    AndroidJNI.DetachCurrentThread();
                }
            }
        }
#endif

        private static void ValidatePayload(
            byte[] payload,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;
            if (payload == null || payload.Length < PayloadHeaderSize)
            {
                throw new InvalidOperationException(
                    "Night map decoder returned an empty or truncated payload.");
            }

            if (payload[0] != (byte)'G' ||
                payload[1] != (byte)'G' ||
                payload[2] != (byte)'N' ||
                payload[3] != (byte)'T')
            {
                throw new InvalidOperationException(
                    "Night map decoder returned the wrong payload signature.");
            }

            int version = ReadUnsignedShortLittleEndian(payload, 4);
            width = ReadUnsignedShortLittleEndian(payload, 6);
            height = ReadUnsignedShortLittleEndian(payload, 8);
            int channels = payload[10];
            byte flags = payload[11];
            if (version != PayloadVersion ||
                width != OutputSize ||
                height != OutputSize ||
                channels != PayloadChannelCount ||
                (flags & (PayloadFlagBottomUp | PayloadFlagSrgb)) !=
                    (PayloadFlagBottomUp | PayloadFlagSrgb))
            {
                throw new InvalidOperationException(
                    "Night map decoder returned an incompatible GGNT payload.");
            }

            int expectedLength = PayloadHeaderSize +
                width * height * PayloadChannelCount;
            if (payload.Length != expectedLength)
            {
                throw new InvalidOperationException(
                    "Night map payload has " + payload.Length +
                    " bytes; expected " + expectedLength + ".");
            }
        }

        private static void ValidateSourceDimensions(int[] dimensions)
        {
            int expectedLength = SourceColumns * SourceRows * 2;
            if (dimensions == null || dimensions.Length != expectedLength)
            {
                throw new InvalidOperationException(
                    "Night map source probe returned the wrong number of dimensions.");
            }

            for (int index = 0; index < dimensions.Length; index += 2)
            {
                if (dimensions[index] != SourceTileSize ||
                    dimensions[index + 1] != SourceTileSize)
                {
                    throw new InvalidOperationException(
                        "Night map source " + (index / 2) + " is " +
                        dimensions[index] + "x" + dimensions[index + 1] +
                        "; expected 21600x21600.");
                }
            }
        }

        private static void ValidateTileCoordinates(
            int lod,
            int tileX,
            int tileY)
        {
            int columns = GetColumnCount(lod);
            int rows = GetRowCount(lod);
            if (tileX < 0 || tileX >= columns ||
                tileY < 0 || tileY >= rows)
            {
                throw new ArgumentOutOfRangeException(
                    "tileX/tileY",
                    "Tile must be inside the " + columns + "x" + rows +
                    " LOD " + lod + " grid.");
            }
        }

        private static void ValidateLod(int lod)
        {
            if (lod < 0 || lod >= LodCount)
            {
                throw new ArgumentOutOfRangeException(
                    "lod",
                    "Night map LOD must be between 0 and 2.");
            }
        }

        private static int ReadUnsignedShortLittleEndian(
            byte[] source,
            int offset)
        {
            return source[offset] | (source[offset + 1] << 8);
        }
    }
}
