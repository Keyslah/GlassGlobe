using System;
using System.Globalization;

namespace GlassGlobe
{
    /// <summary>
    /// Two-line element (TLE) parsing and a lightweight satellite propagator:
    /// Kepler's equation plus first-order J2 secular rates (node regression,
    /// perigee drift, mean-motion correction). Deliberately not full SGP4 —
    /// for LEO guide dots on the glass Earth this stays within a few
    /// kilometers of SGP4 for day-old elements, mirroring the planet markers'
    /// circular-orbit philosophy elsewhere in SkyMath.
    /// </summary>
    public static class OrbitMath
    {
        public const double MeanEarthRadiusKm = 6371.0;

        private const double MuKm3PerS2 = 398600.4418;
        private const double J2 = 1.08262668e-3;
        private const double EquatorialRadiusKm = 6378.137;
        private const double TwoPi = Math.PI * 2.0;

        public struct TwoLineElements
        {
            public string Name;
            public double EpochDaysSinceJ2000;
            public double InclinationRadians;
            public double RaanRadians;
            public double Eccentricity;
            public double ArgumentOfPerigeeRadians;
            public double MeanAnomalyRadians;
            public double SemiMajorAxisKm;
            public double RaanRateRadPerDay;
            public double ArgPerigeeRateRadPerDay;
            public double MeanAnomalyRateRadPerDay;
        }

