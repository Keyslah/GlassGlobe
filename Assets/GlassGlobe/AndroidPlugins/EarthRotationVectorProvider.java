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
 * Supplies Android's earth-referenced fused rotation vector to Unity through
 * one coherent, screen-aligned matrix snapshot per poll. Every snapshot carries
 * the Android sensor timestamp so managed code can reject cached/stale poses.
 */
public final class EarthRotationVectorProvider implements SensorEventListener {
    private static final int SAMPLE_PERIOD_MICROSECONDS = 16667;
    private static EarthRotationVectorProvider instance;

    private final Object sampleLock = new Object();
    private final SensorManager sensorManager;
    private final Sensor earthRotationSensor;
    private final float[] latestRotationVector = new float[4];

    private WeakReference<Activity> activityReference;
    private HandlerThread sensorThread;
    private boolean listening;
    private boolean hasSample;
    private long latestTimestampNanos;
    private float latestHeadingAccuracyRadians = -1.0f;

    private EarthRotationVectorProvider(Activity activity) {
        Context context = activity.getApplicationContext();
        sensorManager = (SensorManager) context.getSystemService(Context.SENSOR_SERVICE);
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
        if (earthRotationSensor == null || sensorManager == null) {
            return false;
        }

        if (listening) {
            return true;
        }

        synchronized (sampleLock) {
            hasSample = false;
            latestTimestampNanos = 0L;
            latestHeadingAccuracyRadians = -1.0f;
        }

        HandlerThread newThread = new HandlerThread("GlassGlobeEarthRotation");
        newThread.start();
        Handler handler = new Handler(newThread.getLooper());
        boolean registered;
        try {
            registered = sensorManager.registerListener(
                this,
                earthRotationSensor,
                SAMPLE_PERIOD_MICROSECONDS,
                handler);
        } catch (RuntimeException exception) {
            registered = false;
        }

        if (!registered) {
            newThread.quitSafely();
            return false;
        }

        sensorThread = newThread;
        listening = true;
        return true;
    }

    private synchronized void stopInternal() {
        if (sensorManager != null) {
            sensorManager.unregisterListener(this);
        }

        listening = false;
        synchronized (sampleLock) {
            hasSample = false;
            latestTimestampNanos = 0L;
            latestHeadingAccuracyRadians = -1.0f;
        }

        if (sensorThread != null) {
            sensorThread.quitSafely();
            sensorThread = null;
        }
    }

    @Override
    public void onSensorChanged(SensorEvent event) {
        if (event.sensor.getType() != Sensor.TYPE_ROTATION_VECTOR || event.values.length < 3) {
            return;
        }

        float x = event.values[0];
        float y = event.values[1];
        float z = event.values[2];
        float w = event.values.length >= 4
            ? event.values[3]
            : (float) Math.sqrt(Math.max(0.0f, 1.0f - x * x - y * y - z * z));

        synchronized (sampleLock) {
            latestRotationVector[0] = x;
            latestRotationVector[1] = y;
            latestRotationVector[2] = z;
            latestRotationVector[3] = w;
            latestTimestampNanos = event.timestamp;
            latestHeadingAccuracyRadians = event.values.length >= 5
                ? event.values[4]
                : -1.0f;
            hasSample = true;
        }
    }

    @Override
    public void onAccuracyChanged(Sensor sensor, int accuracy) {
        // Per-sample heading accuracy is copied from values[4] when available.
    }

    private float[] snapshotInternal() {
        float[] rotationVector = new float[4];
        long timestampNanos;
        float headingAccuracyRadians;
        synchronized (sampleLock) {
            if (!hasSample) {
                return null;
            }

            System.arraycopy(latestRotationVector, 0, rotationVector, 0, rotationVector.length);
            timestampNanos = latestTimestampNanos;
            headingAccuracyRadians = latestHeadingAccuracyRadians;
        }

        float[] naturalMatrix = new float[9];
        SensorManager.getRotationMatrixFromVector(naturalMatrix, rotationVector);

        int displayRotation = currentDisplayRotation();
        float[] displayMatrix = naturalMatrix;
        if (displayRotation != Surface.ROTATION_0) {
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
            if (!SensorManager.remapCoordinateSystem(naturalMatrix, axisX, axisY, remapped)) {
                return null;
            }

            displayMatrix = remapped;
        }

        long ageNanos = SystemClock.elapsedRealtimeNanos() - timestampNanos;
        float[] result = new float[12];
        System.arraycopy(displayMatrix, 0, result, 0, displayMatrix.length);
        result[9] = Math.max(0L, ageNanos) * 0.000000001f;
        result[10] = displayRotation;
        result[11] = headingAccuracyRadians;
        return result;
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
