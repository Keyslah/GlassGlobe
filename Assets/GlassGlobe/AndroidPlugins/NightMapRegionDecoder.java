package com.glassglobe.night;

import android.content.res.AssetManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.BitmapRegionDecoder;
import android.graphics.Rect;
import android.os.Build;

import com.unity3d.player.UnityPlayer;

import java.io.IOException;
import java.io.InputStream;

/**
 * Region-decodes the eight official NASA Black Marble 2016 JPEG tiles without
 * ever expanding a complete 21600x21600 source image. Unity calls this class
 * from attached worker threads and receives a fixed-size, bottom-up RGB24 tile.
 */
public final class NightMapRegionDecoder {
    public static final int SOURCE_TILE_SIZE = 21600;
    public static final int SOURCE_COLUMNS = 4;
    public static final int SOURCE_ROWS = 2;
    public static final int GLOBAL_WIDTH = SOURCE_TILE_SIZE * SOURCE_COLUMNS;
    public static final int GLOBAL_HEIGHT = SOURCE_TILE_SIZE * SOURCE_ROWS;

    public static final int LOD_COUNT = 3;
    public static final int OUTPUT_INTERIOR_SIZE = 1080;
    public static final int OUTPUT_GUTTER_SIZE = 1;
    public static final int OUTPUT_SIZE =
            OUTPUT_INTERIOR_SIZE + OUTPUT_GUTTER_SIZE * 2;
    public static final int HEADER_SIZE = 16;

    private static final int PAYLOAD_VERSION = 1;
    private static final int PAYLOAD_CHANNELS = 3;
    private static final int PAYLOAD_FLAG_BOTTOM_UP = 1;
    private static final int PAYLOAD_FLAG_SRGB = 2;

    private static final String[] SOURCE_FILES = {
            "BlackMarble_2016_A1.resource",
            "BlackMarble_2016_B1.resource",
            "BlackMarble_2016_C1.resource",
            "BlackMarble_2016_D1.resource",
            "BlackMarble_2016_A2.resource",
            "BlackMarble_2016_B2.resource",
            "BlackMarble_2016_C2.resource",
            "BlackMarble_2016_D2.resource"
    };

    // Unity normally places a nested StreamingAssets folder below
    // assets/bin/Data/StreamingAssets. The second root also supports Gradle or
    // local packaging that exposes the StreamingAssets contents at asset root.
    private static final String[] ASSET_ROOTS = {
            "bin/Data/StreamingAssets/GlassGlobeNightFullRes/",
            "GlassGlobeNightFullRes/"
    };

    private static final DecoderSlot[] DECODER_SLOTS = createDecoderSlots();

    private NightMapRegionDecoder() {
    }

    /**
     * Returns widths and heights in A1, B1, C1, D1, A2, B2, C2, D2 order.
     * Opening a missing, corrupt, or incorrectly sized source throws.
     */
    public static int[] probeSourceDimensions() {
        int[] dimensions = new int[DECODER_SLOTS.length * 2];
        try {
            for (int index = 0; index < DECODER_SLOTS.length; index++) {
                DecoderSlot slot = DECODER_SLOTS[index];
                synchronized (slot.lock) {
                    BitmapRegionDecoder decoder = getDecoderLocked(slot);
                    dimensions[index * 2] = decoder.getWidth();
                    dimensions[index * 2 + 1] = decoder.getHeight();
                }
            }
            return dimensions;
        } catch (Exception exception) {
            throw new IllegalStateException(
                    "NightMapRegionDecoder: full-resolution source probe failed.",
                    exception);
        }
    }

