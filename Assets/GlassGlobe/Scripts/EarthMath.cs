using System.Collections.Generic;
using UnityEngine;

namespace GlassGlobe
{
    public static class EarthMath
    {
        public const float DefaultEarthRadiusUnits = 10f;

        public struct LocalFrame
        {
            public Vector3 East;
            public Vector3 North;
            public Vector3 Up;
            public Vector3 Down;
        }

        public static float ClampLatitude(float latitude)
        {
            return Mathf.Clamp(latitude, -90f, 90f);
        }

        public static float WrapLongitude(float longitude)
        {
            longitude = Mathf.Repeat(longitude + 180f, 360f) - 180f;
            if (Mathf.Approximately(longitude, -180f))
            {
                return 180f;
            }

            return longitude;
        }

        // The embedding negates x so that Unity's left-handed cameras see the
        // physically correct (mirror-flipped) far side when looking through the
        // globe from the surface. Rays cast in real-world directions then exit
        // at the true lat/lon.
        public static Vector3 GeoToUnitVector(GeoCoordinate coordinate)
        {
            float latitude = coordinate.Latitude * Mathf.Deg2Rad;
            float longitude = coordinate.Longitude * Mathf.Deg2Rad;
            float cosLatitude = Mathf.Cos(latitude);

            return new Vector3(
                -cosLatitude * Mathf.Sin(longitude),
                Mathf.Sin(latitude),
                cosLatitude * Mathf.Cos(longitude)).normalized;
        }

        public static Vector3 GeoToPoint(GeoCoordinate coordinate, float radius, Vector3 center)
        {
            return center + GeoToUnitVector(coordinate) * radius;
        }

        public static GeoCoordinate UnitVectorToGeo(Vector3 unitVector)
        {
            if (unitVector.sqrMagnitude < 0.000001f)
            {
                return new GeoCoordinate(0f, 0f);
            }

            Vector3 normal = unitVector.normalized;
            float latitude = Mathf.Asin(Mathf.Clamp(normal.y, -1f, 1f)) * Mathf.Rad2Deg;
            float longitude = Mathf.Atan2(-normal.x, normal.z) * Mathf.Rad2Deg;
            return new GeoCoordinate(latitude, longitude);
        }

        public static GeoCoordinate PointToGeo(Vector3 point, Vector3 center)
        {
            return UnitVectorToGeo(point - center);
        }

        public static LocalFrame GetLocalFrame(GeoCoordinate coordinate)
        {
            Vector3 up = GeoToUnitVector(coordinate);
            float longitude = coordinate.Longitude * Mathf.Deg2Rad;
            Vector3 east = new Vector3(-Mathf.Cos(longitude), 0f, -Mathf.Sin(longitude)).normalized;
            Vector3 north = Vector3.Cross(east, up).normalized;

            if (north.sqrMagnitude < 0.000001f)
            {
                north = Vector3.forward;
            }

            return new LocalFrame
            {
                East = east,
                North = north,
                Up = up,
                Down = -up
            };
        }

        public static Vector3 GetHeadingTangent(GeoCoordinate coordinate, float headingDegrees)
        {
            LocalFrame frame = GetLocalFrame(coordinate);
            float headingRadians = headingDegrees * Mathf.Deg2Rad;
            return (frame.North * Mathf.Cos(headingRadians) + frame.East * Mathf.Sin(headingRadians)).normalized;
        }

        public static Vector3 GetInwardViewDirection(GeoCoordinate coordinate, float headingDegrees, float tiltDegrees)
        {
            LocalFrame frame = GetLocalFrame(coordinate);
            Vector3 headingTangent = GetHeadingTangent(coordinate, headingDegrees);
            float tiltRadians = Mathf.Clamp(tiltDegrees, 0f, 89.9f) * Mathf.Deg2Rad;
            return (frame.Down * Mathf.Cos(tiltRadians) + headingTangent * Mathf.Sin(tiltRadians)).normalized;
        }

        public static Quaternion GetInwardViewRotation(GeoCoordinate coordinate, float headingDegrees, float tiltDegrees)
        {
            LocalFrame frame = GetLocalFrame(coordinate);
            Vector3 headingTangent = GetHeadingTangent(coordinate, headingDegrees);
            float tiltRadians = Mathf.Clamp(tiltDegrees, 0f, 89.9f) * Mathf.Deg2Rad;
            Vector3 forward = (frame.Down * Mathf.Cos(tiltRadians) + headingTangent * Mathf.Sin(tiltRadians)).normalized;
            Vector3 cameraUp = (headingTangent * Mathf.Cos(tiltRadians) - frame.Down * Mathf.Sin(tiltRadians)).normalized;

            if (cameraUp.sqrMagnitude < 0.000001f)
            {
                cameraUp = frame.North;
            }

            return Quaternion.LookRotation(forward, cameraUp);
        }

        public static bool RaySphereIntersections(
            Ray ray,
            Vector3 center,
            float radius,
            out float nearDistance,
            out float farDistance)
        {
            nearDistance = 0f;
            farDistance = 0f;

            Vector3 originToCenter = ray.origin - center;
            float a = Vector3.Dot(ray.direction, ray.direction);
            float b = 2f * Vector3.Dot(originToCenter, ray.direction);
            float c = Vector3.Dot(originToCenter, originToCenter) - radius * radius;
            float discriminant = b * b - 4f * a * c;

            if (discriminant < 0f)
            {
                return false;
            }

            float root = Mathf.Sqrt(discriminant);
            float t0 = (-b - root) / (2f * a);
            float t1 = (-b + root) / (2f * a);

            if (t0 > t1)
            {
                float temp = t0;
                t0 = t1;
                t1 = temp;
            }

            if (t1 < 0f)
            {
                return false;
            }

            nearDistance = Mathf.Max(t0, 0f);
            farDistance = t1;
            return true;
        }

        public static List<Vector3> BuildGreatCircleArc(
            GeoCoordinate start,
            GeoCoordinate end,
            float radius,
            Vector3 center,
            float maxSegmentDegrees)
        {
            List<Vector3> points = new List<Vector3>();
            AppendGreatCircleArc(points, start, end, radius, center, maxSegmentDegrees, true);
            return points;
        }

        public static void AppendGreatCircleArc(
            List<Vector3> points,
            GeoCoordinate start,
            GeoCoordinate end,
            float radius,
            Vector3 center,
            float maxSegmentDegrees,
            bool includeStart)
        {
            Vector3 startUnit = GeoToUnitVector(start);
            Vector3 endUnit = GeoToUnitVector(end);
            float dot = Mathf.Clamp(Vector3.Dot(startUnit, endUnit), -1f, 1f);
            float angleDegrees = Mathf.Acos(dot) * Mathf.Rad2Deg;
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(angleDegrees / Mathf.Max(0.25f, maxSegmentDegrees)));

            int firstIndex = includeStart ? 0 : 1;
            for (int index = firstIndex; index <= segmentCount; index++)
            {
                float t = index / (float)segmentCount;
                Vector3 unit = Vector3.Slerp(startUnit, endUnit, t).normalized;
                points.Add(center + unit * radius);
            }
        }

        public static float AngularDistanceDegrees(GeoCoordinate a, GeoCoordinate b)
        {
            float dot = Mathf.Clamp(Vector3.Dot(GeoToUnitVector(a), GeoToUnitVector(b)), -1f, 1f);
            return Mathf.Acos(dot) * Mathf.Rad2Deg;
        }
    }
}
