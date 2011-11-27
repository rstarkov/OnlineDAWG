// Copyright 2011 Roman Starkov
// This file is part of OnlineDAWG: https://bitbucket.org/rstarkov/onlinedawg
///
// OnlineDAWG can be redistributed and/or modified under the terms of the GNU General Public License as published by the
// Free Software Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied
// warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OnlineDAWG
{
    public partial class DawgGraph
    {
        private DawgNodeIndex _starting;
        private DawgNodeIndex _ending;
        private DawgHashTable _hashtable;
        private ChunkyNodeList _nodes = new ChunkyNodeList();
        private ChunkyArrayList<DawgEdge> _edges = new ChunkyArrayList<DawgEdge>();
        private bool _containsEmpty = false;

        /// <summary>Gets the number of distinct "words" (values added with <see cref="Add"/>) that this graph accepts.</summary>
        public int WordCount { get; private set; }
        /// <summary>Gets the number of nodes in the graph.</summary>
        public int NodeCount { get { return _hashtable.Count + (_ending == DawgNodeIndex.Null ? 1 : 2); } }
        /// <summary>Gets the number of edges in the graph.</summary>
        public int EdgeCount { get; private set; }
        /// <summary>Gets the approximate number of bytes consumed by this graph.</summary>
        public long MemoryUsage { get { return (7 * IntPtr.Size + 2 * 4) * (long) NodeCount + (2 * IntPtr.Size) * (long) EdgeCount + _hashtable.MemoryUsage; } }

        public DawgGraph()
        {
            _hashtable = new DawgHashTable(this);
            _starting = _nodes.Add();
            _ending = DawgNodeIndex.Null;
        }

        /// <summary>
        /// Adds the specified value to the DAWG. This method *will* result in corruption if this value
        /// is already present; filter out any duplicates using the <see cref="Contains"/> method.
        /// </summary>
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
                    if (GetEdgeChar(node, n) < c)
                        nmin = n + 1;
                    else if (GetEdgeChar(node, n) > c)
                        nmax = n - 1;
                    else // equal
                        break;
                }
                // If the edge wasn't there, special-case the chain-insertion
                if (nmin > nmax)
                {
                    n = nmin;
                    InsertEdgeAt(node, n);
                    addNewTo(node, n, value, from);
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
                    if (GetNodeHash(candidate) == wantedHash && MatchesSameWithAdd(GetEdgeNode(node, n), value, from + 1, candidate))
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
                    var old = GetEdgeNode(node, n);
                    var newn = _nodes.Add();
                    SetNodeEdgesCount(newn, GetNodeEdgesCount(old));
                    SetNodeEdgesOffset(newn, _edges.Add(GetNodeEdgesCount(old)));
                    SetNodeHash(newn, GetNodeHash(old));
                    SetEdgeNode(node, n, newn);
#warning Optimize this copy
                    for (int i = 0; i < GetNodeEdgesCount(old); i++)
                    {
                        SetEdgeAccepting(newn, i, GetEdgeAccepting(old, i));
                        SetEdgeChar(newn, i, GetEdgeChar(old, i));
                        SetEdgeNode(newn, i, GetEdgeNode(old, i));
                        IncNodeRefCount(GetEdgeNode(newn, i));
                    }
                    EdgeCount += GetNodeEdgesCount(old);
                    dereference(old);
                    IncNodeRefCount(newn);
                }

                node = GetEdgeNode(node, n);
            }
        }

        /// <summary>
        /// Queries the DAWG to see if it contains the specified value.
        /// </summary>
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
                    if (GetEdgeChar(node, n) < c)
                        nmin = n + 1;
                    else if (GetEdgeChar(node, n) > c)
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
            DecNodeRefCount(node);
            if (GetNodeRefCount(node) == 0)
            {
                if (GetNodeEdgesCount(node) != 0)
                    _hashtable.Remove(node);
                EdgeCount -= GetNodeEdgesCount(node);
                for (int i = 0; i < GetNodeEdgesCount(node); i++)
                    dereference(GetEdgeNode(node, i));
                _edges.Reuse(GetNodeEdgesCount(node), GetNodeEdgesOffset(node));
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
                    if (GetNodeHash(n) == hash && MatchesOnly(n, value, from))
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

        private bool MatchesOnly(DawgNodeIndex node, string value, int from)
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

        private bool MatchesSame(DawgNodeIndex thisNode, DawgNodeIndex otherNode)
        {
#warning cache edge counts
            if (GetNodeEdgesCount(thisNode) != GetNodeEdgesCount(otherNode))
                return false;
            for (int i = 0; i < GetNodeEdgesCount(thisNode); i++)
                if (GetEdgeChar(thisNode, i) != GetEdgeChar(otherNode, i) || GetEdgeAccepting(thisNode, i) != GetEdgeAccepting(otherNode, i))
                    return false;
            for (int i = 0; i < GetNodeEdgesCount(thisNode); i++)
                if (!MatchesSame(GetEdgeNode(thisNode, i), GetEdgeNode(otherNode, i)))
                    return false;
            return true;
        }

        private bool MatchesSameWithAdd(DawgNodeIndex thisNode, string add, int from, DawgNodeIndex otherNode)
        {
            if (from == add.Length)
                return MatchesSame(thisNode, otherNode);
            if (GetNodeEdgesCount(thisNode) < GetNodeEdgesCount(otherNode) - 1 || GetNodeEdgesCount(thisNode) > GetNodeEdgesCount(otherNode))
                return false;

            char c = add[from];
            bool accepting = from == add.Length - 1;
            bool had = false;
            int t, o;
            for (t = o = 0; t < GetNodeEdgesCount(thisNode) && o < GetNodeEdgesCount(otherNode); t++, o++)
            {
                if (GetEdgeChar(otherNode, o) == c)
                {
                    had = true;
                    if (GetEdgeChar(thisNode, t) == c)
                    {
                        if ((accepting || GetEdgeAccepting(thisNode, t)) != GetEdgeAccepting(otherNode, o))
                            return false;
                        if (!MatchesSameWithAdd(GetEdgeNode(thisNode, t), add, from + 1, GetEdgeNode(otherNode, o)))
                            return false;
                    }
                    else
                    {
                        if (accepting != GetEdgeAccepting(otherNode, o))
                            return false;
                        if (!MatchesOnly(GetEdgeNode(otherNode, o), add, from + 1))
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
                else if (!MatchesSame(GetEdgeNode(thisNode, t), GetEdgeNode(otherNode, o)))
                    return false;
            }
            if (!had)
            {
                if (t != GetNodeEdgesCount(thisNode) || o != GetNodeEdgesCount(otherNode) - 1 || c != GetEdgeChar(otherNode, o) || accepting != GetEdgeAccepting(otherNode, o))
                    return false;
                if (!MatchesOnly(GetEdgeNode(otherNode, o), add, from + 1))
                    return false;
            }

            return true;
        }

        internal void InsertEdgeAt(DawgNodeIndex node, int pos)
        {
            if (GetNodeEdgesCount(node) == 0)
            {
                SetNodeEdgesOffset(node, _edges.Add(1));
                SetNodeEdgesCount(node, (short) (GetNodeEdgesCount(node) + 1));
            }
            else
            {
                var newOffset = _edges.Add(GetNodeEdgesCount(node) + 1);
                Array.Copy(
                    _edges._chunks[GetNodeEdgesOffset(node) >> _edges._shifts], GetNodeEdgesOffset(node) & _edges._mask,
                    _edges._chunks[newOffset >> _edges._shifts], newOffset & _edges._mask,
                    pos);
                if (pos < GetNodeEdgesCount(node))
                    Array.Copy(
                        _edges._chunks[GetNodeEdgesOffset(node) >> _edges._shifts], (GetNodeEdgesOffset(node) & _edges._mask) + pos,
                        _edges._chunks[newOffset >> _edges._shifts], (newOffset & _edges._mask) + pos + 1,
                        GetNodeEdgesCount(node) - pos);
                _edges.Reuse(GetNodeEdgesCount(node), GetNodeEdgesOffset(node));
                SetNodeEdgesOffset(node, newOffset);
                SetNodeEdgesCount(node, (short) (GetNodeEdgesCount(node) + 1));
            }
        }

        internal IEnumerable<string> Suffixes(DawgNodeIndex node)
        {
            return suffixes(node, "");
        }

        private IEnumerable<string> suffixes(DawgNodeIndex node, string prefix)
        {
            for (int i = 0; i < GetNodeEdgesCount(node); i++)
            {
                if (GetEdgeAccepting(node, i))
                    yield return prefix + GetEdgeChar(node, i);
                foreach (var suf in suffixes(GetEdgeNode(node, i), prefix + GetEdgeChar(node, i)))
                    yield return suf;
            }
        }

        internal string NodeToString(DawgNodeIndex node)
        {
            return "Node: " + string.Join("|", Suffixes(node).Select(s => s == "" ? "<acc>" : s).ToArray());
        }

        internal bool GetEdgeAccepting(DawgNodeIndex node, int index)
        {
            return _edges._chunks[GetNodeEdgesOffset(node) >> _edges._shifts][(GetNodeEdgesOffset(node) & _edges._mask) + index].Accepting;
        }

        internal char GetEdgeChar(DawgNodeIndex node, int index)
        {
            return _edges._chunks[GetNodeEdgesOffset(node) >> _edges._shifts][(GetNodeEdgesOffset(node) & _edges._mask) + index].Char;
        }

        internal DawgNodeIndex GetEdgeNode(DawgNodeIndex node, int index)
        {
            return _edges._chunks[GetNodeEdgesOffset(node) >> _edges._shifts][(GetNodeEdgesOffset(node) & _edges._mask) + index].Node;
        }

        internal void SetEdgeAccepting(DawgNodeIndex node, int index, bool value)
        {
            _edges._chunks[GetNodeEdgesOffset(node) >> _edges._shifts][(GetNodeEdgesOffset(node) & _edges._mask) + index].Accepting = value;
        }

        internal void SetEdgeChar(DawgNodeIndex node, int index, char value)
        {
            _edges._chunks[GetNodeEdgesOffset(node) >> _edges._shifts][(GetNodeEdgesOffset(node) & _edges._mask) + index].Char = value;
        }

        internal void SetEdgeNode(DawgNodeIndex node, int index, DawgNodeIndex value)
        {
            _edges._chunks[GetNodeEdgesOffset(node) >> _edges._shifts][(GetNodeEdgesOffset(node) & _edges._mask) + index].Node = value;
        }

        internal IEnumerable<DawgEdge> EnumEdges(DawgNodeIndex node)
        {
            if (GetNodeEdgesCount(node) == 0)
                yield break;
            var chunk = _edges._chunks[GetNodeEdgesOffset(node) >> _edges._shifts];
            for (int i = 0; i < GetNodeEdgesCount(node); i++)
                yield return chunk[(GetNodeEdgesOffset(node) & _edges._mask) + i];
        }

        internal int GetNodeEdgesOffset(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset;
        }

        internal short GetNodeEdgesCount(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesCount;
        }

        internal int GetNodeRefCount(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].RefCount;
        }

        internal uint GetNodeHash(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].Hash;
        }

        internal DawgNodeIndex GetNodeHashNext(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].HashNext;
        }

        internal void SetNodeEdgesOffset(DawgNodeIndex node, int value)
        {
            _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset = value;
        }

        internal void SetNodeEdgesCount(DawgNodeIndex node, short value)
        {
            _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesCount = value;
        }

        internal int IncNodeRefCount(DawgNodeIndex node)
        {
            return ++_nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].RefCount;
        }

        internal int DecNodeRefCount(DawgNodeIndex node)
        {
            return --_nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].RefCount;
        }

        internal void SetNodeHash(DawgNodeIndex node, uint value)
        {
            _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].Hash = value;
        }

        internal void SetNodeHashNext(DawgNodeIndex node, DawgNodeIndex value)
        {
            _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].HashNext = value;
        }

        private static uint FnvHash(string str, int from = 0)
        {
            uint hash = 2166136261;
            for (int i = from; i < str.Length; i++)
                hash = (hash ^ str[i]) * 16777619;
            return hash;
        }

        /// <summary>
        /// Saves the DAWG in binary format to a file at the specified location. Note: to make it possible to modify
        /// the graph using <see cref="Add"/> again, the <see cref="RebuildHashes"/> method must be called first.
        /// </summary>
        public void Save(string path)
        {
            using (var s = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                Save(s);
        }

        /// <summary>
        /// Saves the DAWG in binary format to the specified stream. Note: to make it possible to modify
        /// the graph using <see cref="Add"/> again, the <see cref="RebuildHashes"/> method must be called first.
        /// </summary>
        public void Save(Stream stream)
        {
            // This method reuses the fields Hash and HashNext, destroying their earlier values.

            // Relink all nodes into one single chain
            var dummy = _nodes.Add(); // dummy node
            var curnode = dummy;
            foreach (var n in GetNodes())
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
                foreach (var e in EnumEdges(curnode))
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
                foreach (var e in EnumEdges(curnode))
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
        /// Loads the DAWG from the specified stream, assuming it was saved by <see cref="Save"/>.
        /// Note: to make it possible to modify the graph using <see cref="Add"/> again, the
        /// <see cref="RebuildHashes"/> method must be called first.
        /// </summary>
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
        /// Must be called to make a <see cref="Save"/>d or <see cref="Load"/>ed graph writable again.
        /// Currently unimplemented.
        /// </summary>
        public void RebuildHashes()
        {
            throw new NotImplementedException();
        }

        internal IEnumerable<DawgNodeIndex> GetNodes()
        {
            yield return _starting;
            foreach (var node in _hashtable)
                yield return node;
            if (_ending != DawgNodeIndex.Null)
                yield return _ending;
        }
    }
}