    /**
     * Decodes one tile from the requested global LOD grid. LOD 0 is 20x10
     * with a 4x source sample, LOD 1 is 40x20 with a 2x sample, and LOD 2 is
     * 80x40 at native source resolution. Every result has a one-pixel gutter.
     */
    public static byte[] decodeTile(int lod, int tileX, int tileY) {
        try {
            validateTileCoordinates(lod, tileX, tileY);
            return decodeTileInternal(lod, tileX, tileY);
        } catch (Exception exception) {
            throw new IllegalStateException(
                    "NightMapRegionDecoder: failed to decode lod=" + lod +
                            " x=" + tileX + " y=" + tileY + ".",
                    exception);
        }
    }

    /** Closes cached streams and native decoder state after all callers stop. */
    public static void shutdown() {
        for (DecoderSlot slot : DECODER_SLOTS) {
            synchronized (slot.lock) {
                if (slot.decoder != null) {
                    slot.decoder.recycle();
                    slot.decoder = null;
                }

                if (slot.stream != null) {
                    try {
                        slot.stream.close();
                    } catch (IOException ignored) {
                        // The process is releasing this cache; no recovery work
                        // remains for a close failure.
                    }
                    slot.stream = null;
                }
            }
        }
    }

    private static byte[] decodeTileInternal(int lod, int tileX, int tileY)
            throws IOException {
        int sampleSize = getSampleSize(lod);
        int sourceInteriorSize = OUTPUT_INTERIOR_SIZE * sampleSize;
        int sourceRequestSize = OUTPUT_SIZE * sampleSize;
        int requestX = tileX * sourceInteriorSize - sampleSize;
        int requestY = tileY * sourceInteriorSize - sampleSize;
        int requestEndY = requestY + sourceRequestSize;

        int validYStart = Math.max(0, requestY);
        int validYEnd = Math.min(GLOBAL_HEIGHT, requestEndY);
        if (validYStart >= validYEnd) {
            throw new IllegalArgumentException("Requested tile does not overlap the source map.");
        }

        int[] argbTopDown = new int[OUTPUT_SIZE * OUTPUT_SIZE];
        int globalY = validYStart;
        while (globalY < validYEnd) {
            int sourceRow = globalY / SOURCE_TILE_SIZE;
            int nextSourceRowBoundary = (sourceRow + 1) * SOURCE_TILE_SIZE;
            int pieceEndY = Math.min(validYEnd, nextSourceRowBoundary);
            int sourcePieceHeight = pieceEndY - globalY;
            requireSampleAlignment(sourcePieceHeight, sampleSize, "height");

            int destinationY = (globalY - requestY) / sampleSize;
            int requestOffsetX = 0;
            while (requestOffsetX < sourceRequestSize) {
                int normalizedGlobalX = floorMod(
                        requestX + requestOffsetX,
                        GLOBAL_WIDTH);
                int sourceColumn = normalizedGlobalX / SOURCE_TILE_SIZE;
                int localX = normalizedGlobalX % SOURCE_TILE_SIZE;
                int remainingRequestWidth = sourceRequestSize - requestOffsetX;
                int sourcePieceWidth = Math.min(
                        remainingRequestWidth,
                        SOURCE_TILE_SIZE - localX);
                requireSampleAlignment(sourcePieceWidth, sampleSize, "width");

                int destinationX = requestOffsetX / sampleSize;
                int localY = globalY % SOURCE_TILE_SIZE;
                int sourceIndex = sourceRow * SOURCE_COLUMNS + sourceColumn;
                decodePieceInto(
                        sourceIndex,
                        localX,
                        localY,
                        sourcePieceWidth,
                        sourcePieceHeight,
                        sampleSize,
                        destinationX,
                        destinationY,
                        argbTopDown);

                requestOffsetX += sourcePieceWidth;
            }

            globalY = pieceEndY;
        }

        fillPoleGutters(argbTopDown, requestY, validYStart, validYEnd, sampleSize);
        return packPayload(argbTopDown);
    }