        public static bool TryParseTle(string name, string line1, string line2, out TwoLineElements elements)
        {
            elements = default(TwoLineElements);
            if (line1 == null || line2 == null ||
                line1.Length < 32 || line2.Length < 63 ||
                !line1.StartsWith("1 ", StringComparison.Ordinal) ||
                !line2.StartsWith("2 ", StringComparison.Ordinal))
            {
                return false;
            }

            int epochYearTwoDigit;
            double epochDayOfYear;
            double inclinationDegrees;
            double raanDegrees;
            double argPerigeeDegrees;
            double meanAnomalyDegrees;
            double meanMotionRevPerDay;
            string eccentricityField = line2.Substring(26, 7).Trim();
            long eccentricityDigits;

            if (!int.TryParse(line1.Substring(18, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out epochYearTwoDigit) ||
                !double.TryParse(line1.Substring(20, 12), NumberStyles.Float, CultureInfo.InvariantCulture, out epochDayOfYear) ||
                !double.TryParse(line2.Substring(8, 8), NumberStyles.Float, CultureInfo.InvariantCulture, out inclinationDegrees) ||
                !double.TryParse(line2.Substring(17, 8), NumberStyles.Float, CultureInfo.InvariantCulture, out raanDegrees) ||
                !long.TryParse(eccentricityField, NumberStyles.Integer, CultureInfo.InvariantCulture, out eccentricityDigits) ||
                !double.TryParse(line2.Substring(34, 8), NumberStyles.Float, CultureInfo.InvariantCulture, out argPerigeeDegrees) ||
                !double.TryParse(line2.Substring(43, 8), NumberStyles.Float, CultureInfo.InvariantCulture, out meanAnomalyDegrees) ||
                !double.TryParse(line2.Substring(52, 11), NumberStyles.Float, CultureInfo.InvariantCulture, out meanMotionRevPerDay))
            {
                return false;
            }

            if (meanMotionRevPerDay <= 0.0)
            {
                return false;
            }

            int epochYear = epochYearTwoDigit < 57 ? 2000 + epochYearTwoDigit : 1900 + epochYearTwoDigit;
            DateTime epochUtc = new DateTime(epochYear, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(epochDayOfYear - 1.0);
            DateTime j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            double eccentricity = eccentricityDigits / 1.0e7;
            double inclination = inclinationDegrees * DegToRad;
            double meanMotionRadPerSecond = meanMotionRevPerDay * TwoPi / 86400.0;
            double semiMajorAxisKm = Math.Pow(MuKm3PerS2 / (meanMotionRadPerSecond * meanMotionRadPerSecond), 1.0 / 3.0);
            double semiLatusRectumKm = semiMajorAxisKm * (1.0 - eccentricity * eccentricity);
            if (semiLatusRectumKm <= 0.0)
            {
                return false;
            }

            double meanMotionRadPerDay = meanMotionRevPerDay * TwoPi;
            double j2Factor = 1.5 * J2 * Math.Pow(EquatorialRadiusKm / semiLatusRectumKm, 2.0) * meanMotionRadPerDay;
            double sinInclination = Math.Sin(inclination);
            double sinInclinationSquared = sinInclination * sinInclination;

            elements.Name = name;
            elements.EpochDaysSinceJ2000 = (epochUtc - j2000).TotalDays;
            elements.InclinationRadians = inclination;
            elements.RaanRadians = raanDegrees * DegToRad;
            elements.Eccentricity = eccentricity;
            elements.ArgumentOfPerigeeRadians = argPerigeeDegrees * DegToRad;
            elements.MeanAnomalyRadians = meanAnomalyDegrees * DegToRad;
            elements.SemiMajorAxisKm = semiMajorAxisKm;
            elements.RaanRateRadPerDay = -j2Factor * Math.Cos(inclination);
            elements.ArgPerigeeRateRadPerDay = j2Factor * (2.0 - 2.5 * sinInclinationSquared);
            elements.MeanAnomalyRateRadPerDay = meanMotionRadPerDay *
                (1.0 + 1.5 * J2 * Math.Pow(EquatorialRadiusKm / semiLatusRectumKm, 2.0) *
                    Math.Sqrt(1.0 - eccentricity * eccentricity) * (1.0 - 1.5 * sinInclinationSquared));
            return true;
        }

        /// <summary>
        /// Propagates the elements to the given time and converts to
        /// Earth-fixed geographic position. gmstDegrees is Greenwich mean
        /// sidereal time (SkyMath.ComputeLocalSiderealDegrees(0)).
        /// </summary>
        public static void PropagateToGeo(
            TwoLineElements elements,
            double daysSinceJ2000,
            double gmstDegrees,
            out float latitudeDegrees,
            out float longitudeDegrees,
            out double radiusKm)
        {
            double elapsedDays = daysSinceJ2000 - elements.EpochDaysSinceJ2000;
            double meanAnomaly = WrapRadians(elements.MeanAnomalyRadians + elements.MeanAnomalyRateRadPerDay * elapsedDays);
            double raan = elements.RaanRadians + elements.RaanRateRadPerDay * elapsedDays;
            double argPerigee = elements.ArgumentOfPerigeeRadians + elements.ArgPerigeeRateRadPerDay * elapsedDays;
            double eccentricity = elements.Eccentricity;

            double eccentricAnomaly = meanAnomaly;
            for (int iteration = 0; iteration < 5; iteration++)
            {
                double delta = eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly) - meanAnomaly;
                eccentricAnomaly -= delta / (1.0 - eccentricity * Math.Cos(eccentricAnomaly));
            }

            double trueAnomaly = 2.0 * Math.Atan2(
                Math.Sqrt(1.0 + eccentricity) * Math.Sin(eccentricAnomaly * 0.5),
                Math.Sqrt(1.0 - eccentricity) * Math.Cos(eccentricAnomaly * 0.5));
            radiusKm = elements.SemiMajorAxisKm * (1.0 - eccentricity * Math.Cos(eccentricAnomaly));

            double argumentOfLatitude = argPerigee + trueAnomaly;
            double cosRaan = Math.Cos(raan);
            double sinRaan = Math.Sin(raan);
            double cosU = Math.Cos(argumentOfLatitude);
            double sinU = Math.Sin(argumentOfLatitude);
            double cosInclination = Math.Cos(elements.InclinationRadians);
            double sinInclination = Math.Sin(elements.InclinationRadians);

            double xEci = radiusKm * (cosRaan * cosU - sinRaan * sinU * cosInclination);
            double yEci = radiusKm * (sinRaan * cosU + cosRaan * sinU * cosInclination);
            double zEci = radiusKm * (sinU * sinInclination);

            latitudeDegrees = (float)(Math.Asin(Clamp(zEci / radiusKm, -1.0, 1.0)) * RadToDeg);
            double rightAscensionDegrees = Math.Atan2(yEci, xEci) * RadToDeg;
            double lonEast = rightAscensionDegrees - gmstDegrees;
            lonEast %= 360.0;
            if (lonEast > 180.0)
            {
                lonEast -= 360.0;
            }
            else if (lonEast < -180.0)
            {
                lonEast += 360.0;
            }

            longitudeDegrees = (float)lonEast;
        }

        private static double WrapRadians(double radians)
        {
            radians %= TwoPi;
            if (radians < 0.0)
            {
                radians += TwoPi;
            }

            return radians;
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private const double DegToRad = Math.PI / 180.0;
        private const double RadToDeg = 180.0 / Math.PI;
    }
}
