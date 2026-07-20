using System;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Shared astronomy for sky rendering: sidereal time, the equatorial to
    /// local East/North/Up transform, and low-precision Sun/Moon ephemerides
    /// (Astronomical Almanac approximations, plenty for on-screen placement).
    /// </summary>
    public static class SkyMath
    {
        public static double DaysSinceJ2000()
        {
            DateTime nowUtc = DateTime.UtcNow;
            double julianDay = nowUtc.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds / 86400.0 + 2440587.5;
            return julianDay - 2451545.0;
        }

        public static float ComputeLocalSiderealDegrees(float longitudeEastDegrees)
        {
            double gmst = 280.46061837 + 360.98564736629 * DaysSinceJ2000();
            double lst = gmst + longitudeEastDegrees;
            return WrapDegrees(lst);
        }

        public static Vector3 RaDecToEquatorial(float raDegrees, float decDegrees)
        {
            float ra = raDegrees * Mathf.Deg2Rad;
            float dec = decDegrees * Mathf.Deg2Rad;
            float cosDec = Mathf.Cos(dec);
            return new Vector3(cosDec * Mathf.Cos(ra), cosDec * Mathf.Sin(ra), Mathf.Sin(dec));
        }

        /// <summary>
        /// East/North/Up components of an equatorial-frame direction for the
        /// given local sidereal time and latitude. Returned as (E, N, U).
        /// </summary>
        public static Vector3 EquatorialToEnu(Vector3 equatorial, float lstDegrees, float latitudeDegrees)
        {
            float theta = lstDegrees * Mathf.Deg2Rad;
            float cosTheta = Mathf.Cos(theta);
            float sinTheta = Mathf.Sin(theta);
            float xPrime = cosTheta * equatorial.x + sinTheta * equatorial.y;
            float yPrime = -sinTheta * equatorial.x + cosTheta * equatorial.y;
            float zPrime = equatorial.z;

            float latitude = latitudeDegrees * Mathf.Deg2Rad;
            float cosLat = Mathf.Cos(latitude);
            float sinLat = Mathf.Sin(latitude);

            float east = yPrime;
            float north = cosLat * zPrime - sinLat * xPrime;
            float up = sinLat * zPrime + cosLat * xPrime;
            return new Vector3(east, north, up);
        }

        public static Vector3 EquatorialToWorld(Vector3 equatorial, float lstDegrees, float latitudeDegrees, EarthMath.LocalFrame frame)
        {
            Vector3 enu = EquatorialToEnu(equatorial, lstDegrees, latitudeDegrees);
            return (enu.x * frame.East + enu.y * frame.North + enu.z * frame.Up).normalized;
        }

        public static void EnuToAzimuthAltitude(Vector3 enu, out float azimuthDegrees, out float altitudeDegrees)
        {
            azimuthDegrees = Mathf.Repeat(Mathf.Atan2(enu.x, enu.y) * Mathf.Rad2Deg, 360f);
            altitudeDegrees = Mathf.Asin(Mathf.Clamp(enu.z, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Geocentric Sun direction in the equatorial frame, accurate to about
        /// 0.01 degrees over the current century.
        /// </summary>
        public static Vector3 SunEquatorialDirection()
        {
            double n = DaysSinceJ2000();
            double meanLongitude = WrapDegrees(280.460 + 0.9856474 * n);
            double meanAnomaly = (WrapDegrees(357.528 + 0.9856003 * n)) * DegToRad;
            double eclipticLongitude = (meanLongitude + 1.915 * Math.Sin(meanAnomaly) + 0.020 * Math.Sin(2.0 * meanAnomaly)) * DegToRad;
            double obliquity = (23.439 - 0.0000004 * n) * DegToRad;

            return EclipticToEquatorial(eclipticLongitude, 0.0, obliquity);
        }

        /// <summary>
        /// Lightweight geocentric planet direction. Circular J2000 orbits are
        /// deliberately used here: the foreground dots are guides, not an
        /// observatory ephemeris, while still moving believably through the sky.
        /// </summary>
        public static Vector3 PlanetEquatorialDirection(string planetName)
        {
            double days = DaysSinceJ2000();
            Orbit earth = new Orbit(1.0000, 365.256, 100.464, 0.0, 0.0);
            Orbit planet;

            switch (planetName)
            {
                case "Venus":
                    planet = new Orbit(0.7233, 224.701, 181.979, 3.395, 76.680);
                    break;
                case "Mars":
                    planet = new Orbit(1.5237, 686.980, 355.433, 1.850, 49.558);
                    break;
                case "Jupiter":
                    planet = new Orbit(5.2028, 4332.589, 34.351, 1.303, 100.464);
                    break;
                case "Saturn":
                    planet = new Orbit(9.5388, 10759.22, 50.077, 2.489, 113.665);
                    break;
                default:
                    return Vector3.forward;
            }

            Vector3 heliocentricEarth = OrbitPosition(earth, days);
            Vector3 heliocentricPlanet = OrbitPosition(planet, days);
            Vector3 geocentric = (heliocentricPlanet - heliocentricEarth).normalized;
            double obliquity = (23.439 - 0.0000004 * days) * DegToRad;
            return EclipticVectorToEquatorial(geocentric, obliquity);
        }

        /// <summary>
        /// Geocentric Moon direction in the equatorial frame, accurate to a
        /// few tenths of a degree (topocentric parallax up to ~1 deg ignored).
        /// </summary>
        public static Vector3 MoonEquatorialDirection()
        {
            double n = DaysSinceJ2000();

            double lambda = 218.32 + 13.176396 * n
                + 6.29 * SinDeg(134.9 + 13.064993 * n)
                - 1.27 * SinDeg(259.2 - 0.185600 * n)
                + 0.66 * SinDeg(235.7 + 24.381500 * n)
                + 0.21 * SinDeg(269.9 + 26.107600 * n)
                - 0.19 * SinDeg(357.5 + 0.985600 * n)
                - 0.11 * SinDeg(186.6 + 12.190800 * n);

            double beta = 5.13 * SinDeg(93.3 + 13.228990 * n)
                + 0.28 * SinDeg(228.2 + 26.295400 * n)
                - 0.28 * SinDeg(318.3 + 0.037000 * n)
                - 0.17 * SinDeg(217.6 - 5.163700 * n);

            double obliquity = (23.439 - 0.0000004 * n) * DegToRad;
            return EclipticToEquatorial(WrapDegrees(lambda) * DegToRad, beta * DegToRad, obliquity);
        }

        /// <summary>
        /// Observer-corrected Moon direction. The Moon is close enough that
        /// geocentric placement can miss by nearly a degree near the horizon.
        /// </summary>
        public static Vector3 MoonTopocentricEquatorialDirection(float lstDegrees, float latitudeDegrees)
        {
            Vector3 geocentricDirection = MoonEquatorialDirection();
            double days = DaysSinceJ2000();
            double lunarAnomaly = (134.963 + 13.064993 * days) * DegToRad;
            double elongation = (297.850 + 12.190749 * days) * DegToRad;
            double distanceEarthRadii = 60.36298
                - 3.27746 * Math.Cos(lunarAnomaly)
                - 0.57994 * Math.Cos(2.0 * elongation - lunarAnomaly)
                - 0.46357 * Math.Cos(2.0 * elongation);

            double lst = lstDegrees * DegToRad;
            double latitude = latitudeDegrees * DegToRad;
            Vector3 observerFromEarthCenter = new Vector3(
                (float)(Math.Cos(latitude) * Math.Cos(lst)),
                (float)(Math.Cos(latitude) * Math.Sin(lst)),
                (float)Math.Sin(latitude));

            return (geocentricDirection * (float)distanceEarthRadii - observerFromEarthCenter).normalized;
        }

        private static Vector3 EclipticToEquatorial(double lambdaRadians, double betaRadians, double obliquityRadians)
        {
            double sinLambda = Math.Sin(lambdaRadians);
            double cosLambda = Math.Cos(lambdaRadians);
            double sinBeta = Math.Sin(betaRadians);
            double cosBeta = Math.Cos(betaRadians);
            double sinEps = Math.Sin(obliquityRadians);
            double cosEps = Math.Cos(obliquityRadians);

            double x = cosBeta * cosLambda;
            double y = cosBeta * sinLambda * cosEps - sinBeta * sinEps;
            double z = cosBeta * sinLambda * sinEps + sinBeta * cosEps;

            Vector3 direction = new Vector3((float)x, (float)y, (float)z);
            return direction.normalized;
        }

        private struct Orbit
        {
            public readonly double Radius;
            public readonly double PeriodDays;
            public readonly double LongitudeAtJ2000;
            public readonly double Inclination;
            public readonly double AscendingNode;

            public Orbit(double radius, double periodDays, double longitudeAtJ2000, double inclination, double ascendingNode)
            {
                Radius = radius;
                PeriodDays = periodDays;
                LongitudeAtJ2000 = longitudeAtJ2000;
                Inclination = inclination;
                AscendingNode = ascendingNode;
            }
        }

        private static Vector3 OrbitPosition(Orbit orbit, double days)
        {
            double longitude = (orbit.LongitudeAtJ2000 + 360.0 * days / orbit.PeriodDays) * DegToRad;
            double node = orbit.AscendingNode * DegToRad;
            double inclination = orbit.Inclination * DegToRad;
            double argument = longitude - node;
            double cosArgument = Math.Cos(argument);
            double sinArgument = Math.Sin(argument);
            double cosNode = Math.Cos(node);
            double sinNode = Math.Sin(node);
            double cosInclination = Math.Cos(inclination);
            double sinInclination = Math.Sin(inclination);

            return new Vector3(
                (float)(orbit.Radius * (cosNode * cosArgument - sinNode * sinArgument * cosInclination)),
                (float)(orbit.Radius * (sinNode * cosArgument + cosNode * sinArgument * cosInclination)),
                (float)(orbit.Radius * sinArgument * sinInclination));
        }

        private static Vector3 EclipticVectorToEquatorial(Vector3 ecliptic, double obliquityRadians)
        {
            double cosEps = Math.Cos(obliquityRadians);
            double sinEps = Math.Sin(obliquityRadians);
            return new Vector3(
                ecliptic.x,
                (float)(ecliptic.y * cosEps - ecliptic.z * sinEps),
                (float)(ecliptic.y * sinEps + ecliptic.z * cosEps)).normalized;
        }

        private const double DegToRad = Math.PI / 180.0;

        private static double SinDeg(double degrees)
        {
            return Math.Sin(degrees * DegToRad);
        }

        private static float WrapDegrees(double degrees)
        {
            degrees %= 360.0;
            if (degrees < 0.0)
            {
                degrees += 360.0;
            }

            return (float)degrees;
        }
    }
}
