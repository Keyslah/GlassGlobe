package com.glassglobe.sensors;

import android.app.Activity;
import android.content.Context;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.view.Display;
import android.view.Surface;

import java.lang.ref.WeakReference;

/**
 * Supplies a magnetometer-free motion pose plus a separately timestamped
 * magnetic-north reference. Unity locks the north mapping after validation and
 * never lets later magnetic changes drive continuous rendered motion.
 */
public final class EarthRotationVectorProvider implements SensorEventListener {
    private static final int SAMPLE_PERIOD_MICROSECONDS = 16667;
    private static EarthRotationVectorProvider instance;

    private final Object sampleLock = new Object();
    private final SensorManager sensorManager;
    private final Sensor gameRotationSensor;
    private final Sensor earthRotationSensor;
    private final float[] latestGameRotationVector = new float[4];
    private final float[] latestEarthRotationVector = new float[4];

    private WeakReference<Activity> activityReference;
    private volatile HandlerThread sensorThread;
    private volatile boolean listening;
    private boolean usingGameRotation;
    private boolean hasGameSample;
    private boolean hasEarthSample;
    private long latestGameTimestampNanos;
    private long latestEarthTimestampNanos;
    private float latestHeadingAccuracyRadians = -1.0f;
    private int providerEpoch;