    private static void decodePieceInto(
            int sourceIndex,
            int sourceX,
            int sourceY,
            int sourceWidth,
            int sourceHeight,
            int sampleSize,
            int destinationX,
            int destinationY,
            int[] destination) throws IOException {
        DecoderSlot slot = DECODER_SLOTS[sourceIndex];
        Bitmap decoded = null;
        synchronized (slot.lock) {
            BitmapRegionDecoder decoder = getDecoderLocked(slot);
            BitmapFactory.Options options = new BitmapFactory.Options();
            options.inSampleSize = sampleSize;
            options.inPreferredConfig = Bitmap.Config.ARGB_8888;
            options.inScaled = false;
            options.inDither = false;
            Rect sourceRect = new Rect(
                    sourceX,
                    sourceY,
                    sourceX + sourceWidth,
                    sourceY + sourceHeight);
            decoded = decoder.decodeRegion(sourceRect, options);
        }

        if (decoded == null) {
            throw new IOException("Android returned no bitmap for source region.");
        }

        try {
            int expectedWidth = sourceWidth / sampleSize;
            int expectedHeight = sourceHeight / sampleSize;
            if (decoded.getWidth() != expectedWidth ||
                    decoded.getHeight() != expectedHeight) {
                throw new IOException(
                        "Unexpected decoded size " + decoded.getWidth() + "x" +
                                decoded.getHeight() + ", expected " + expectedWidth +
                                "x" + expectedHeight + ".");
            }

            decoded.getPixels(
                    destination,
                    destinationY * OUTPUT_SIZE + destinationX,
                    OUTPUT_SIZE,
                    0,
                    0,
                    expectedWidth,
                    expectedHeight);
        } finally {
            decoded.recycle();
        }
    }

    private static void fillPoleGutters(
            int[] pixels,
            int requestY,
            int validYStart,
            int validYEnd,
            int sampleSize) {
        int firstValidRow = (validYStart - requestY) / sampleSize;
        for (int row = 0; row < firstValidRow; row++) {
            System.arraycopy(
                    pixels,
                    firstValidRow * OUTPUT_SIZE,
                    pixels,
                    row * OUTPUT_SIZE,
                    OUTPUT_SIZE);
        }

        int onePastLastValidRow = (validYEnd - requestY) / sampleSize;
        int lastValidRow = onePastLastValidRow - 1;
        for (int row = onePastLastValidRow; row < OUTPUT_SIZE; row++) {
            System.arraycopy(
                    pixels,
                    lastValidRow * OUTPUT_SIZE,
                    pixels,
                    row * OUTPUT_SIZE,
                    OUTPUT_SIZE);
        }
    }

    private static byte[] packPayload(int[] argbTopDown) {
        int pixelBytes = OUTPUT_SIZE * OUTPUT_SIZE * PAYLOAD_CHANNELS;
        byte[] payload = new byte[HEADER_SIZE + pixelBytes];
        payload[0] = 'G';
        payload[1] = 'G';
        payload[2] = 'N';
        payload[3] = 'T';
        putUnsignedShortLittleEndian(payload, 4, PAYLOAD_VERSION);
        putUnsignedShortLittleEndian(payload, 6, OUTPUT_SIZE);
        putUnsignedShortLittleEndian(payload, 8, OUTPUT_SIZE);
        payload[10] = (byte) PAYLOAD_CHANNELS;
        payload[11] = (byte) (PAYLOAD_FLAG_BOTTOM_UP | PAYLOAD_FLAG_SRGB);

        int outputOffset = HEADER_SIZE;
        for (int outputY = 0; outputY < OUTPUT_SIZE; outputY++) {
            int sourceY = OUTPUT_SIZE - 1 - outputY;
            int sourceOffset = sourceY * OUTPUT_SIZE;
            for (int x = 0; x < OUTPUT_SIZE; x++) {
                int pixel = argbTopDown[sourceOffset + x];
                payload[outputOffset++] = (byte) ((pixel >>> 16) & 0xff);
                payload[outputOffset++] = (byte) ((pixel >>> 8) & 0xff);
                payload[outputOffset++] = (byte) (pixel & 0xff);
            }
        }
        return payload;
    }

