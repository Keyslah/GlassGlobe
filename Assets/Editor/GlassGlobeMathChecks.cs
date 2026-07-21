using System;
using System.Collections.Generic;
using GlassGlobe;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless invariant checks for the pure math cores (EarthMath, SkyMath,
/// OrbitMath, spherical point-in-polygon). Runs from the GlassGlobe menu or in
/// batch mode via -executeMethod GlassGlobeMathChecks.RunBatch so CI can guard
/// the geometry that is hardest to eyeball on device.
/// </summary>
public static class GlassGlobeMathChecks
{
    [MenuItem("GlassGlobe/Run Math Checks")]
    public static void RunMenu()
    {
        if (!Run())
        {
            throw new Exception("GlassGlobe math checks failed. See Console errors above.");
        }
    }

    public static void RunBatch()
    {
        if (!Run())
        {
            EditorApplication.Exit(1);
        }
    }

    public static bool Run()
    {
        int failures = 0;

        CheckGeoRoundTrips(ref failures);
        CheckLongitudeWrap(ref failures);
        CheckMirrorEmbedding(ref failures);
        CheckLocalFrame(ref failures);
        CheckRaySphere(ref failures);
        CheckSphericalContainment(ref failures);
        CheckSkyMath(ref failures);
        CheckOrbitMath(ref failures);

        if (failures == 0)
        {
            Debug.Log("GlassGlobe math checks passed.");
            return true;
        }

        Debug.LogError("GlassGlobe math checks failed with " + failures + " issue(s).");
        return false;
    }

    private static void CheckGeoRoundTrips(ref int failures)
    {
        float[] latitudes = { -89f, -45f, -10f, 0f, 12.5f, 37.7749f, 60f, 89f };
        float[] longitudes = { -179f, -122.4194f, -60f, 0f, 45f, 116.4074f, 178f };
        foreach (float latitude in latitudes)
        {
            foreach (float longitude in longitudes)
            {
                GeoCoordinate original = new GeoCoordinate(latitude, longitude);
                GeoCoordinate roundTrip = EarthMath.UnitVectorToGeo(EarthMath.GeoToUnitVector(original));
                Check(ref failures,
                    Mathf.Abs(Mathf.DeltaAngle(original.Latitude, roundTrip.Latitude)) < 0.01f &&
                    Mathf.Abs(Mathf.DeltaAngle(original.Longitude, roundTrip.Longitude)) < 0.01f,
                    string.Format("Geo round-trip failed for {0}: got {1}", original, roundTrip));
            }
        }
    }

    private static void CheckLongitudeWrap(ref int failures)
    {
        Check(ref failures, Mathf.Approximately(EarthMath.WrapLongitude(190f), -170f), "WrapLongitude(190) should be -170.");
        Check(ref failures, Mathf.Approximately(EarthMath.WrapLongitude(-190f), 170f), "WrapLongitude(-190) should be 170.");
        Check(ref failures, Mathf.Approximately(EarthMath.WrapLongitude(-180f), 180f), "WrapLongitude(-180) should normalize to 180.");
        Check(ref failures, Mathf.Approximately(EarthMath.WrapLongitude(540f), 180f), "WrapLongitude(540) should be 180.");
        Check(ref failures, Mathf.Approximately(EarthMath.ClampLatitude(120f), 90f), "ClampLatitude(120) should be 90.");
    }

    private static void CheckMirrorEmbedding(ref int failures)
    {
        // The embedding negates x: a point at +90 longitude (east) must land on
        // the -x side so the far side reads true through the left-handed camera.
        Vector3 east = EarthMath.GeoToUnitVector(new GeoCoordinate(0f, 90f));
        Check(ref failures, east.x < -0.99f, "Mirror embedding: +90 lon should map to -x.");
        Vector3 northPole = EarthMath.GeoToUnitVector(new GeoCoordinate(90f, 0f));
        Check(ref failures, northPole.y > 0.99f, "North pole should map to +y.");
        Vector3 primeMeridian = EarthMath.GeoToUnitVector(new GeoCoordinate(0f, 0f));
        Check(ref failures, primeMeridian.z > 0.99f, "Lat0/Lon0 should map to +z.");
    }

    private static void CheckLocalFrame(ref int failures)
    {
        float[] latitudes = { -60f, -20f, 0f, 20f, 51.5f, 80f };
        float[] longitudes = { -150f, -30f, 0f, 77f, 160f };
        foreach (float latitude in latitudes)
        {
            foreach (float longitude in longitudes)
            {
                GeoCoordinate coordinate = new GeoCoordinate(latitude, longitude);
                EarthMath.LocalFrame frame = EarthMath.GetLocalFrame(coordinate);
                Check(ref failures, Mathf.Abs(Vector3.Dot(frame.East, frame.Up)) < 0.001f, "East and Up should be orthogonal at " + coordinate);
                Check(ref failures, Mathf.Abs(Vector3.Dot(frame.North, frame.Up)) < 0.001f, "North and Up should be orthogonal at " + coordinate);
                Check(ref failures, Mathf.Abs(Vector3.Dot(frame.East, frame.North)) < 0.001f, "East and North should be orthogonal at " + coordinate);
                // North should have a positive component toward the pole.
                Vector3 towardPole = EarthMath.GeoToUnitVector(new GeoCoordinate(Mathf.Min(90f, latitude + 1f), longitude)) -
                    EarthMath.GeoToUnitVector(coordinate);
                Check(ref failures, Vector3.Dot(frame.North, towardPole.normalized) > 0.5f, "North should point toward the pole at " + coordinate);
            }
        }
    }

    private static void CheckRaySphere(ref int failures)
    {
        Vector3 center = Vector3.zero;
        float radius = 10f;
        Ray through = new Ray(new Vector3(0f, 0f, -30f), Vector3.forward);
        float near;
        float far;
        Check(ref failures, EarthMath.RaySphereIntersections(through, center, radius, out near, out far), "Ray through center should intersect.");
        Check(ref failures, Mathf.Abs(near - 20f) < 0.01f && Mathf.Abs(far - 40f) < 0.01f, "Through-ray near/far should be 20/40.");

        Ray miss = new Ray(new Vector3(0f, 30f, -30f), Vector3.forward);
        Check(ref failures, !EarthMath.RaySphereIntersections(miss, center, radius, out near, out far), "Ray missing the sphere should not intersect.");

        float nearClip = EarthMath.CalculateThroughEarthNearClip(new Vector3(0f, 0f, -30f), Vector3.forward, center, radius);
        Check(ref failures, nearClip > 20f && nearClip < 40f, "Through-Earth near clip should sit inside the sphere.");
        Check(ref failures, EarthMath.CalculateSkyFarClip(10f, 90f) >= 90f, "Sky far clip must reach the farthest sky sphere.");
    }

    private static void CheckSphericalContainment(ref int failures)
    {
        // A ring crossing the antimeridian: the flat lat/lon test would fail
        // here, so this guards the spherical winding test.
        List<GeoCoordinate> dateline = new List<GeoCoordinate>
        {
            new GeoCoordinate(10f, 170f),
            new GeoCoordinate(10f, -170f),
            new GeoCoordinate(-10f, -170f),
            new GeoCoordinate(-10f, 170f)
        };
        Check(ref failures, CountryBorderRenderer.ContainsOnSphere(new GeoCoordinate(0f, 180f), dateline), "Antimeridian ring should contain (0,180).");
        Check(ref failures, !CountryBorderRenderer.ContainsOnSphere(new GeoCoordinate(0f, 0f), dateline), "Antimeridian ring should not contain (0,0).");

        // A polar cap ring around the south pole.
        List<GeoCoordinate> polarCap = new List<GeoCoordinate>();
        for (int longitude = -180; longitude < 180; longitude += 30)
        {
            polarCap.Add(new GeoCoordinate(-70f, longitude));
        }

        Check(ref failures, CountryBorderRenderer.ContainsOnSphere(new GeoCoordinate(-85f, 40f), polarCap), "Polar cap ring should contain the south pole region.");
        Check(ref failures, !CountryBorderRenderer.ContainsOnSphere(new GeoCoordinate(0f, 40f), polarCap), "Polar cap ring should not contain the equator.");

        // A simple mid-latitude box.
        List<GeoCoordinate> box = new List<GeoCoordinate>
        {
            new GeoCoordinate(40f, -10f),
            new GeoCoordinate(40f, 10f),
            new GeoCoordinate(20f, 10f),
            new GeoCoordinate(20f, -10f)
        };
        Check(ref failures, CountryBorderRenderer.ContainsOnSphere(new GeoCoordinate(30f, 0f), box), "Box should contain its center.");
        Check(ref failures, !CountryBorderRenderer.ContainsOnSphere(new GeoCoordinate(0f, 0f), box), "Box should not contain a point below it.");
    }

    private static void CheckSkyMath(ref int failures)
    {
        // The Sun's declination must stay within the obliquity band.
        Vector3 sun = SkyMath.SunEquatorialDirection();
        float declination = Mathf.Asin(Mathf.Clamp(sun.z, -1f, 1f)) * Mathf.Rad2Deg;
        Check(ref failures, Mathf.Abs(declination) <= 23.5f, "Sun declination should be within +/-23.5 deg, was " + declination);

        // Equatorial->ENU->azimuth/altitude should round-trip a known zenith.
        float lst = 0f;
        float latitude = 45f;
        Vector3 zenithEquatorial = SkyMath.EquatorialToEnu(new Vector3(0f, 0f, 1f), lst, 90f);
        float azimuth;
        float altitude;
        SkyMath.EnuToAzimuthAltitude(zenithEquatorial, out azimuth, out altitude);
        Check(ref failures, Mathf.Abs(altitude - 90f) < 0.01f, "Celestial pole should be at the zenith from the north pole.");

        // Moon topocentric direction should be a unit vector.
        Vector3 moon = SkyMath.MoonTopocentricEquatorialDirection(lst, latitude);
        Check(ref failures, Mathf.Abs(moon.magnitude - 1f) < 0.001f, "Moon direction should be normalized.");
    }

    private static void CheckOrbitMath(ref int failures)
    {
        // A real ISS TLE (epoch 2024). Propagating at epoch must yield a LEO
        // altitude (~400 km) and a valid sub-point.
        const string name = "ISS (ZARYA)";
        const string line1 = "1 25544U 98067A   24152.51782528  .00016717  00000-0  30074-3 0  9993";
        const string line2 = "2 25544  51.6392 205.9059 0002649  73.3103 286.8324 15.50129648456382";

        OrbitMath.TwoLineElements elements;
        Check(ref failures, OrbitMath.TryParseTle(name, line1, line2, out elements), "ISS TLE should parse.");
        if (elements.SemiMajorAxisKm <= 0.0)
        {
            return;
        }

        double altitudeKm = elements.SemiMajorAxisKm - OrbitMath.MeanEarthRadiusKm;
        Check(ref failures, altitudeKm > 300.0 && altitudeKm < 500.0, "ISS semi-major axis should imply a ~400 km altitude, was " + altitudeKm);
        Check(ref failures, Mathf.Abs((float)(elements.InclinationRadians * Mathf.Rad2Deg) - 51.6392f) < 0.01f, "ISS inclination should be ~51.64 deg.");

        float latitude;
        float longitude;
        double radiusKm;
        double gmst = SkyMath.ComputeLocalSiderealDegrees(0f);
        OrbitMath.PropagateToGeo(elements, elements.EpochDaysSinceJ2000, gmst, out latitude, out longitude, out radiusKm);
        Check(ref failures, latitude >= -90f && latitude <= 90f, "Propagated latitude out of range: " + latitude);
        Check(ref failures, longitude >= -180f && longitude <= 180f, "Propagated longitude out of range: " + longitude);
        // ISS never leaves the band bounded by its inclination.
        Check(ref failures, Mathf.Abs(latitude) <= 52.5f, "ISS sub-point latitude should stay within inclination band, was " + latitude);
        double propagatedAltitude = radiusKm - OrbitMath.MeanEarthRadiusKm;
        Check(ref failures, propagatedAltitude > 300.0 && propagatedAltitude < 500.0, "Propagated ISS altitude should be ~400 km, was " + propagatedAltitude);

        // Rejects malformed input.
        OrbitMath.TwoLineElements garbage;
        Check(ref failures, !OrbitMath.TryParseTle("junk", "not a tle", "still not", out garbage), "Malformed TLE should be rejected.");
    }

    private static void Check(ref int failures, bool condition, string message)
    {
        if (!condition)
        {
            failures++;
            Debug.LogError("GlassGlobe math check: " + message);
        }
    }
}