    private EarthRotationVectorProvider(Activity activity) {
        Context context = activity.getApplicationContext();
        sensorManager = (SensorManager) context.getSystemService(Context.SENSOR_SERVICE);
        gameRotationSensor = sensorManager != null
            ? sensorManager.getDefaultSensor(Sensor.TYPE_GAME_ROTATION_VECTOR)
            : null;
        earthRotationSensor = sensorManager != null
            ? sensorManager.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR)
            : null;
        activityReference = new WeakReference<>(activity);
    }

    public static synchronized boolean start(Activity activity) {
        if (activity == null) {
            return false;
        }

        if (instance == null) {
            instance = new EarthRotationVectorProvider(activity);
        } else {
            instance.activityReference = new WeakReference<>(activity);
        }

        return instance.startInternal();
    }

    public static synchronized void stop() {
        if (instance != null) {
            instance.stopInternal();
        }
    }

    public static float[] snapshot() {
        EarthRotationVectorProvider provider;
        synchronized (EarthRotationVectorProvider.class) {
            provider = instance;
        }

        return provider != null ? provider.snapshotInternal() : null;
    }

    private synchronized boolean startInternal() {
        if (sensorManager == null || (gameRotationSensor == null && earthRotationSensor == null)) {
            return false;
        }

        if (listening) {
            return true;
        }

        synchronized (sampleLock) {
            hasGameSample = false;
            hasEarthSample = false;
            latestGameTimestampNanos = 0L;
            latestEarthTimestampNanos = 0L;
            latestHeadingAccuracyRadians = -1.0f;
        }

        HandlerThread newThread = new HandlerThread("GlassGlobeStableRotation");
        newThread.start();
        Handler handler = new Handler(newThread.getLooper());
        boolean gameRegistered = false;
        boolean motionRegistered = false;
        boolean earthRegistered = false;
        try {
            if (gameRotationSensor != null) {
                gameRegistered = sensorManager.registerListener(
                    this,
                    gameRotationSensor,
                    SAMPLE_PERIOD_MICROSECONDS,
                    handler);
                motionRegistered = gameRegistered;
            }

            if (earthRotationSensor != null) {
                earthRegistered = sensorManager.registerListener(
                    this,
                    earthRotationSensor,
                    SAMPLE_PERIOD_MICROSECONDS,
                    handler);
                if (!motionRegistered) {
                    motionRegistered = earthRegistered;
                }
            }
        } catch (RuntimeException exception) {
            motionRegistered = false;
        }

        // A game-vector frame is arbitrary in yaw, so it is not useful for this
        // app unless the earth vector registered too and can provide the one-time
        // north mapping. Earth-only devices can still use the timestamped
        // absolute fallback.
        if (!motionRegistered || (gameRegistered && !earthRegistered)) {
            sensorManager.unregisterListener(this);
            newThread.quitSafely();
            return false;
        }

        // Publish the active thread before allowing callbacks through. Samples
        // queued while registration was still in progress are intentionally
        // ignored; the next 60 Hz sample will establish the new epoch cleanly.
        sensorThread = newThread;
        synchronized (sampleLock) {
            usingGameRotation = gameRegistered;
            providerEpoch++;
        }
        listening = true;
        return true;
    }

    private synchronized void stopInternal() {
        HandlerThread retiredThread = sensorThread;
        // Retire the callback generation before unregistering or clearing. A
        // callback already queued by quitSafely() must not populate the next
        // provider epoch.
        listening = false;
        sensorThread = null;
        if (sensorManager != null) {
            sensorManager.unregisterListener(this);
        }

        synchronized (sampleLock) {
            usingGameRotation = false;
            hasGameSample = false;
            hasEarthSample = false;
            latestGameTimestampNanos = 0L;
            latestEarthTimestampNanos = 0L;
            latestHeadingAccuracyRadians = -1.0f;
        }

        if (retiredThread != null) {
            retiredThread.quitSafely();
        }
    }

    @Override
    public void onSensorChanged(SensorEvent event) {
        if (!isCallbackFromActiveThread()) {
            return;
        }

        int sensorType = event.sensor.getType();
        if ((sensorType != Sensor.TYPE_GAME_ROTATION_VECTOR &&
                sensorType != Sensor.TYPE_ROTATION_VECTOR) ||
            event.values.length < 3) {
            return;
        }

        float x = event.values[0];
        float y = event.values[1];
        float z = event.values[2];
        float w = event.values.length >= 4
            ? event.values[3]
            : (float) Math.sqrt(Math.max(0.0f, 1.0f - x * x - y * y - z * z));

        synchronized (sampleLock) {
            // stop/start can race a callback that passed the first check while
            // waiting for sampleLock. Recheck so a retired HandlerThread can
            // never write into the current generation.
            if (!isCallbackFromActiveThread()) {
                return;
            }

            if (sensorType == Sensor.TYPE_GAME_ROTATION_VECTOR) {
                latestGameRotationVector[0] = x;
                latestGameRotationVector[1] = y;
                latestGameRotationVector[2] = z;
                latestGameRotationVector[3] = w;
                latestGameTimestampNanos = event.timestamp;
                hasGameSample = true;
            } else {
                latestEarthRotationVector[0] = x;
                latestEarthRotationVector[1] = y;
                latestEarthRotationVector[2] = z;
                latestEarthRotationVector[3] = w;
                latestEarthTimestampNanos = event.timestamp;
                latestHeadingAccuracyRadians = event.values.length >= 5
                    ? event.values[4]
                    : -1.0f;
                hasEarthSample = true;
            }
        }
    }

    private boolean isCallbackFromActiveThread() {
        HandlerThread activeThread = sensorThread;
        return listening &&
            activeThread != null &&
            Thread.currentThread() == activeThread;
    }

    @Override
    public void onAccuracyChanged(Sensor sensor, int accuracy) {
        // Per-sample heading accuracy is copied from values[4] when available.
    }

    private float[] snapshotInternal() {
        float[] motionRotationVector = new float[4];
        float[] earthRotationVector = new float[4];
        long motionTimestampNanos;
        long earthTimestampNanos;
        boolean snapshotUsesGameRotation;
        boolean snapshotHasEarthReference;
        float headingAccuracyRadians;
        int snapshotEpoch;
        synchronized (sampleLock) {
            snapshotUsesGameRotation = usingGameRotation;
            if (snapshotUsesGameRotation && !hasGameSample) {
                return null;
            }

            if (!snapshotUsesGameRotation && !hasEarthSample) {
                return null;
            }

            if (snapshotUsesGameRotation) {
                System.arraycopy(
                    latestGameRotationVector,
                    0,
                    motionRotationVector,
                    0,
                    motionRotationVector.length);
                motionTimestampNanos = latestGameTimestampNanos;
            } else {
                System.arraycopy(
                    latestEarthRotationVector,
                    0,
                    motionRotationVector,
                    0,
                    motionRotationVector.length);
                motionTimestampNanos = latestEarthTimestampNanos;
            }

            snapshotHasEarthReference = hasEarthSample;
            if (snapshotHasEarthReference) {
                System.arraycopy(
                    latestEarthRotationVector,
                    0,
                    earthRotationVector,
                    0,
                    earthRotationVector.length);
            }

            earthTimestampNanos = latestEarthTimestampNanos;
            headingAccuracyRadians = latestHeadingAccuracyRadians;
            snapshotEpoch = providerEpoch;
        }

        int displayRotation = currentDisplayRotation();
        float[] motionMatrix = screenAlignedMatrix(motionRotationVector, displayRotation);
        if (motionMatrix == null) {
            return null;
        }

        float[] earthMatrix = snapshotHasEarthReference
            ? screenAlignedMatrix(earthRotationVector, displayRotation)
            : null;
        long nowNanos = SystemClock.elapsedRealtimeNanos();
        long motionAgeNanos = nowNanos - motionTimestampNanos;
        long earthAgeNanos = nowNanos - earthTimestampNanos;
        float[] result = new float[24];
        System.arraycopy(motionMatrix, 0, result, 0, motionMatrix.length);
        result[9] = Math.max(0L, motionAgeNanos) * 0.000000001f;
        result[10] = displayRotation;
        result[11] = snapshotUsesGameRotation ? 1.0f : 0.0f;
        if (earthMatrix != null) {
            System.arraycopy(earthMatrix, 0, result, 12, earthMatrix.length);
            result[21] = Math.max(0L, earthAgeNanos) * 0.000000001f;
        } else {
            result[21] = -1.0f;
        }
        result[22] = headingAccuracyRadians;
        result[23] = snapshotEpoch;
        return result;
    }

    private static float[] screenAlignedMatrix(float[] rotationVector, int displayRotation) {
        float[] naturalMatrix = new float[9];
        SensorManager.getRotationMatrixFromVector(naturalMatrix, rotationVector);
        if (displayRotation == Surface.ROTATION_0) {
            return naturalMatrix;
        }

        int axisX;
        int axisY;
        switch (displayRotation) {
            case Surface.ROTATION_90:
                axisX = SensorManager.AXIS_Y;
                axisY = SensorManager.AXIS_MINUS_X;
                break;
            case Surface.ROTATION_180:
                axisX = SensorManager.AXIS_MINUS_X;
                axisY = SensorManager.AXIS_MINUS_Y;
                break;
            case Surface.ROTATION_270:
                axisX = SensorManager.AXIS_MINUS_Y;
                axisY = SensorManager.AXIS_X;
                break;
            default:
                axisX = SensorManager.AXIS_X;
                axisY = SensorManager.AXIS_Y;
                break;
        }

        float[] remapped = new float[9];
        return SensorManager.remapCoordinateSystem(
            naturalMatrix,
            axisX,
            axisY,
            remapped)
            ? remapped
            : null;
    }

    @SuppressWarnings("deprecation")
    private int currentDisplayRotation() {
        Activity activity = activityReference != null ? activityReference.get() : null;
        if (activity == null || activity.getWindowManager() == null) {
            return Surface.ROTATION_0;
        }

        Display display = activity.getWindowManager().getDefaultDisplay();
        return display != null ? display.getRotation() : Surface.ROTATION_0;
    }
}
