using System.Linq;
using GateRush.Track.Geo;
using NUnit.Framework;

namespace GateRush.Tests
{
    /// <summary>
    /// Sphere District E1: WGS84 → local metres (east-north tangent plane at the sphere centre) and the OSM extract
    /// parser. Expected values from the WGS84 ellipsoid computed independently (ECEF → ENU).
    /// </summary>
    public class GeoTests
    {
        private static readonly GeoProjection Projection = new GeoProjection(51.08931, 71.41615);

        [Test]
        public void Origin_IsZero()
        {
            var p = Projection.ToLocal(51.08931, 71.41615);
            Assert.AreEqual(0.0, p.x, 1e-6);
            Assert.AreEqual(0.0, p.y, 1e-6);
        }

        [Test]
        public void KnownOffsets_LandWhereExpected()
        {
            var north = Projection.ToLocal(51.08931 + 0.001, 71.41615);
            Assert.AreEqual(0.0, north.x, 0.01, "north stays on the axis");
            Assert.AreEqual(111.250, north.y, 0.01, "0.001 deg of latitude at 51 N");

            var east = Projection.ToLocal(51.08931, 71.41615 + 0.001);
            Assert.AreEqual(70.063, east.x, 0.01, "0.001 deg of longitude at 51 N");
            Assert.AreEqual(0.0, east.y, 0.01);

            var far = Projection.ToLocal(51.08931 + 0.01, 71.41615 + 0.01);
            Assert.AreEqual(700.477, far.x, 0.05);
            Assert.AreEqual(1112.548, far.y, 0.05);
        }

        [Test]
        public void RoundTrip_ReturnsTheSameCoordinates()
        {
            foreach (var (lat, lon) in new[] { (51.0913, 71.4128), (51.1, 71.43), (51.078, 71.396) })
            {
                var p = Projection.ToLocal(lat, lon);
                var (lat2, lon2) = Projection.ToLatLon(p.x, p.y);
                Assert.AreEqual(lat, lat2, 1e-7);
                Assert.AreEqual(lon, lon2, 1e-7);
            }
        }

        private const string Sample = @"{
  ""version"": 0.6,
  ""elements"": [
    { ""type"": ""node"", ""id"": 1, ""lat"": 51.08931, ""lon"": 71.41615 },
    { ""type"": ""node"", ""id"": 2, ""lat"": 51.09031, ""lon"": 71.41615 },
    { ""type"": ""node"", ""id"": 3, ""lat"": 51.09031, ""lon"": 71.41715 },
    { ""type"": ""node"", ""id"": 4, ""lat"": 51.08931, ""lon"": 71.41715, ""tags"": { ""natural"": ""tree"" } },
    { ""type"": ""way"", ""id"": 10, ""nodes"": [1, 2, 3, 4, 1], ""tags"": { ""building"": ""yes"", ""building:levels"": ""5"", ""name"": ""Test \""block\"""" } },
    { ""type"": ""way"", ""id"": 11, ""nodes"": [1, 2], ""tags"": { ""highway"": ""primary"", ""lanes"": ""4"", ""width"": ""14.5"" } },
    { ""type"": ""way"", ""id"": 12, ""nodes"": [1, 2, 3, 1], ""tags"": { ""leisure"": ""park"" } },
    { ""type"": ""way"", ""id"": 13, ""nodes"": [2, 3, 4, 2], ""tags"": { ""building"": ""yes"", ""height"": ""46 m"" } }
  ]
}";

        [Test]
        public void Parser_ReadsBuildingsRoadsAreasAndTrees()
        {
            var data = OsmParser.Parse(Sample, Projection);
            Assert.AreEqual(2, data.Buildings.Count);
            var b = data.Buildings.First(x => x.Id == 10);
            Assert.AreEqual(5, b.Levels);
            Assert.AreEqual(5 * OsmParser.LevelHeight, b.Height, 1e-3, "height from levels");
            Assert.AreEqual(4, b.Footprint.Count, "closing node dropped");
            Assert.AreEqual("Test \"block\"", b.Name);
            Assert.AreEqual(46f, data.Buildings.First(x => x.Id == 13).Height, 1e-3, "height tag with a unit");

            Assert.AreEqual(1, data.Roads.Count);
            Assert.AreEqual("primary", data.Roads[0].Kind);
            Assert.AreEqual(4, data.Roads[0].Lanes);
            Assert.AreEqual(14.5f, data.Roads[0].Width, 1e-3);
            Assert.AreEqual(111.25f, data.Roads[0].Points[1].y, 0.05f);

            Assert.AreEqual(1, data.Areas.Count);
            Assert.AreEqual("park", data.Areas[0].Kind);
            Assert.AreEqual(1, data.Trees.Count);
            Assert.AreEqual(70.06f, data.Trees[0].x, 0.05f);
        }

        [Test]
        public void Parser_DefaultsMissingHeights()
        {
            const string json = @"{""elements"":[{""type"":""node"",""id"":1,""lat"":51.0,""lon"":71.0},{""type"":""node"",""id"":2,""lat"":51.0001,""lon"":71.0},{""type"":""node"",""id"":3,""lat"":51.0001,""lon"":71.0001},{""type"":""way"",""id"":5,""nodes"":[1,2,3,1],""tags"":{""building"":""apartments""}}]}";
            var data = OsmParser.Parse(json, new GeoProjection(51.0, 71.0));
            Assert.AreEqual(OsmParser.DefaultHeight("apartments"), data.Buildings[0].Height, 1e-3);
            Assert.Greater(OsmParser.DefaultHeight("apartments"), OsmParser.DefaultHeight("garage"));
        }
    }
}
