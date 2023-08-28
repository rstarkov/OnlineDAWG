using System.Text;

namespace OnlineDAWG;

public partial class DawgGraph : IEnumerable<string>
{
    private DawgNodeIndex _starting;
    private DawgNodeIndex _ending;
    private DawgHashTable _hashtable;
    private ChunkyNodeList _nodes = new ChunkyNodeList();
    private ChunkyEdgeList _edges = new ChunkyEdgeList();
    private bool _containsEmpty = false;

    /// <summary>Gets the number of distinct "words" (values added with <see cref="Add"/>) that this graph accepts.</summary>
    public int WordCount { get; private set; }
    /// <summary>Gets the number of nodes in the graph.</summary>
    public int NodeCount { get { return _hashtable.Count + (_ending == DawgNodeIndex.Null ? 1 : 2); } }
    /// <summary>Gets the number of edges in the graph.</summary>
    public int EdgeCount { get; private set; }
    /// <summary>Gets the approximate number of bytes consumed by this graph.</summary>
    public long MemoryUsage { get { return _nodes.MemoryUsage + _edges.MemoryUsage + _hashtable.MemoryUsage; } }

    public DawgGraph()
    {
        _hashtable = new DawgHashTable(this);
        _starting = _nodes.Add();
        _ending = DawgNodeIndex.Null;
    }

    /// <summary>
    ///     Adds the specified value to the DAWG. This method *will* result in corruption if this value is already present;
    ///     filter out any duplicates using the <see cref="Contains"/> method.</summary>
    public void Add(string value)
    {
        WordCount++;

        if (value.Length == 0)
        {
            _containsEmpty = true;
            return;
        }

        var node = _starting;
        uint nextHash = 0;
        for (int from = 0; from < value.Length; from++)
        {
            if (node != _starting)
            {
                if (GetNodeEdgesCount(node) != 0)
                    _hashtable.Remove(node);
                SetNodeHash(node, GetNodeHash(node) ^ nextHash);
                _hashtable.Add(node);
            }

            if (node == _ending)
                _ending = DawgNodeIndex.Null;

            char c = value[from];

            // Find the outgoing edge index, or insert it if not there yet
            int n = -1;
            int nmin = 0, nmax = GetNodeEdgesCount(node) - 1;
            while (nmin <= nmax)
            {
                n = (nmin + nmax) >> 1;
                char cn = GetEdgeChar(node, n);
                if (cn < c)
                    nmin = n + 1;
                else if (cn > c)
                    nmax = n - 1;
                else // equal
                    break;
            }
            // If the edge wasn't there, special-case the chain-insertion
            if (nmin > nmax)
            {
                insertEdgeAt(node, nmin);
                addNewTo(node, nmin, value, from);
                return;
            }
            // If the edge was there and this is the last letter, just mark it accepting and be done
            if (from == value.Length - 1)
            {
                SetEdgeAccepting(node, n, true);
                return;
            }
            // If we already have a node exactly like the (next node + new suffix), just relink to that
            nextHash = FnvHash(value, from + 1);
            var wantedHash = GetNodeHash(GetEdgeNode(node, n)) ^ nextHash;
            for (var candidate = _hashtable.GetFirstInBucket(wantedHash); candidate != DawgNodeIndex.Null; candidate = GetNodeHashNext(candidate))
                if (GetNodeHash(candidate) == wantedHash && matchesSameWithAdd(GetEdgeNode(node, n), value, from + 1, candidate))
                {
                    var old = GetEdgeNode(node, n);
                    SetEdgeNode(node, n, candidate);
                    IncNodeRefCount(candidate);
                    dereference(old);
                    return;
                }
            // If anything else uses the next node, we must make a copy of it, relink to the copy, and modify _that_ instead
            if (GetNodeRefCount(GetEdgeNode(node, n)) > 1)
            {
                var oldNode = GetEdgeNode(node, n);
                var newNode = _nodes.Add();
                var edgesCount = GetNodeEdgesCount(oldNode);
                SetNodeEdgesCount(newNode, edgesCount);
                SetNodeEdgesOffset(newNode, _edges.Add(edgesCount));
                SetNodeHash(newNode, GetNodeHash(oldNode));
                SetEdgeNode(node, n, newNode);
                edgesCopy(GetNodeEdgesOffset(oldNode), GetNodeEdgesOffset(newNode), edgesCount);
                for (int i = 0; i < edgesCount; i++)
                    IncNodeRefCount(GetEdgeNode(newNode, i));
                EdgeCount += edgesCount;
                dereference(oldNode);
                IncNodeRefCount(newNode);
            }

            node = GetEdgeNode(node, n);
        }
    }