    private static BitmapRegionDecoder getDecoderLocked(DecoderSlot slot)
            throws IOException {
        if (slot.decoder != null && !slot.decoder.isRecycled()) {
            return slot.decoder;
        }

        AssetManager assetManager = getAssetManager();
        InputStream stream = openSourceAsset(assetManager, slot.fileName);
        BitmapRegionDecoder decoder;
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                decoder = BitmapRegionDecoder.newInstance(stream);
            } else {
                // The shareable flag has been ignored since Android KitKat.
                decoder = BitmapRegionDecoder.newInstance(stream, false);
            }
        } catch (IOException exception) {
            stream.close();
            throw exception;
        }

        if (decoder == null) {
            stream.close();
            throw new IOException("No decoder returned for " + slot.fileName + ".");
        }

        if (decoder.getWidth() != SOURCE_TILE_SIZE ||
                decoder.getHeight() != SOURCE_TILE_SIZE) {
            int width = decoder.getWidth();
            int height = decoder.getHeight();
            decoder.recycle();
            stream.close();
            throw new IOException(
                    slot.fileName + " is " + width + "x" + height +
                            ", expected 21600x21600.");
        }

        slot.stream = stream;
        slot.decoder = decoder;
        return decoder;
    }

    private static AssetManager getAssetManager() throws IOException {
        if (UnityPlayer.currentActivity == null) {
            throw new IOException("UnityPlayer.currentActivity is unavailable.");
        }
        return UnityPlayer.currentActivity.getAssets();
    }

    private static InputStream openSourceAsset(
            AssetManager assetManager,
            String fileName) throws IOException {
        IOException lastFailure = null;
        for (String root : ASSET_ROOTS) {
            String path = root + fileName;
            try {
                return assetManager.open(path, AssetManager.ACCESS_RANDOM);
            } catch (IOException exception) {
                lastFailure = exception;
            }
        }

        throw new IOException(
                "Could not open full-resolution night source " + fileName + ".",
                lastFailure);
    }

    private static void validateTileCoordinates(int lod, int tileX, int tileY) {
        if (lod < 0 || lod >= LOD_COUNT) {
            throw new IllegalArgumentException("LOD must be between 0 and 2.");
        }

        int columns = 20 << lod;
        int rows = 10 << lod;
        if (tileX < 0 || tileX >= columns || tileY < 0 || tileY >= rows) {
            throw new IllegalArgumentException(
                    "Tile is outside the " + columns + "x" + rows +
                            " LOD " + lod + " grid.");
        }
    }

    private static int getSampleSize(int lod) {
        return 4 >> lod;
    }

    private static void requireSampleAlignment(
            int sourceLength,
            int sampleSize,
            String dimensionName) {
        if (sourceLength <= 0 || sourceLength % sampleSize != 0) {
            throw new IllegalStateException(
                    "Source piece " + dimensionName + " " + sourceLength +
                            " is not aligned to sample size " + sampleSize + ".");
        }
    }

    private static int floorMod(int value, int modulus) {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static void putUnsignedShortLittleEndian(
            byte[] destination,
            int offset,
            int value) {
        destination[offset] = (byte) (value & 0xff);
        destination[offset + 1] = (byte) ((value >>> 8) & 0xff);
    }

    private static DecoderSlot[] createDecoderSlots() {
        DecoderSlot[] slots = new DecoderSlot[SOURCE_FILES.length];
        for (int index = 0; index < SOURCE_FILES.length; index++) {
            slots[index] = new DecoderSlot(SOURCE_FILES[index]);
        }
        return slots;
    }

    private static final class DecoderSlot {
        final Object lock = new Object();
        final String fileName;
        InputStream stream;
        BitmapRegionDecoder decoder;

        DecoderSlot(String fileName) {
            this.fileName = fileName;
        }
    }
}
