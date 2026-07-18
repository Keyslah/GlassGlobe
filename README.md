# GlassGlobe

Hold your phone up and look **through the Earth**.

Star-map apps point up at the sky and show you constellations that are really there.
GlassGlobe points the other way: it treats the planet as if it were crystal-clear glass
and shows you the coastlines and country borders on the far side — aligned with
physical reality. If a straight metal rod ran from your eye through the phone and
onward through the planet, the border it hits on screen is the border it would
actually hit.

## How it works

- Your **GPS position** places a virtual camera on a globe; the phone's **fused
  attitude sensor** (gravity + gyroscope + magnetometer) orients it, with a slow
  filter aligning yaw to compass true north and manual nudge buttons for on-the-spot
  calibration.
- A ray from the camera through the screen center exits the far side of the globe;
  everything you see is the inside of the far hemisphere — concave, and
  mirror-flipped, because that is genuinely what a glass Earth would show you.
- The globe's coordinate embedding is deliberately mirrored (negated x in
  `EarthMath`) so Unity's left-handed cameras produce that physically truthful
  view. An east-pointing, 45°-down ray exits at 90°E, as in reality. Don't "fix" it.
- The rear camera feed renders behind the overlay (toggle **AR On/Off** in the HUD),
  switching the field of view between the phone lens (~70°) and a through-a-window
  eye FOV (32.4°).
- Borders are Natural Earth 1:110m admin-0 data (177 countries, public domain),
  converted to a compact ring format and drawn as great-circle arcs.

## Building

Unity 6000.0.34f1 with Android build support.

- **GlassGlobe → Build Preview Scene** regenerates the entire scene from code.
- **GlassGlobe → Build Android Preview APK** validates and produces
  `Builds/Android/GlassGlobePreview.apk`.

Or headless:

```
Unity.exe -batchmode -quit -projectPath <repo> -executeMethod GlassGlobeAndroidBuilder.BuildPreviewApk
```

On device you'll be asked for location and camera permissions. In the Unity editor,
the same scene runs as a mouse-drag simulator instead of using sensors.

Expected accuracy with stock sensors is compass-grade: within a few degrees
outdoors, worse near metal or indoors. The **Align** button snaps to the compass;
**±1/±5** nudge heading manually.

## Roadmap

- ARCore Geospatial API for sub-degree heading (the true "metal rod" experience)
- Country name labels and distance readouts
- Dateline-safe spherical point-in-polygon for the country-under-reticle readout
- WGS84 ellipsoid correction (~0.2°)

## Data

Country boundaries: [Natural Earth](https://www.naturalearthdata.com/) 1:110m
Admin 0 — public domain. Thank you, Natural Earth.

## License

MIT — see [LICENSE](LICENSE).
