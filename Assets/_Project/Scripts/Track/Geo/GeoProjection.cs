using System;

namespace GateRush.Track.Geo
{
    /// <summary>
    /// WGS84 latitude/longitude → local metres on the tangent plane at an origin (east-north-up, the Sphere District
    /// origin is the sphere centre): x = east (Unity +X), y = north (Unity +Z). Through ECEF in double precision, so
    /// it stays centimetre-accurate across the 3 km extract.
    /// </summary>
    public sealed class GeoProjection
    {
        private const double A = 6378137.0, F = 1.0 / 298.257223563;
        private static readonly double E2 = F * (2.0 - F);

        public double OriginLat { get; }
        public double OriginLon { get; }

        private readonly double _sinLat, _cosLat, _sinLon, _cosLon;
        private readonly double _x0, _y0, _z0;

        public GeoProjection(double originLat, double originLon)
        {
            OriginLat = originLat;
            OriginLon = originLon;
            double la = originLat * Math.PI / 180.0, lo = originLon * Math.PI / 180.0;
            _sinLat = Math.Sin(la);
            _cosLat = Math.Cos(la);
            _sinLon = Math.Sin(lo);
            _cosLon = Math.Cos(lo);
            (_x0, _y0, _z0) = Ecef(originLat, originLon);
        }

        public (double x, double y) ToLocal(double lat, double lon)
        {
            var (x, y, z) = Ecef(lat, lon);
            double dx = x - _x0, dy = y - _y0, dz = z - _z0;
            double east = -_sinLon * dx + _cosLon * dy;
            double north = -_sinLat * _cosLon * dx - _sinLat * _sinLon * dy + _cosLat * dz;
            return (east, north);
        }

        /// <summary>Inverse of <see cref="ToLocal"/> (on the ellipsoid surface), by Newton iteration.</summary>
        public (double lat, double lon) ToLatLon(double east, double north)
        {
            // Start from the spherical estimate, then correct with the local Jacobian.
            double lat = OriginLat + north / 111250.0, lon = OriginLon + east / (111320.0 * _cosLat);
            for (int i = 0; i < 6; i++)
            {
                var (e, n) = ToLocal(lat, lon);
                const double d = 1e-6;
                var (eLat, nLat) = ToLocal(lat + d, lon);
                var (eLon, nLon) = ToLocal(lat, lon + d);
                double j11 = (eLat - e) / d, j12 = (eLon - e) / d, j21 = (nLat - n) / d, j22 = (nLon - n) / d;
                double det = j11 * j22 - j12 * j21;
                double re = east - e, rn = north - n;
                lat += (j22 * re - j12 * rn) / det;
                lon += (-j21 * re + j11 * rn) / det;
            }
            return (lat, lon);
        }

        private static (double, double, double) Ecef(double lat, double lon)
        {
            double la = lat * Math.PI / 180.0, lo = lon * Math.PI / 180.0;
            double s = Math.Sin(la), c = Math.Cos(la);
            double n = A / Math.Sqrt(1.0 - E2 * s * s);
            return (n * c * Math.Cos(lo), n * c * Math.Sin(lo), n * (1.0 - E2) * s);
        }
    }
}