    /// <summary>Queries the DAWG to see if it contains the specified value.</summary>
    public bool Contains(string value)
    {
        var node = _starting;
        bool accepting = _containsEmpty;
        for (int index = 0; index < value.Length; index++)
        {
            char c = value[index];

            int n = -1;
            int nmin = 0, nmax = GetNodeEdgesCount(node) - 1;
            while (nmin <= nmax)
            {
                n = (nmin + nmax) >> 1;
                char cn = GetEdgeChar(node, n);
                if (cn < c)
                    nmin = n + 1;
                else if (cn > c)
                    nmax = n - 1;
                else // equal
                    break;
            }
            if (nmin > nmax)
                return false;
            accepting = GetEdgeAccepting(node, n);
            node = GetEdgeNode(node, n);
        }
        return accepting;
    }

    private void dereference(DawgNodeIndex node)
    {
        var newRefCount = DecNodeRefCount(node);
        if (newRefCount == 0)
        {
            var edgesCount = GetNodeEdgesCount(node);
            if (edgesCount != 0)
                _hashtable.Remove(node);
            EdgeCount -= edgesCount;
            for (int i = 0; i < edgesCount; i++)
                dereference(GetEdgeNode(node, i));
            _edges.Reuse(edgesCount, GetNodeEdgesOffset(node));
            _nodes.Reuse(node);
        }
    }

    private void addNewTo(DawgNodeIndex node, int edge, string value, int from)
    {
        while (true)
        {
            // The edge has just been created; must initialize every field
            EdgeCount++;
            SetEdgeChar(node, edge, value[from]);
            SetEdgeAccepting(node, edge, from == value.Length - 1);
            if (GetEdgeAccepting(node, edge))
            {
                if (_ending == DawgNodeIndex.Null)
                    _ending = _nodes.Add();
                SetEdgeNode(node, edge, _ending);
                IncNodeRefCount(_ending);
                return;
            }

            // Now link this edge to the next node
            from++;

            // See if any existing nodes match just the remaining suffix
            var hash = FnvHash(value, from);
            var n = _hashtable.GetFirstInBucket(hash);
            while (n != DawgNodeIndex.Null)
            {
                if (GetNodeHash(n) == hash && matchesOnly(n, value, from))
                {
                    SetEdgeNode(node, edge, n);
                    IncNodeRefCount(n);
                    return;
                }
                n = GetNodeHashNext(n);
            }

            // No suitable nodes found. Create a new one with one edge, to be initialized by the next iteration.
            SetEdgeNode(node, edge, _nodes.Add());
            node = GetEdgeNode(node, edge);
            SetNodeEdgesCount(node, 1);
            SetNodeEdgesOffset(node, _edges.Add(1));
            SetNodeHash(node, hash);
            edge = 0;
            IncNodeRefCount(node);
            _hashtable.Add(node);
        }
    }

    private bool matchesOnly(DawgNodeIndex node, string value, int from)
    {
        for (; from < value.Length; from++)
        {
            if (GetNodeEdgesCount(node) != 1) return false;
            if (GetEdgeChar(node, 0) != value[from]) return false;
            if (GetEdgeAccepting(node, 0) != (from == value.Length - 1)) return false;
            node = GetEdgeNode(node, 0);
        }
        return GetNodeEdgesCount(node) == 0;
    }

    private bool matchesSame(DawgNodeIndex thisNode, DawgNodeIndex otherNode)
    {
        var thisNodeEdgesCount = GetNodeEdgesCount(thisNode);
        if (thisNodeEdgesCount != GetNodeEdgesCount(otherNode))
            return false;
        for (int i = 0; i < thisNodeEdgesCount; i++)
            if (GetEdgeChar(thisNode, i) != GetEdgeChar(otherNode, i) || GetEdgeAccepting(thisNode, i) != GetEdgeAccepting(otherNode, i))
                return false;
        for (int i = 0; i < thisNodeEdgesCount; i++)
            if (!matchesSame(GetEdgeNode(thisNode, i), GetEdgeNode(otherNode, i)))
                return false;
        return true;
    }

