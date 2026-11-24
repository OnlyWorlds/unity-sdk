// The most linked elements of each type, read from a graph: a few of every type, never one ranking. A draft reading
// beside the landing profile (OWLanding), held to the packages' own fixture (most-linked.fixture.json, the same bytes in
// the TypeScript SDK and the Python package) until the landing profile takes it as a section. It adds nothing to the
// world and stores nothing: it reads the graph OWGraph.Read answered for the asker, so it sees exactly what that graph
// holds.
//
// Ported from @onlyworlds/sdk 4.203.0 (fc4d560), src/landing.ts's mostLinkedOf (the Python package's most_linked_of):
// here OWMostLinked.Of. Degree is the graph's own count (OWGraph.DegreeOf).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary><see cref="OWMostLinked.Of"/>'s options: <see cref="PerType"/>, a whole number of 1 or more; null is its default.</summary>
    public sealed class OWMostLinkedOptions
    {
        /// <summary>The default of <see cref="PerType"/>.</summary>
        public const long DefaultPerType = 3;

        /// <summary>The largest <see cref="PerType"/> taken: the largest whole number a JavaScript number holds exactly (2^53 - 1), as the TypeScript SDK reads one.</summary>
        public const long MaxPerType = 9007199254740991;

        /// <summary>The keys of the options as JSON, as the fixture writes them.</summary>
        public static readonly IReadOnlyList<string> JsonKeys = Array.AsReadOnly(new[] { "per_type" });

        /// <summary>How many of each type at most. Null: 3.</summary>
        public long? PerType { get; set; }

        /// <summary>
        /// The options written as JSON, in the fixture's words (<c>per_type</c>, the TypeScript SDK's <c>perType</c>). A key
        /// given as null takes its default; a whole number written with a zero fraction (<c>2.0</c>) is that number.
        /// </summary>
        /// <exception cref="ArgumentNullException">No options.</exception>
        /// <exception cref="OWLandingRefusedException">
        /// An unknown key, refused by name before any value is read; then a <c>per_type</c> that is not a whole number
        /// from 1 to 2^53 - 1 (a fraction, a string, a boolean, 0 or less, a larger number), named.
        /// </exception>
        public static OWMostLinkedOptions FromJson(JObject options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options), "OWMostLinkedOptions.FromJson: the options are an object, such as { per_type: 3 }");
            foreach (var p in options.Properties())
            {
                if (!JsonKeys.Contains(p.Name))
                    throw new OWLandingRefusedException(p.Name,
                        $"OWMostLinked.Of: unknown option '{p.Name}'. The keys are {string.Join(", ", JsonKeys)}.");
            }

            var t = options["per_type"];
            if (t == null || t.Type == JTokenType.Null) return new OWMostLinkedOptions();
            if (t.Type == JTokenType.Integer && ((JValue)t).Value is long v && v >= 1 && v <= MaxPerType)
                return new OWMostLinkedOptions { PerType = v };
            if (t.Type == JTokenType.Float)
            {
                var d = (double)t;
                if (d >= 1 && d <= MaxPerType && Math.Floor(d) == d) return new OWMostLinkedOptions { PerType = (long)d };
            }

            throw OWMostLinked.NotWhole(t.ToString(Newtonsoft.Json.Formatting.None));
        }
    }

    /// <summary>One of the most linked: its id, its name as the graph holds it (null when not a string), its degree.</summary>
    public sealed class OWLinked
    {
        internal OWLinked(string id, string name, int degree)
        {
            Id = id;
            Name = name;
            Degree = degree;
        }

        /// <summary>The node's id.</summary>
        public string Id { get; }

        /// <summary>The node's name when it is a string (<see cref="OWGraphNode.Name"/>), as written, or null.</summary>
        public string Name { get; }

        /// <summary>The node's degree in the graph (<see cref="OWGraph.DegreeOf"/>), 1 or more.</summary>
        public int Degree { get; }

        /// <summary>The fixture's <c>{id, name, degree}</c>.</summary>
        public JObject ToJson() => new JObject { ["id"] = Id, ["name"] = Name, ["degree"] = Degree };
    }

    /// <summary>
    /// The most linked elements of each type, as <see cref="Of"/> reads them from a graph: the count per type it was read
    /// at, and by type, the types in code point order. It is "the most linked", never "where to start": in a world with
    /// a secret, the secret is often the hub, so where a world has a Guide, the Guide is the start, and a door says so.
    /// </summary>
    public sealed class OWMostLinked
    {
        private OWMostLinked(long perType, IReadOnlyDictionary<string, IReadOnlyList<OWLinked>> byType,
            IReadOnlyList<string> types)
        {
            PerType = perType;
            ByType = byType;
            Types = types;
        }

        /// <summary>The count per type it was read at.</summary>
        public long PerType { get; }

        /// <summary>Each type's most linked, highest degree first, ties by id in code point order; a type with nothing to pick is absent.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<OWLinked>> ByType { get; }

        /// <summary>The keys of <see cref="ByType"/>, in code point order.</summary>
        public IReadOnlyList<string> Types { get; }

        /// <summary>The answer in the fixture's words: <c>per_type</c>, then <c>by_type</c>, its types in code point order.</summary>
        public JObject ToJson()
        {
            var byType = new JObject();
            foreach (var t in Types) byType[t] = new JArray(ByType[t].Select(l => (object)l.ToJson()));
            return new JObject { ["per_type"] = PerType, ["by_type"] = byType };
        }

        /// <summary>
        /// The most linked elements of each type, from the graph <see cref="OWGraph.Read"/> answered for the asker, so it
        /// holds exactly what that reader may see (map space, hidden, proposed and room elements are out unless the graph
        /// brought them in). Pure: it never changes the graph.
        /// </summary>
        /// <param name="graph">What <see cref="OWGraph.Read"/> answered for the asker.</param>
        /// <param name="options">The options (<see cref="OWMostLinkedOptions"/>, or <see cref="OWMostLinkedOptions.FromJson"/>); null for the defaults.</param>
        /// <remarks>
        /// For each type, at most <c>per_type</c> of its elements by the landing profile's degree
        /// (<see cref="OWGraph.DegreeOf"/>: every edge, in or out, once at each node it touches, so a mirror pair counts 2
        /// at each end and a link to itself once), highest first, ties by id in code point order. A node of degree 0 and
        /// a known-of node are never picked, and a type with nothing to pick is left out. A node's type and name are
        /// echoed as written, never folded. The same graph and options give the same answer, byte for byte.
        /// </remarks>
        /// <exception cref="ArgumentNullException">No graph.</exception>
        /// <exception cref="OWLandingRefusedException">A <c>per_type</c> below 1 or above 2^53 - 1, named, before anything is read.</exception>
        public static OWMostLinked Of(OWWorldGraph graph, OWMostLinkedOptions options = null)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph), "OWMostLinked.Of: graph is what OWGraph.Read answers.");
            var per = options?.PerType ?? OWMostLinkedOptions.DefaultPerType;
            if (per < 1 || per > OWMostLinkedOptions.MaxPerType)
                throw NotWhole(per.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var degree = OWGraph.DegreeOf(graph);
            var grouped = new Dictionary<string, List<OWLinked>>(StringComparer.Ordinal);
            foreach (var n in graph.Nodes)
            {
                if (n.Id == null || n.Type == null || n.KnownOf) continue;
                if (!degree.TryGetValue(n.Id, out var d) || d == 0) continue;
                if (!grouped.TryGetValue(n.Type, out var list)) grouped[n.Type] = list = new List<OWLinked>();
                list.Add(new OWLinked(n.Id, n.Name, d));
            }

            var types = grouped.Keys.OrderBy(t => t, OWCodePointComparer.Instance).ToList();
            var byType = new Dictionary<string, IReadOnlyList<OWLinked>>(StringComparer.Ordinal);
            foreach (var t in types)
            {
                // OrderBy is stable, as JavaScript's sort is: two nodes with one id keep the graph's order.
                byType[t] = grouped[t]
                    .OrderByDescending(l => l.Degree)
                    .ThenBy(l => l.Id, OWCodePointComparer.Instance)
                    .Take((int)Math.Min(per, int.MaxValue))
                    .ToList()
                    .AsReadOnly();
            }

            return new OWMostLinked(per, new ReadOnlyDictionary<string, IReadOnlyList<OWLinked>>(byType), types.AsReadOnly());
        }

        internal static OWLandingRefusedException NotWhole(string got) =>
            new OWLandingRefusedException("per_type", $"OWMostLinked.Of: per_type is a whole number, 1 or more; got {got}.");
    }
}
