# GlassGlobe

Hold your phone up and look **through the Earth**.

Star-map apps point at the sky and show you the constellations that are really
there. GlassGlobe points the other way: it treats the planet as clear glass and
draws the coastlines and borders on the far side, in the direction you are
actually pointing. Aim at the ground and you see what is under your feet, all the
way through.

## Features

- **Far-side geography** — coastlines and borders of the 177 countries in Natural
  Earth 1:110m, drawn as great-circle arcs, with a readout naming whatever the
  center dot is on.
- **Blue Marble surface** — NASA satellite imagery on the globe, in four seasons,
  with adjustable transparency and a season button on the viewport. Only the
  selected season is loaded into runtime texture memory. Summer uses NASA's
  official July 21600×10800 source, imported at 16384×8192.
- **Earth at Night surface** — NASA Black Marble 2016 imagery, independently
  toggleable over Blue Moon or Blue Marble with adjustable transparency. On
  Android, the complete 500m source map is region-decoded from eight tiles while
  the global 3600×1800 map remains available as a fallback.
- **Melish 1817 surface** — John Melish's hand-colored historical world map,
  reprojected from Mercator to an 8192×4096 globe texture and loaded on demand
  after Earth at Night in the viewport surface cycle.
- **Sky** — the Milky Way, Sun, and Moon at their true positions for your location
  and the current time, with the Moon showing its real phase.
- **Live data** — satellite clouds and rain radar, the ISS and Tiangong, and
  earthquakes from the last 24 hours.
- **Viewpoints** — look from your GPS position, from any of 33,000 cities, from a
  country, or from wherever the center dot is pointing.
- **Saved settings** — name and reload as many configurations as you like.

## How it works

- Your **GPS position** places a virtual camera on a globe. Android's fused,
  earth-referenced rotation vector continuously owns the view and keeps yaw tied
  to north. **Set North** applies a fixed on-the-spot correction without depending
  on camera tracking. The optional AR session is camera-only: tracking loss or
  relocalization cannot freeze, drag, or snap the globe's orientation.
- A ray from the camera through the screen center exits the far side of the
  globe. Everything you see is the inside of the far hemisphere: concave and
  mirror-flipped, because that is what a glass Earth would actually show you.
- The coordinate embedding is deliberately mirrored (negated x in `EarthMath`) so
  Unity's left-handed cameras produce that physically truthful view. An
  east-pointing, 45°-down ray exits at 90°E, as in reality. **Don't "fix" it.**
- `GlobeRenderer` builds its sphere with the unmirrored `+sin(lon)`, so its
  winding is the opposite of every shell mesh and the globe shader needs
  `Cull Back` where the shells use `Cull Front`. Getting this wrong draws the
  hemisphere under your feet instead of the one you are looking at.
- The optional rear camera feed renders behind the overlay, switching the field of
  view between the phone lens (~70°) and a through-a-window eye FOV (32.4°).
  Its ARCore session and camera streams stop when the feed is off.

Accuracy with stock sensors is compass-grade: within a few degrees outdoors,
worse near metal or indoors.

## Building

Unity 6000.3.20f1 with Android build support.

- **GlassGlobe → Build Preview Scene** regenerates the whole scene from code.
- **GlassGlobe → Build Android Preview APK** validates and produces
  `Builds/Android/GlassGlobePreview.apk`.
- **GlassGlobe → Build Google Play App Bundle** produces a signed AAB. It reads
  `GLASSGLOBE_KEYSTORE_PATH`, `GLASSGLOBE_KEYSTORE_PASSWORD`,
  `GLASSGLOBE_KEY_ALIAS`, and `GLASSGLOBE_KEY_ALIAS_PASSWORD`.

Or headless:

```
Unity.exe -batchmode -quit -projectPath <repo> -executeMethod GlassGlobeAndroidBuilder.BuildPreviewApk
```

On device you will be asked for location and camera permissions. In the editor
the same scene runs as a mouse-drag simulator instead of using sensors.

If a bundle build fails with `Cannot move asset ... Assets/XR/Temp/
XRSimulationRuntimeSettings.asset: Destination path name does already exist`, an
earlier build was interrupted and orphaned that file. Delete the contents of
`Assets/XR/Temp/` and build again.

## Roadmap

- ARCore Geospatial API for sub-degree heading
- Country name labels and distance readouts
- WGS84 ellipsoid correction (~0.2°)

## Data