    private bool matchesSameWithAdd(DawgNodeIndex thisNode, string add, int from, DawgNodeIndex otherNode)
    {
        var thisNodeEdgesCount = GetNodeEdgesCount(thisNode);
        var otherNodeEdgesCount = GetNodeEdgesCount(otherNode);

        if (from == add.Length)
            return matchesSame(thisNode, otherNode);
        if (thisNodeEdgesCount < otherNodeEdgesCount - 1 || thisNodeEdgesCount > otherNodeEdgesCount)
            return false;

        char c = add[from];
        bool accepting = from == add.Length - 1;
        bool had = false;
        int t, o;
        for (t = o = 0; t < thisNodeEdgesCount && o < otherNodeEdgesCount; t++, o++)
        {
            if (GetEdgeChar(otherNode, o) == c)
            {
                had = true;
                if (GetEdgeChar(thisNode, t) == c)
                {
                    if ((accepting || GetEdgeAccepting(thisNode, t)) != GetEdgeAccepting(otherNode, o))
                        return false;
                    if (!matchesSameWithAdd(GetEdgeNode(thisNode, t), add, from + 1, GetEdgeNode(otherNode, o)))
                        return false;
                }
                else
                {
                    if (accepting != GetEdgeAccepting(otherNode, o))
                        return false;
                    if (!matchesOnly(GetEdgeNode(otherNode, o), add, from + 1))
                        return false;
                    t--;
                }
            }
            else if (GetEdgeChar(thisNode, t) == c)
                return false;
            else if (GetEdgeChar(thisNode, t) != GetEdgeChar(otherNode, o))
                return false;
            else if (GetEdgeAccepting(thisNode, t) != GetEdgeAccepting(otherNode, o))
                return false;
            else if (!matchesSame(GetEdgeNode(thisNode, t), GetEdgeNode(otherNode, o)))
                return false;
        }
        if (!had)
        {
            if (t != thisNodeEdgesCount || o != otherNodeEdgesCount - 1 || c != GetEdgeChar(otherNode, o) || accepting != GetEdgeAccepting(otherNode, o))
                return false;
            if (!matchesOnly(GetEdgeNode(otherNode, o), add, from + 1))
                return false;
        }

        return true;
    }

    private void insertEdgeAt(DawgNodeIndex node, int pos)
    {
        var nodeEdgesCount = GetNodeEdgesCount(node);
        if (nodeEdgesCount == 0)
        {
            SetNodeEdgesOffset(node, _edges.Add(1));
            SetNodeEdgesCount(node, (short) (nodeEdgesCount + 1));
        }
        else
        {
            var nodeEdgesOffset = GetNodeEdgesOffset(node);
            var newOffset = _edges.Add(nodeEdgesCount + 1);
            edgesCopy(nodeEdgesOffset, newOffset, pos);
            if (pos < nodeEdgesCount)
                edgesCopy(nodeEdgesOffset + pos, newOffset + pos + 1, nodeEdgesCount - pos);
            _edges.Reuse(nodeEdgesCount, nodeEdgesOffset);
            SetNodeEdgesOffset(node, newOffset);
            SetNodeEdgesCount(node, (short) (nodeEdgesCount + 1));
        }
    }

    private void edgesCopy(int sourceOffset, int targetOffset, int count)
    {
        Array.Copy(
            _edges._chunks[sourceOffset >> ChunkyEdgeList._shifts], sourceOffset & ChunkyEdgeList._mask,
            _edges._chunks[targetOffset >> ChunkyEdgeList._shifts], targetOffset & ChunkyEdgeList._mask,
            count);
    }

    private IEnumerable<DawgNodeIndex> getNodes()
    {
        yield return _starting;
        foreach (var node in _hashtable)
            yield return node;
        if (_ending != DawgNodeIndex.Null)
            yield return _ending;
    }

