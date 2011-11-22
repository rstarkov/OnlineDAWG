using System.IO;
using System.Text;

namespace OnlineDAWG
{
    public class DawgGraphReadonly
    {
        private DawgEdgeReadonly[] _edges;
        private int _startingIndex;
        private bool _containsEmpty;

        /// <summary>Gets the number of distinct "words" (values added with <see cref="Add"/>) that this graph accepts.</summary>
        public int WordCount { get; private set; }
        /// <summary>Gets the number of nodes in the graph.</summary>
        public int NodeCount { get; private set; }
        /// <summary>Gets the number of edges in the graph.</summary>
        public int EdgeCount { get { return _edges.Length; } }
        /// <summary>Gets the approximate number of bytes of memory that this graph consumes.</summary>
        public long MemoryUsage { get { return 8 * _edges.LongLength; } }

        /// <summary>Loads the DAWG from the specified file.</summary>
        public static DawgGraphReadonly Load(string filename)
        {
            using (var s = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                return Load(s);
        }

        /// <summary>Loads the DAWG from the specified compatible stream.</summary>
        public static DawgGraphReadonly Load(Stream stream)
        {
            var buf = new byte[64];
            Util.FillBuffer(stream, buf, 0, 6);
            if (Encoding.UTF8.GetString(buf, 0, 6) != "DAWG.1")
                throw new InvalidDataException();
            var result = new DawgGraphReadonly();

            var charset = new char[Util.OptimRead(stream)];
            for (int i = 0; i < charset.Length; i++)
                charset[i] = (char) Util.OptimRead(stream);

            result._edges = new DawgEdgeReadonly[(int) Util.OptimRead(stream)];
            result.NodeCount = (int) Util.OptimRead(stream);
            result.WordCount = (int) Util.OptimRead(stream);
            result._containsEmpty = stream.ReadByte() != 0;
            var nodeFirstEdgeIndex = new int[result.NodeCount];
            result._startingIndex = (int) Util.OptimRead(stream);
            int e = 0;
            for (int n = 0; n < nodeFirstEdgeIndex.Length; n++)
            {
                var edgesCount = (short) Util.OptimRead(stream);
                nodeFirstEdgeIndex[n] = edgesCount == 0 ? -1 : e;
                for (int i = 0; i < edgesCount; i++)
                {
                    var characc = Util.OptimRead(stream);
                    result._edges[e].Accepting = (characc & 1) != 0;
                    result._edges[e].Char = charset[characc >> 1];
                    result._edges[e].EdgesIndex = (int) Util.OptimRead(stream); // initially this is the node index, because the edge index is unknown at this time
                    result._edges[e].Last = i == edgesCount - 1;
                    e++;
                }
            }
            // Now that the first edge index is known for every node, patch them into the edges array
            result._startingIndex = nodeFirstEdgeIndex[result._startingIndex];
            for (int i = 0; i < result._edges.Length; i++)
                result._edges[i].EdgesIndex = nodeFirstEdgeIndex[result._edges[i].EdgesIndex];

            return result;
        }

        /// <summary>Looks up the specified value if the DAWG and returns a value to indicate if it's present.</summary>
        public bool Contains(string value)
        {
            int cur = _startingIndex;
            bool accepting = _containsEmpty;
            for (int i = 0; i < value.Length; i++)
            {
                if (cur < 0)
                    return false;
                char c = value[i];
                int e = cur;
                while (true)
                {
                    if (_edges[e].Char == c)
                    {
                        cur = _edges[e].EdgesIndex;
                        accepting = _edges[e].Accepting;
                        break;
                    }
                    if (_edges[e].Last)
                        return false;
                    e++;
                }
            }
            return accepting;
        }
    }

    struct DawgEdgeReadonly
    {
        /// <summary>Index of the first edge of the node that this edge points to, or -1 if the target node is the ending one.</summary>
        public int EdgesIndex;
        public char Char;
        public bool Accepting;
        /// <summary>True if this edge is the last one in the set of edges belonging to the current node.</summary>
        public bool Last;
    }
}
