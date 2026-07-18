using System;
using UnityEngine;

namespace GlassGlobe
{
    [Serializable]
    public struct GeoCoordinate
    {
        [SerializeField]
        private float latitude;

        [SerializeField]
        private float longitude;

        public GeoCoordinate(float latitude, float longitude)
        {
            this.latitude = EarthMath.ClampLatitude(latitude);
            this.longitude = EarthMath.WrapLongitude(longitude);
        }

        public float Latitude
        {
            get { return latitude; }
            set { latitude = EarthMath.ClampLatitude(value); }
        }

        public float Longitude
        {
            get { return longitude; }
            set { longitude = EarthMath.WrapLongitude(value); }
        }

        public override string ToString()
        {
            return string.Format("{0:0.0000} deg, {1:0.0000} deg", latitude, longitude);
        }
    }
}