    private IEnumerable<DawgEdge> getEdges(DawgNodeIndex node)
    {
        if (GetNodeEdgesCount(node) == 0)
            yield break;
        var chunk = _edges._chunks[GetNodeEdgesOffset(node) >> ChunkyEdgeList._shifts];
        for (int i = 0; i < GetNodeEdgesCount(node); i++)
            yield return chunk[(GetNodeEdgesOffset(node) & ChunkyEdgeList._mask) + i];
    }

    private static uint FnvHash(string str, int from = 0)
    {
        uint hash = 2166136261;
        for (int i = from; i < str.Length; i++)
            hash = (hash ^ str[i]) * 16777619;
        return hash;
    }

    /// <summary>
    ///     Saves the DAWG in binary format to a file at the specified location. Note: to make it possible to modify the graph
    ///     using <see cref="Add"/> again, the <see cref="RebuildHashes"/> method must be called first.</summary>
    public void Save(string path)
    {
        using (var s = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            Save(s);
    }

    /// <summary>
    ///     Saves the DAWG in binary format to the specified stream. Note: to make it possible to modify the graph using <see
    ///     cref="Add"/> again, the <see cref="RebuildHashes"/> method must be called first.</summary>
    public void Save(Stream stream)
    {
        // This method reuses the fields Hash and HashNext, destroying their earlier values.

        // Relink all nodes into one single chain
        var dummy = _nodes.Add(); // dummy node
        var curnode = dummy;
        foreach (var n in getNodes())
        {
            SetNodeHashNext(curnode, n);
            curnode = n;
        }
        // Merge sort them by decreasing RefCount
        var first = mergeSort(GetNodeHashNext(dummy), NodeCount - 1);
        // Assign integer id's and establish char frequencies
        curnode = first;
        var chars = new Dictionary<char, int>();
        for (int id = 0; curnode != DawgNodeIndex.Null; id++, curnode = GetNodeHashNext(curnode))
        {
            SetNodeHash(curnode, (uint) id);
            foreach (var e in getEdges(curnode))
                if (chars.ContainsKey(e.Char))
                    chars[e.Char]++;
                else
                    chars[e.Char] = 1;
        }
        var charset = chars.OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).ToArray();

        // Write out header
        stream.Write(Encoding.UTF8.GetBytes("DAWG.1"), 0, 6);
        Util.OptimWrite(stream, (uint) charset.Length);
        foreach (var c in charset)
            Util.OptimWrite(stream, c);
        Util.OptimWrite(stream, (uint) EdgeCount);
        Util.OptimWrite(stream, (uint) NodeCount);
        Util.OptimWrite(stream, (uint) WordCount);
        stream.WriteByte((byte) (_containsEmpty ? 1 : 0));
        Util.OptimWrite(stream, GetNodeHash(_starting));
        // Write out nodes
        curnode = first;
        while (curnode != DawgNodeIndex.Null)
        {
            Util.OptimWrite(stream, (uint) GetNodeEdgesCount(curnode));
            foreach (var e in getEdges(curnode))
            {
                int f = 0;
                for (; f < charset.Length; f++)
                    if (charset[f] == e.Char)
                        break;
                Util.OptimWrite(stream, (uint) ((f << 1) + (e.Accepting ? 1 : 0)));
                Util.OptimWrite(stream, GetNodeHash(e.Node));
            }
            curnode = GetNodeHashNext(curnode);
        }
        _nodes.Reuse(dummy);
    }

    private DawgNodeIndex mergeSort(DawgNodeIndex first, int count)
    {
        if (count <= 1)
            return first;
        // Divide
        int count1 = count / 2;
        int count2 = count - count1;
        var first1 = first;
        var first2 = first;
        for (int i = 0; i < count1; i++)
            first2 = GetNodeHashNext(first2);
        var next = first2;
        for (int i = 0; i < count2; i++)
            next = GetNodeHashNext(next);
        // Recurse
        first1 = mergeSort(first1, count1);
        first2 = mergeSort(first2, count2);
        // Merge
        DawgNodeIndex dummy = _nodes.Add();
        DawgNodeIndex cur = dummy;
        while (count1 > 0 || count2 > 0)
        {
            if ((count2 <= 0) || (count1 > 0 && GetNodeRefCount(first1) >= GetNodeRefCount(first2)))
            {
                SetNodeHashNext(cur, first1);
                cur = first1;
                first1 = GetNodeHashNext(first1);
                count1--;
            }
            else
            {
                SetNodeHashNext(cur, first2);
                cur = first2;
                first2 = GetNodeHashNext(first2);
                count2--;
            }
        }
        SetNodeHashNext(cur, next);
        var result = GetNodeHashNext(dummy);
        _nodes.Reuse(dummy);
        return result;
    }

