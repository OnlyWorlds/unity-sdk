// The map-space profile: how a reader turns a world's Map, Zone, Marker and Pin elements into geometry (schema-dist
// profiles/map-space.md, serials 105 and 106, ruled 2026-11-02): a Map's frame, a Zone's outline from its Markers, a
// Pin's point, and what the reading left aside, counted by reason. Structure and coordinates in the Map's own pixels;
// looks, cameras, units, rooms and doors are a drawing tool's and are not read here. Since serial 110 the profile's
// second, small reading, OWMapSpace.LayoutOf: one Map's layout keys (x_layout on the Map, x_hand on its Pins). Since
// serial 139 its third, OWMapSpace.CarryOf: where a point on a child Map lies on its parent and on each Map above it
// (x_parent_frame on the child, rules 13 and 14).
//
// Written from the profile and its conformance fixture, profiles/map-space.fixture.json, which is this file's own test
// (OWMapSpaceTests its cases, OWMapLayoutTests its layout_cases, OWMapCarryTests its frame_cases); the TypeScript SDK's
// mapSpace, layoutOf and carryOf and the Python package's map_space, layout_of and carry_of answer the same cases.

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
    /// One Map's layout reading (<see cref="OWMapSpace.LayoutOf"/>; the map-space profile's rules 8 to 10, serial 110):
    /// whether the Map is a layout map and by which method, the Map it stands on, and the Pins a new lay-out keeps where
    /// they are. How a method places Pins, and its own settings, are a layout tool's and are not read here: a tool that
    /// cannot run the method draws the Pins where they are and never lays them out again.
    /// </summary>
    public sealed class OWMapLayout
    {
        /// <summary>The method word of a board, a Map whose Pins are all placed by hand.</summary>
        public const string Board = "hand";

        internal static readonly OWMapLayout None = new OWMapLayout(null, null, Array.Empty<string>());

        internal OWMapLayout(string layout, string ground, IReadOnlyList<string> hand)
        {
            Layout = layout;
            Ground = ground;
            Hand = hand;
        }

        /// <summary>
        /// The Map's <c>x_layout.method</c> when <c>x_layout</c> is an object whose <c>method</c> is a non-empty string
        /// (<see cref="Board"/> for a board; a method no page names answers itself), else null: the Map is not a layout map.
        /// </summary>
        public string Layout { get; }

        /// <summary>
        /// When <see cref="Layout"/> is not null, the id in <c>x_layout.settings.ground.map</c> when it names a served Map
        /// other than this one, else null. Its size is not returned: the served Map's own <c>width</c> and <c>height</c>
        /// win, and the caller holds it.
        /// </summary>
        public string Ground { get; }

        /// <summary>
        /// The ids of this Map's Pins a new lay-out keeps where they are, by id in code point order: every Pin on a board,
        /// the Pins whose <c>x_hand</c> is exactly <c>true</c> on any other layout map, none on a Map that is not one.
        /// </summary>
        public IReadOnlyList<string> Hand { get; }

        /// <summary>True when the Map is a layout map (<see cref="Layout"/> is not null).</summary>
        public bool IsLayoutMap => Layout != null;

        /// <summary>The profile's answer, <c>{layout, ground, hand}</c>, every key present, in that order.</summary>
        public JObject ToJson() => new JObject
        {
            ["layout"] = OWMapSpace.Str(Layout),
            ["ground"] = OWMapSpace.Str(Ground),
            ["hand"] = new JArray(Hand.Select(h => (object)h)),
        };
    }

    /// <summary>Why the frame reading's walk up <c>parent_map</c> ended (<see cref="OWMapSpace.CarryOf"/>, rule 14), in the order the walk tests them.</summary>
    public static class OWMapCarryStop
    {
        /// <summary>The Map has no <c>parent_map</c>: the top of the chain.</summary>
        public const string Top = "top";

        /// <summary>Its <c>parent_map</c> names a Map the reader is not served.</summary>
        public const string Dangling = "dangling";

        /// <summary>Its <c>parent_map</c> names a Map the walk has already reached, the Map itself included.</summary>
        public const string Cycle = "cycle";

        /// <summary>The Map has no parent frame.</summary>
        public const string NoFrame = "no_frame";

        /// <summary>The Map has a parent frame and no carry: its written <c>width</c> or <c>height</c> is not a whole number above 0.</summary>
        public const string NoCarry = "no_carry";
    }

    /// <summary>What is wrong with a Map's own <c>x_parent_frame</c> (<see cref="OWMapSpace.CarryOf"/>, rule 13).</summary>
    public static class OWMapCarryLint
    {
        /// <summary>The key is present and not null on a Map with no <c>parent_map</c>: not read, whatever it holds.</summary>
        public const string NoParent = "no_parent";

        /// <summary>The key is not a list of exactly four numbers; its first four are never read.</summary>
        public const string NotFourNumbers = "not_four_numbers";

        /// <summary>Four numbers whose x0 is not less than x1, or y0 not less than y1: mirrored, or no width or height. Never normalised.</summary>
        public const string NotABox = "not_a_box";
    }

    /// <summary>A point on one Map in the frame reading: the Map's id and the point in its pixels, from its bottom left, as 64-bit floats.</summary>
    public readonly struct OWMapCarryPoint : IEquatable<OWMapCarryPoint>
    {
        internal OWMapCarryPoint(string map, double x, double y)
        {
            Map = map;
            X = x;
            Y = y;
        }

        /// <summary>The Map's id.</summary>
        public string Map { get; }

        /// <summary>Pixels from the Map's left edge.</summary>
        public double X { get; }

        /// <summary>Pixels from the Map's bottom edge.</summary>
        public double Y { get; }

        /// <summary>The profile's <c>{map, x, y}</c>.</summary>
        public JObject ToJson() => new JObject { ["map"] = Map, ["x"] = X, ["y"] = Y };

        /// <summary>The same Map and the same two 64-bit floats (0 and -0 are one number, as the profile compares them).</summary>
        public bool Equals(OWMapCarryPoint other) => Map == other.Map && X == other.X && Y == other.Y;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is OWMapCarryPoint p && Equals(p);

        /// <inheritdoc />
        public override int GetHashCode() =>
            unchecked(((Map?.GetHashCode() ?? 0) * 397 ^ (X == 0 ? 0d : X).GetHashCode()) * 397 ^ (Y == 0 ? 0d : Y).GetHashCode());

        /// <inheritdoc />
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0} [{1:R}, {2:R}]", Map, X, Y);
    }

    /// <summary>
    /// One Map's frame reading (<see cref="OWMapSpace.CarryOf"/>; the map-space profile's rules 13 and 14, serial 139):
    /// the Map's own parent frame and lint, and a point on it carried up the <c>parent_map</c> chain, one level at a
    /// time, until the walk stops. A Map row that is no Map answers no parent frame, no lint, no points and no stop.
    /// </summary>
    public sealed class OWMapCarry
    {
        internal static readonly OWMapCarry NoMap = new OWMapCarry(null, null, null, Array.Empty<OWMapCarryPoint>(), null);

        private readonly JArray _frame;

        internal OWMapCarry(JArray frame, IReadOnlyList<double> parentFrame, string lint, IReadOnlyList<OWMapCarryPoint> points,
            string stop)
        {
            _frame = frame;
            ParentFrame = parentFrame;
            Lint = lint;
            Points = points;
            Stop = stop;
        }

        /// <summary>
        /// The Map's <c>x_parent_frame</c> as <c>[x0, y0, x1, y1]</c> when it is a parent frame, else null. The Map's
        /// own: it does not depend on the point, and it is reported on a Map with no carry and on one whose parent is not
        /// served or closes a ring.
        /// </summary>
        public IReadOnlyList<double> ParentFrame { get; }

        /// <summary>A <see cref="OWMapCarryLint"/> word when this Map's own key is one, else null. A Map further up the chain is read for its own lint by its own reading.</summary>
        public string Lint { get; }

        /// <summary>The point on this Map as given, then the point on each Map it is carried to, in order up the chain. Empty only for a row that is no Map.</summary>
        public IReadOnlyList<OWMapCarryPoint> Points { get; }

        /// <summary>A <see cref="OWMapCarryStop"/> word: why the last point is carried no further. Null only for a row that is no Map.</summary>
        public string Stop { get; }

        /// <summary>The profile's answer, <c>{parent_frame, lint, points, stop}</c>, every key present, in that order; <c>parent_frame</c>'s numbers as served.</summary>
        public JObject ToJson() => new JObject
        {
            ["parent_frame"] = _frame == null ? JValue.CreateNull() : _frame.DeepClone(),
            ["lint"] = OWMapSpace.Str(Lint),
            ["points"] = new JArray(Points.Select(p => (object)p.ToJson())),
            ["stop"] = OWMapSpace.Str(Stop),
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

        /// <summary>
        /// Reads one Map's layout keys (the map-space profile's layout reading, rules 8 to 10): <paramref name="map"/> is
        /// the Map read, <paramref name="maps"/> the Maps the reader is served, <paramref name="pins"/> every Pin the caller
        /// holds, of any Map, as served. The reader keeps the Pins whose <c>map</c> names this Map's id, so a caller passes
        /// every Pin it has. Rows are read as <see cref="Of"/> reads them: a row without a non-empty string <c>id</c> is no
        /// element (a Map row without one is no layout map), and of two rows with one id the last served is read.
        /// </summary>
        /// <remarks>
        /// The Map is a layout map when its <c>x_layout</c> is an object whose <c>method</c> is a non-empty string;
        /// anything else (absent, null, a string, an object without a string <c>method</c>, <c>""</c>) is no layout, and
        /// its ground and Pins are not read. On a board (<c>method</c> <see cref="OWMapLayout.Board"/>) every Pin of the
        /// Map is kept, marked or not; on any other layout map only the Pins whose <c>x_hand</c> is exactly <c>true</c>
        /// (never <c>"true"</c> or <c>1</c>); on a Map that is not a layout map none, whatever its Pins carry. A Pin whose
        /// element the reader cannot see is still a Pin. Nothing in the input is changed. Parity with the TypeScript SDK's
        /// <c>layoutOf</c> and the Python package's <c>layout_of</c>; <see cref="Of"/> reads neither key.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="map"/>, <paramref name="maps"/> or <paramref name="pins"/> is null.</exception>
        public static OWMapLayout LayoutOf(JObject map, IEnumerable<JToken> maps, IEnumerable<JToken> pins)
        {
            if (map == null) throw new ArgumentNullException(nameof(map), "OWMapSpace.LayoutOf: map is one Map element");
            if (maps == null) throw new ArgumentNullException(nameof(maps), "OWMapSpace.LayoutOf: maps is a list of Map elements");
            if (pins == null) throw new ArgumentNullException(nameof(pins), "OWMapSpace.LayoutOf: pins is a list of Pin elements");
            var id = Id(map["id"]);
            if (id == null) return OWMapLayout.None;

            // Rule 8: the mark is x_layout alone, an object with a non-empty string method.
            if (!(map["x_layout"] is JObject recipe)) return OWMapLayout.None;
            var method = recipe["method"] is JValue mv && mv.Type == JTokenType.String ? (string)mv : null;
            if (string.IsNullOrEmpty(method)) return OWMapLayout.None;

            // Rule 9: the ground is a served Map other than this one, named by settings.ground.map.
            string ground = null;
            var named = recipe["settings"] is JObject settings && settings["ground"] is JObject g ? Id(g["map"]) : null;
            if (named != null && named != id && maps.Any(m => m is JObject row && Id(row["id"]) == named)) ground = named;

            // Rule 10: one row per Pin id, the last served; this Map's Pins, every one on a board, else the marked ones.
            var held = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var t in pins)
            {
                if (!(t is JObject p)) continue;
                var pid = Id(p["id"]);
                if (pid != null) held[pid] = p;
            }

            var board = method == OWMapLayout.Board;
            var hand = new List<string>();
            foreach (var kv in held)
            {
                if (Id(kv.Value["map"]) != id) continue;
                if (board || (kv.Value["x_hand"] is JValue h && h.Type == JTokenType.Boolean && (bool)h)) hand.Add(kv.Key);
            }

            hand.Sort(OWCodePointComparer.Instance);
            return new OWMapLayout(method, ground, hand);
        }

        /// <summary>
        /// Reads one Map's frame (the map-space profile's frame reading, rules 13 and 14): <paramref name="map"/> is the
        /// Map read, (<paramref name="x"/>, <paramref name="y"/>) a point on it in its pixels from its bottom left (a
        /// Pin's <c>x</c> and <c>y</c>, or a tap), and <paramref name="maps"/> the Maps the reader is served. A row is a
        /// Map when its <c>type</c> is exactly <c>map</c> and its <c>id</c> a non-empty string; other rows are passed
        /// over, and of two Map rows with one id the last served is read. A <paramref name="map"/> that is no Map answers
        /// no parent frame, no lint, no points and no stop.
        /// </summary>
        /// <remarks>
        /// A Map's <c>x_parent_frame</c> is read only when its <c>parent_map</c> is a link (a non-empty string); it is a
        /// parent frame when it is a list of exactly four finite numbers <c>[x0, y0, x1, y1]</c> with x0 less than x1
        /// and y0 less than y1, in the parent's pixels. The point is carried one level at a time up <c>parent_map</c>,
        /// by each level's own parent frame and its own written <c>width</c> and <c>height</c>, as
        /// <c>x0 + ((x * (x1 - x0)) / width)</c> and <c>y0 + ((y * (y1 - y0)) / height)</c> in 64-bit floats, in that
        /// order, never through one product of the levels' ratios. The walk ends, the first of these that holds, at a
        /// Map with no <c>parent_map</c> (<c>top</c>), one whose parent is not served (<c>dangling</c>), one whose
        /// parent the walk has already reached, itself included (<c>cycle</c>), one with no parent frame
        /// (<c>no_frame</c>), or one whose written <c>width</c> or <c>height</c> is not a whole number above 0, by value
        /// (<c>no_carry</c>). A point outside the child is carried as written, never clipped; <c>z</c> is not carried;
        /// no scale is read or reported, and a layout map is read as any Map. The walk starts from the row passed as
        /// <paramref name="map"/>, never from a row of <paramref name="maps"/> with its id. Pure. Parity with the
        /// TypeScript SDK's <c>carryOf</c> and the Python package's <c>carry_of</c>; <see cref="Of"/> and
        /// <see cref="LayoutOf"/> read no frame key.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="maps"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="x"/> or <paramref name="y"/> is NaN or infinite.</exception>
        public static OWMapCarry CarryOf(JObject map, double x, double y, IEnumerable<JToken> maps)
        {
            if (map == null) throw new ArgumentNullException(nameof(map), "OWMapSpace.CarryOf: map is one Map element");
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "OWMapSpace.CarryOf: the point is two finite numbers; got [{0:R}, {1:R}]", x, y));
            if (maps == null) throw new ArgumentNullException(nameof(maps), "OWMapSpace.CarryOf: maps is a list of Map elements");
            var id = MapId(map);
            if (id == null) return OWMapCarry.NoMap;

            var served = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var t in maps)
                if (t is JObject row && MapId(row) is string mid) served[mid] = row;

            var ownFrame = FrameOf(map, out var ownLint, out var ownTokens);
            var points = new List<OWMapCarryPoint> { new OWMapCarryPoint(id, x, y) };
            var reached = new HashSet<string>(StringComparer.Ordinal) { id };
            var at = map;
            var frame = ownFrame;
            string stop;
            for (;;)
            {
                var parentId = Id(at["parent_map"]);
                if (parentId == null) { stop = OWMapCarryStop.Top; break; }
                if (!served.TryGetValue(parentId, out var parent)) { stop = OWMapCarryStop.Dangling; break; }
                if (reached.Contains(parentId)) { stop = OWMapCarryStop.Cycle; break; }
                if (frame == null) { stop = OWMapCarryStop.NoFrame; break; }
                if (!CarrySize(at["width"], out var width) || !CarrySize(at["height"], out var height))
                {
                    stop = OWMapCarryStop.NoCarry;
                    break;
                }

                // Rule 14's order: the difference, then the product, then the quotient, then the sum.
                x = frame[0] + ((x * (frame[2] - frame[0])) / width);
                y = frame[1] + ((y * (frame[3] - frame[1])) / height);
                points.Add(new OWMapCarryPoint(parentId, x, y));
                reached.Add(parentId);
                at = parent;
                frame = FrameOf(parent, out _, out _);
            }

            return new OWMapCarry(ownTokens, ownFrame, ownLint, points, stop);
        }

        /// <summary>A Map row's id: <c>type</c> exactly <c>map</c> (reading K) and a non-empty string id (reading H); else null.</summary>
        private static string MapId(JObject row) =>
            row["type"] is JValue t && t.Type == JTokenType.String && (string)t == "map" ? Id(row["id"]) : null;

        /// <summary>
        /// A Map's own parent frame (rule 13) as four doubles, or null, with its lint and the list as served. The key is
        /// read only on a Map whose <c>parent_map</c> is a link; absent or null it is no frame and no lint.
        /// </summary>
        private static double[] FrameOf(JObject map, out string lint, out JArray served)
        {
            served = null;
            var key = map["x_parent_frame"];
            var present = key != null && key.Type != JTokenType.Null;
            if (Id(map["parent_map"]) == null)
            {
                lint = present ? OWMapCarryLint.NoParent : null;
                return null;
            }

            lint = null;
            if (!present) return null;
            var f = new double[4];
            if (!(key is JArray list) || list.Count != 4 || !FiniteNumber(list[0], out f[0]) || !FiniteNumber(list[1], out f[1])
                || !FiniteNumber(list[2], out f[2]) || !FiniteNumber(list[3], out f[3]))
            {
                lint = OWMapCarryLint.NotFourNumbers;
                return null;
            }

            if (!(f[0] < f[2]) || !(f[1] < f[3]))
            {
                lint = OWMapCarryLint.NotABox;
                return null;
            }

            served = list;
            return f;
        }

        /// <summary>A JSON number held as a finite 64-bit float (rule 13); a string, a boolean, null, a list, an object and a number past a double's range are none.</summary>
        private static bool FiniteNumber(JToken token, out double value)
        {
            value = 0;
            if (!(token is JValue v) || (v.Type != JTokenType.Integer && v.Type != JTokenType.Float)) return false;
            value = ToDouble(v);
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// A size a carry divides by (rule 13): a whole number above 0 by value, read as a 64-bit float (<c>100.0</c> is
        /// 100). Unlike <see cref="Whole"/> it bounds no magnitude, as the TypeScript SDK's <c>Number.isInteger</c> does
        /// not: an integer past 2^53 reads as its nearest double and divides.
        /// </summary>
        private static bool CarrySize(JToken token, out double size) =>
            FiniteNumber(token, out size) && Math.Floor(size) == size && size > 0;

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