**Country boundaries:** [Natural Earth](https://www.naturalearthdata.com/) 1:110m
Admin 0 — public domain. Note that overseas departments are filed under their
parent state, so French Guiana reads as France.

**Cities:** [GeoNames](https://www.geonames.org/) `cities15000` — CC BY 4.0.
32,968 cities of 15,000 people or more, reduced to name, country, and position
and ordered by population. Where a name repeats within one country only the
largest is kept, since this drives a viewpoint picker rather than a gazetteer.

**Globe surface:** [NASA Earth Observatory Blue Marble Next
Generation](https://visibleearth.nasa.gov/collection/1484/blue-marble), Reto
Stöckli, NASA Goddard Space Flight Center — public domain. The four seasons are
`world.topo.bathy` monthly composites. Winter, spring, and fall are downsampled
from 5400×2700 to 4096×2048. Summer uses the official July 21600×10800 source,
imported at 16384×8192:

| Season | Source composite | Image record |
| --- | --- | --- |
| Winter | `world.topo.bathy.200401.3x5400x2700.jpg` | [73580](https://visibleearth.nasa.gov/images/73580/) |
| Spring | `world.topo.bathy.200404.3x5400x2700.jpg` | [73655](https://visibleearth.nasa.gov/images/73655/) |
| Summer | `world.topo.bathy.200407.3x21600x10800.jpg` | [73751](https://visibleearth.nasa.gov/images/73751/) |
| Fall | `world.topo.bathy.200410.3x5400x2700.jpg` | [73826](https://visibleearth.nasa.gov/images/73826/) |

**Earth at Night:** [NASA Earth Observatory Black Marble 2016 color map](https://science.nasa.gov/earth/earth-observatory/earth-at-night/maps/),
using the global 0.1-degree `BlackMarble_2016_01deg.jpg` composite (3600×1800)
as a fallback plus the complete 500m source map. The full-resolution map is a
4×2 grid of eight official `BlackMarble_2016_A1.jpg` through
`BlackMarble_2016_D2.jpg` source JPEGs, each 21600×21600. Their exact JPEG bytes
are stored in `Assets/StreamingAssets/GlassGlobeNightFullRes/` with
`.resource` names so Unity does not import the oversized sources as textures.
Android region-decodes the required source areas at runtime instead. NASA Earth
Observatory images by Joshua Stevens, using Suomi NPP VIIRS data from Miguel
Román, NASA Goddard Space Flight Center. `GlassGlobeEarthAtNightChecks` verifies
the NASA source files before a release build.

**Melish 1817 historical surface:** John Melish, Samuel Harrison, Hugh Bridport,
and George Murray, [*The world on Mercator's
projection*](https://www.loc.gov/item/78692118/), Philadelphia, 1817, Library of
Congress Geography and Map Division, item 78692118, digital ID
`g3200.ct007148` — public domain. The runtime texture was derived from the
[14918×11417 scan on Wikimedia Commons](https://commons.wikimedia.org/wiki/File:The_world_on_Mercator%27s_projection,_LOC_78692118.jpg)
(source JPEG SHA-256
`68139d41b7f84d39f5b07744aa27b363976a8aad9b4605a0c54a5088c5c58eae`).
The conversion used visible longitude controls at −180°, −90°, 0°, 90°, and
180° across the 60°N, equator, and 60°S graticules, with the geographic field
bounded near 79°N. In source pixels, those five control points were
`(348,2684) (3926,2694) (7485,2704) (11026,2714) (14549,2724)` at 60°N,
`(338,5698) (3913,5700) (7485,5703) (11021,5705) (14568,5708)` at the equator,
and `(325,8672) (3915,8675) (7487,8678) (11033,8680) (14581,8683)` at 60°S.
The full printed graticule's corner bounds were approximately `(354,375)`,
`(14536,418)`, `(14569,11005)`, and `(319,10982)`. Longitude remains in
standard west-to-east orientation; the Mercator vertical coordinate was
resampled to equirectangular latitude. The
title, tables, cartouche, ornaments, frame, and scanner margins were excluded.
The source becomes unreliable below about 60°S, so 58–60°S and 77.5–79°N fade
into sampled parchment and the unmapped polar caps use compatible parchment
fill instead of stretched map rows. Only the resulting 8192×4096 runtime JPEG is
shipped in `Resources`; its SHA-256 is
`c34a82d233b192bf0ccff21654e609ccbbe64790dca5250d2843937f08b208fe`.

**Milky Way panorama:** [ESO/S. Brunier](https://www.eso.org/public/images/eso0932a/)
(eso0932a), CC BY 4.0. The panorama is authored in galactic coordinates; the app
rotates it by your position and the current sidereal time so the galaxy sits
where it really is.

**Weather and live data:** cloud imagery from NOAA GOES and Himawari via NASA
GIBS plus EUMETSAT Meteosat; radar from RainViewer; orbits from CelesTrak;
earthquakes from the USGS Earthquake Hazards Program.

## License

MIT — see [LICENSE](LICENSE).