    /// <summary>
    ///     Loads the DAWG from the specified stream, assuming it was saved by <see cref="Save(Stream)"/>. Note: to make it
    ///     possible to modify the graph using <see cref="Add"/> again, the <see cref="RebuildHashes"/> method must be called
    ///     first.</summary>
    public static DawgGraph Load(Stream stream)
    {
        var buf = new byte[64];
        Util.FillBuffer(stream, buf, 0, 6);
        if (Encoding.UTF8.GetString(buf, 0, 6) != "DAWG.1")
            throw new InvalidDataException();
        var result = new DawgGraph();

        var charset = new char[Util.OptimRead(stream)];
        for (int i = 0; i < charset.Length; i++)
            charset[i] = (char) Util.OptimRead(stream);

        result.EdgeCount = (int) Util.OptimRead(stream);
        var nodes = new DawgNodeIndex[Util.OptimRead(stream)];
        result.WordCount = (int) Util.OptimRead(stream);
        result._containsEmpty = stream.ReadByte() != 0;
        for (int n = 0; n < nodes.Length; n++)
            nodes[n] = result._nodes.Add();
        result._starting = nodes[Util.OptimRead(stream)];
        for (int n = 0; n < nodes.Length; n++)
        {
            result.SetNodeEdgesCount(nodes[n], (short) Util.OptimRead(stream));
            result.SetNodeEdgesOffset(nodes[n], result._edges.Add(result.GetNodeEdgesCount(nodes[n])));
            for (int i = 0; i < result.GetNodeEdgesCount(nodes[n]); i++)
            {
                var characc = Util.OptimRead(stream);
                result.SetEdgeAccepting(nodes[n], i, (characc & 1) != 0);
                result.SetEdgeChar(nodes[n], i, charset[characc >> 1]);
                result.SetEdgeNode(nodes[n], i, nodes[Util.OptimRead(stream)]);
            }
        }
        return result;
    }

    /// <summary>
    ///     Must be called to make a <see cref="Save(Stream)"/>d or <see cref="Load"/>ed graph writable again. Currently
    ///     unimplemented.</summary>
    public void RebuildHashes()
    {
        throw new NotImplementedException();
    }

    /// <summary>Enumerates all words currently in the graph, in lexicographical order.</summary>
    public IEnumerator<string> GetEnumerator()
    {
        var stack = new List<enumerationState>();
        for (int i = 0; i < 16; i++)
            stack.Add(new enumerationState());
        stack[0].Node = _starting;
        stack[0].Edge = 0;
        stack[0].EdgeCount = GetNodeEdgesCount(_starting);
        stack[0].SoFar = "";
        int pos = 0;
        while (pos >= 0)
        {
            var cur = stack[pos];
            if (cur.Edge >= cur.EdgeCount)
            {
                pos--;
                continue;
            }

            var edgeChar = GetEdgeChar(cur.Node, cur.Edge);
            var edgeAccept = GetEdgeAccepting(cur.Node, cur.Edge);

            pos++;
            if (pos >= stack.Count)
                for (int i = 0; i < 16; i++)
                    stack.Add(new enumerationState());
            var next = stack[pos];

            next.Node = GetEdgeNode(cur.Node, cur.Edge);
            next.Edge = 0;
            next.EdgeCount = GetNodeEdgesCount(next.Node);
            next.SoFar = cur.SoFar + edgeChar;

            cur.Edge++;

            if (edgeAccept)
                yield return next.SoFar;
        }
    }

    private class enumerationState
    {
        public DawgNodeIndex Node;
        public int Edge;
        public int EdgeCount;
        public string SoFar;
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
