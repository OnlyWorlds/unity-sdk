// The map-space profile: how a reader turns a world's Map, Zone, Marker and Pin elements into geometry (schema-dist
// profiles/map-space.md, serials 105 and 106, ruled 2026-11-02): a Map's frame, a Zone's outline from its Markers, a
// Pin's point, and what the reading left aside, counted by reason. Structure and coordinates in the Map's own pixels;
// looks, cameras, units, rooms and doors are a drawing tool's and are not read here.
//
// Written from the profile and its conformance fixture, profiles/map-space.fixture.json, which is this file's own test
// (OWMapSpaceTests); the TypeScript SDK's mapSpace and the Python package's map_space answer the same cases.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>The reasons <see cref="OWMapSpace.Out"/> counts a row under, in the profile's order.</summary>
    public static class OWMapSpaceReason
    {
        /// <summary>A Marker or Pin without a whole <c>x</c> and <c>y</c>.</summary>
        public const string NoPoint = "no_point";

        /// <summary>A Zone with no placed Marker on any served Map: it has no outline.</summary>
        public const string NoMarkers = "no_markers";

        /// <summary>A Marker whose <c>zone</c> or <c>map</c> is not served (checked before its point).</summary>
        public const string Dangling = "dangling";

        /// <summary>A Pin whose <c>map</c> is not served (checked before its point).</summary>
        public const string NoMap = "no_map";

        /// <summary>The four, in the order <see cref="OWMapSpace.ToJson"/> writes them.</summary>
        public static readonly IReadOnlyList<string> All = new[] { NoPoint, NoMarkers, Dangling, NoMap };
    }

    /// <summary>A Zone's outline shape: the Zone's own <c>shape</c> when it is <c>line</c>, else <c>polygon</c>.</summary>
    public static class OWMapSpaceShape
    {
        /// <summary>The Markers join and close.</summary>
        public const string Polygon = "polygon";

        /// <summary>The Markers join and stay open.</summary>
        public const string Line = "line";
    }

    /// <summary>A point in a Map's pixels, from the bottom left; <c>Z</c> is an elevation upward (0 when the row has none).</summary>
    public readonly struct OWMapSpacePoint : IEquatable<OWMapSpacePoint>
    {
        internal OWMapSpacePoint(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Pixels from the Map's left edge.</summary>
        public long X { get; }

        /// <summary>Pixels from the Map's bottom edge.</summary>
        public long Y { get; }

        /// <summary>Pixels up: an elevation, never a height. Kept as written, even past the Map's <c>depth</c>.</summary>
        public long Z { get; }

        /// <summary>The profile's <c>[x, y, z]</c>.</summary>
        public JArray ToJson() => new JArray(X, Y, Z);

        /// <inheritdoc />
        public bool Equals(OWMapSpacePoint other) => X == other.X && Y == other.Y && Z == other.Z;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is OWMapSpacePoint p && Equals(p);

        /// <inheritdoc />
        public override int GetHashCode() => unchecked(((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());

        /// <inheritdoc />
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "[{0}, {1}, {2}]", X, Y, Z);
    }

    /// <summary>One served Map's frame.</summary>
    public sealed class OWMapSpaceMap
    {
        private readonly JValue _scale;

        internal OWMapSpaceMap(string id, long? width, long? height, long? depth, long? zMax, JValue scale, string location,
            string parentMap, bool measured)
        {
            Id = id;
            Width = width;
            Height = height;
            Depth = depth;
            ZMax = zMax;
            _scale = scale;
            Scale = scale == null ? (double?)null : OWMapSpace.ToDouble(scale);
            Location = location;
            ParentMap = parentMap;
            Measured = measured;
        }

        /// <summary>The Map's id.</summary>
        public string Id { get; }

        /// <summary>The Map's own <c>width</c> when it is a whole number, else the largest x among its own points (null with none; <see cref="Measured"/>).</summary>
        public long? Width { get; }

        /// <summary>The Map's own <c>height</c> when it is a whole number, else the largest y among its own points (null with none; <see cref="Measured"/>).</summary>
        public long? Height { get; }

        /// <summary>The Map's <c>depth</c>, its z extent, when it is a whole number, else null.</summary>
        public long? Depth { get; }

        /// <summary>The largest z among the Map's own outline and Pin points, null with none: beside <see cref="Depth"/>, so a z past it shows.</summary>
        public long? ZMax { get; }

        /// <summary>Metres per pixel: the Map's <c>x_metres_per_pixel</c> when it is a positive number, else null. A tool with none draws in pixels.</summary>
        public double? Scale { get; }

        /// <summary>The Map's <c>location</c> id, or null.</summary>
        public string Location { get; }

        /// <summary>The Map's <c>parent_map</c> id, or null.</summary>
        public string ParentMap { get; }

        /// <summary>True when <see cref="Width"/> or <see cref="Height"/> was measured from the Map's own points, not written (the profile's <c>extent: "points"</c>).</summary>
        public bool Measured { get; }

        /// <summary>
        /// The profile's <c>{id, width, height, depth, z_max, scale, location, parent_map}</c>, with <c>extent: "points"</c>
        /// last when a slot was measured. <c>scale</c> is the number as it was served.
        /// </summary>
        public JObject ToJson()
        {
            var o = new JObject
            {
                ["id"] = Id,
                ["width"] = OWMapSpace.Num(Width),
                ["height"] = OWMapSpace.Num(Height),
                ["depth"] = OWMapSpace.Num(Depth),
                ["z_max"] = OWMapSpace.Num(ZMax),
                ["scale"] = _scale == null ? JValue.CreateNull() : (JToken)_scale.DeepClone(),
                ["location"] = OWMapSpace.Str(Location),
                ["parent_map"] = OWMapSpace.Str(ParentMap),
            };
            if (Measured) o["extent"] = "points";
            return o;
        }
    }

    /// <summary>One Zone's outline on one Map: its placed Markers' points in order, a repeated point kept.</summary>
    public sealed class OWMapSpaceOutline
    {
        internal OWMapSpaceOutline(string zone, string map, string shape, IReadOnlyList<OWMapSpacePoint> points)
        {
            Zone = zone;
            Map = map;
            Shape = shape;
            Points = points;
        }

        /// <summary>The Zone's id.</summary>
        public string Zone { get; }

        /// <summary>The Map's id.</summary>
        public string Map { get; }

        /// <summary><see cref="OWMapSpaceShape.Polygon"/> or <see cref="OWMapSpaceShape.Line"/>.</summary>
        public string Shape { get; }

        /// <summary>
        /// The points by the Markers' <c>order</c> ascending; a Marker without a whole <c>order</c> after every ordered
        /// one; ties by the Marker's id in code point order.
        /// </summary>
        public IReadOnlyList<OWMapSpacePoint> Points { get; }

        /// <summary>The profile's <c>{zone, map, shape, points}</c>.</summary>
        public JObject ToJson() => new JObject
        {
            ["zone"] = Zone,
            ["map"] = Map,
            ["shape"] = Shape,
            ["points"] = new JArray(Points.Select(p => (object)p.ToJson())),
        };
    }

    /// <summary>One Pin's point on a served Map, with the element it stands for when it names both keys.</summary>
    public sealed class OWMapSpacePin
    {
        internal OWMapSpacePin(string pin, string map, string elementType, string elementId, OWMapSpacePoint point)
        {
            Pin = pin;
            Map = map;
            ElementType = elementType;
            ElementId = elementId;
            Point = point;
        }

        /// <summary>The Pin's id.</summary>
        public string Pin { get; }

        /// <summary>The Map's id.</summary>
        public string Map { get; }

        /// <summary>The element's type slug, or null; null with <see cref="ElementId"/> when the Pin names only one of the two.</summary>
        public string ElementType { get; }

        /// <summary>The element's id, or null (kept when the element itself is not served: the reader may know the id without the row).</summary>
        public string ElementId { get; }

        /// <summary>The Pin's point.</summary>
        public OWMapSpacePoint Point { get; }

        /// <summary>The profile's <c>{pin, map, element_type, element_id, x, y, z}</c>.</summary>
        public JObject ToJson() => new JObject
        {
            ["pin"] = Pin,
            ["map"] = Map,
            ["element_type"] = OWMapSpace.Str(ElementType),
            ["element_id"] = OWMapSpace.Str(ElementId),
            ["x"] = Point.X,
            ["y"] = Point.Y,
            ["z"] = Point.Z,
        };
    }

    /// <summary>
    /// A world's map space as one reader is served it (<see cref="Of"/>): the Maps' frames, the Zones' outlines, the
    /// Pins' points, and what the reading left aside by reason. Nothing is stored and nothing in the input is changed.
    /// </summary>
    public sealed class OWMapSpace
    {
        private OWMapSpace(IReadOnlyList<OWMapSpaceMap> maps, IReadOnlyList<OWMapSpaceOutline> zones,
            IReadOnlyList<OWMapSpacePin> pins, IReadOnlyDictionary<string, int> @out)
        {
            Maps = maps;
            Zones = zones;
            Pins = pins;
            Out = @out;
        }

        /// <summary>One frame per served Map, by id in code point order.</summary>
        public IReadOnlyList<OWMapSpaceMap> Maps { get; }

        /// <summary>One outline per Zone and Map the Zone has placed Markers on, by Zone id, then Map id, in code point order.</summary>
        public IReadOnlyList<OWMapSpaceOutline> Zones { get; }

        /// <summary>One point per Pin whose Map is served and whose <c>x</c> and <c>y</c> are whole, by id in code point order.</summary>
        public IReadOnlyList<OWMapSpacePin> Pins { get; }

        /// <summary>What the reading left aside, by <see cref="OWMapSpaceReason"/>; a reason with nothing counted is absent.</summary>
        public IReadOnlyDictionary<string, int> Out { get; }

        /// <summary>The profile's answer, <c>{maps, zones, pins, out}</c>, every key present, <c>out</c>'s reasons in the profile's order.</summary>
        public JObject ToJson()
        {
            var counted = new JObject();
            foreach (var reason in OWMapSpaceReason.All)
                if (Out.TryGetValue(reason, out var n)) counted[reason] = n;
            return new JObject
            {
                ["maps"] = new JArray(Maps.Select(m => (object)m.ToJson())),
                ["zones"] = new JArray(Zones.Select(z => (object)z.ToJson())),
                ["pins"] = new JArray(Pins.Select(p => (object)p.ToJson())),
                ["out"] = counted,
            };
        }

        /// <summary>
        /// Reads a world's map space from <paramref name="elements"/>, the elements as the reader is served them (a
        /// folder's rows or the API's, each with its <c>type</c>), so a Zone the reader may not see is not in the input
        /// and is never brought back. Rows of other types are passed over (a type is read case-exactly: <c>Map</c> is no
        /// Map); a row without a non-empty string <c>id</c> is no element and is ignored, never counted; two rows of one
        /// type with one id read as the last one served, for every type.
        /// </summary>
        /// <remarks>
        /// A Zone's outline is the Markers that name it and one served Map, by <c>order</c>; a Marker whose Zone or Map
        /// is not served is counted <c>dangling</c> (before its point), one without a whole <c>x</c> and <c>y</c>
        /// <c>no_point</c>; a Zone with no placed Marker <c>no_markers</c>; a Pin whose Map is not served <c>no_map</c>
        /// (before its point), one without a whole <c>x</c> and <c>y</c> <c>no_point</c>. A whole number is read by
        /// value (<c>2.0</c> is 2; <c>1.5</c>, <c>"2"</c> and a boolean are not), within 64 bits. A <c>z</c> that is
        /// absent or not whole reads as 0. Ids compare by code point, so the same input gives the same bytes. Parity
        /// with the TypeScript SDK's <c>mapSpace</c> and the Python package's <c>map_space</c>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
        public static OWMapSpace Of(IEnumerable<JToken> elements)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements), "OWMapSpace.Of: elements is a list of elements");
            var byCodePoint = OWCodePointComparer.Instance;

            // One row per id within a type, the last served (reading I): an element is its id. The type is read as
            // written (reading K: "Map" is no Map).
            var byType = new Dictionary<string, Dictionary<string, JObject>>(StringComparer.Ordinal);
            foreach (var t in elements)
            {
                // A row without a non-empty string id is no element: ignored, never counted.
                if (!(t is JObject e) || e["type"]?.Type != JTokenType.String) continue;
                var id = Id(e["id"]);
                if (id == null) continue;
                var type = (string)e["type"];
                if (!byType.TryGetValue(type, out var rows))
                    byType[type] = rows = new Dictionary<string, JObject>(StringComparer.Ordinal);
                rows[id] = e;
            }

            Dictionary<string, JObject> Indexed(string type) =>
                byType.TryGetValue(type, out var r) ? r : new Dictionary<string, JObject>(StringComparer.Ordinal);

            IEnumerable<JObject> RowsOf(string type) => Indexed(type).Values;

            var maps = Indexed("map");
            var zones = Indexed("zone");
            int noPoint = 0, noMarkers = 0, dangling = 0, noMap = 0;

            // Markers: dangling first, then no_point; a Marker counts once.
            var placed = new Dictionary<(string zone, string map), List<JObject>>();
            foreach (var mk in RowsOf("marker"))
            {
                var z = Id(mk["zone"]);
                var m = Id(mk["map"]);
                if (z == null || m == null || !zones.ContainsKey(z) || !maps.ContainsKey(m))
                {
                    dangling++;
                    continue;
                }

                if (!Whole(mk["x"], out _) || !Whole(mk["y"], out _))
                {
                    noPoint++;
                    continue;
                }

                if (!placed.TryGetValue((z, m), out var group)) placed[(z, m)] = group = new List<JObject>();
                group.Add(mk);
            }

            var outlines = new List<OWMapSpaceOutline>();
            foreach (var zid in zones.Keys.OrderBy(k => k, byCodePoint))
            {
                var groups = placed.Keys.Where(k => k.zone == zid).Select(k => k.map).OrderBy(k => k, byCodePoint).ToList();
                if (groups.Count == 0)
                {
                    noMarkers++;
                    continue;
                }

                var shape = zones[zid]["shape"] is JValue s && s.Type == JTokenType.String && (string)s == OWMapSpaceShape.Line
                    ? OWMapSpaceShape.Line
                    : OWMapSpaceShape.Polygon;
                foreach (var mid in groups)
                {
                    // A total order: one Marker per id (reading I), so no two tie on order and id.
                    var points = placed[(zid, mid)].OrderBy(mk => mk, MarkerOrder.Instance).Select(PointOf).ToList();
                    outlines.Add(new OWMapSpaceOutline(zid, mid, shape, points));
                }
            }

            var pins = new List<OWMapSpacePin>();
            foreach (var p in RowsOf("pin").OrderBy(r => Id(r["id"]), byCodePoint))
            {
                var m = Id(p["map"]);
                if (m == null || !maps.ContainsKey(m))
                {
                    noMap++;
                    continue;
                }

                if (!Whole(p["x"], out _) || !Whole(p["y"], out _))
                {
                    noPoint++;
                    continue;
                }

                // A link needs both keys: one alone reads as neither.
                var et = Id(p["element_type"]);
                var ei = Id(p["element_id"]);
                if (et == null || ei == null) et = ei = null;
                pins.Add(new OWMapSpacePin(Id(p["id"]), m, et, ei, PointOf(p)));
            }

            var frames = new List<OWMapSpaceMap>();
            foreach (var mid in maps.Keys.OrderBy(k => k, byCodePoint))
            {
                var mp = maps[mid];
                var own = new List<OWMapSpacePoint>();
                foreach (var o in outlines)
                    if (o.Map == mid) own.AddRange(o.Points);
                foreach (var q in pins)
                    if (q.Map == mid) own.Add(q.Point);

                var measured = false;
                long? Slot(string key, Func<OWMapSpacePoint, long> axis)
                {
                    if (Whole(mp[key], out var v)) return v;
                    measured = true;
                    return own.Count == 0 ? (long?)null : own.Max(axis);
                }

                var width = Slot("width", pt => pt.X);
                var height = Slot("height", pt => pt.Y);
                var depth = Whole(mp["depth"], out var d) ? d : (long?)null;
                var zMax = own.Count == 0 ? (long?)null : own.Max(pt => pt.Z);
                frames.Add(new OWMapSpaceMap(mid, width, height, depth, zMax, ScaleOf(mp["x_metres_per_pixel"]),
                    Id(mp["location"]), Id(mp["parent_map"]), measured));
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (noPoint > 0) counts[OWMapSpaceReason.NoPoint] = noPoint;
            if (noMarkers > 0) counts[OWMapSpaceReason.NoMarkers] = noMarkers;
            if (dangling > 0) counts[OWMapSpaceReason.Dangling] = dangling;
            if (noMap > 0) counts[OWMapSpaceReason.NoMap] = noMap;
            return new OWMapSpace(frames, outlines, pins, counts);
        }

        // -- Reading -----------------------------------------------------------------------

        /// <summary>Markers in an outline: a whole <c>order</c> ascending, then the ones without, each tie by id in code point order.</summary>
        private sealed class MarkerOrder : IComparer<JObject>
        {
            internal static readonly MarkerOrder Instance = new MarkerOrder();

            public int Compare(JObject a, JObject b)
            {
                var oa = Whole(a["order"], out var va);
                var ob = Whole(b["order"], out var vb);
                if (oa && ob && va != vb) return va.CompareTo(vb);
                if (oa != ob) return oa ? -1 : 1;
                return OWCodePointComparer.Instance.Compare(Id(a["id"]), Id(b["id"]));
            }
        }

        /// <summary>A placed row's <c>[x, y, z]</c>; its <c>x</c> and <c>y</c> are whole (checked), a <c>z</c> absent or not whole is 0.</summary>
        private static OWMapSpacePoint PointOf(JObject row)
        {
            Whole(row["x"], out var x);
            Whole(row["y"], out var y);
            return new OWMapSpacePoint(x, y, Whole(row["z"], out var z) ? z : 0);
        }

        /// <summary>An id: a non-empty string, anything else none.</summary>
        private static string Id(JToken token) =>
            token is JValue v && v.Type == JTokenType.String && (string)v is string s && s.Length > 0 ? s : null;

        /// <summary>A scale: a positive JSON number (never a boolean or a string), kept as served; else null.</summary>
        private static JValue ScaleOf(JToken token)
        {
            if (!(token is JValue v) || (v.Type != JTokenType.Integer && v.Type != JTokenType.Float)) return null;
            var d = ToDouble(v);
            return d > 0 && !double.IsNaN(d) ? v : null;
        }

        /// <summary>A JSON number as a double; an integer past 64 bits (a BigInteger) as its nearest double.</summary>
        internal static double ToDouble(JValue v) =>
            v.Value is System.Numerics.BigInteger b ? (double)b : Convert.ToDouble(v.Value, CultureInfo.InvariantCulture);

        /// <summary>The largest whole number a double holds exactly, 2^53 - 1 (the TypeScript SDK's <c>Number.MAX_SAFE_INTEGER</c>).</summary>
        private const double SafeWhole = 9007199254740991.0;

        /// <summary>
        /// A JSON number with no fraction as a long (<c>7</c> and <c>7.0</c> alike, as the TypeScript SDK and the Python
        /// package read it); a boolean, a string, a fraction, NaN or a number past 64 bits is not a whole number. A reader's
        /// whole numbers are those it holds exactly: an integer token is read as written, to 64 bits; a double token (a
        /// number written with a fraction or an exponent) only to 2^53 - 1 either way, since past it a double cannot tell
        /// a value the parse had to round from an exact one.
        /// </summary>
        internal static bool Whole(JToken token, out long value)
        {
            value = 0;
            if (!(token is JValue v)) return false;
            if (v.Type == JTokenType.Float)
            {
                var d = v.Value is decimal m ? (double)m : Convert.ToDouble(v.Value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d) return false;
                if (d < -SafeWhole || d > SafeWhole) return false;
                value = (long)d;
                return true;
            }

            if (v.Type != JTokenType.Integer) return false;
            switch (v.Value)
            {
                case long l:
                    value = l;
                    return true;
                case int i:
                    value = i;
                    return true;
                case short s:
                    value = s;
                    return true;
                case byte b:
                    value = b;
                    return true;
                case ulong u when u <= long.MaxValue:
                    value = (long)u;
                    return true;
                case uint ui:
                    value = ui;
                    return true;
                default:
                    return false; // a BigInteger: past 64 bits
            }
        }

        internal static JToken Num(long? v) => v.HasValue ? new JValue(v.Value) : JValue.CreateNull();

        internal static JToken Str(string s) => s == null ? JValue.CreateNull() : new JValue(s);
    }
}
