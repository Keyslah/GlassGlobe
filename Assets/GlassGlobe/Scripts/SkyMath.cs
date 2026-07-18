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
