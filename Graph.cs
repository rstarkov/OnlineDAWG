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
        private DawgNode _starting = new DawgNode(0, -1);
        private DawgNode _ending = null;
        private DawgHashTable _nodes = new DawgHashTable();
        private ChunkyArrayList<DawgEdge> _edges = new ChunkyArrayList<DawgEdge>();
        private bool _containsEmpty = false;

        /// <summary>Gets the number of distinct "words" (values added with <see cref="Add"/>) that this graph accepts.</summary>
        public int WordCount { get; private set; }
        /// <summary>Gets the number of nodes in the graph.</summary>
        public int NodeCount { get { return _nodes.Count + (_ending == null ? 1 : 2); } }
        /// <summary>Gets the number of edges in the graph.</summary>
        public int EdgeCount { get; private set; }
        /// <summary>Gets the approximate number of bytes consumed by this graph.</summary>
        public long MemoryUsage { get { return (7 * IntPtr.Size + 2 * 4) * (long) NodeCount + (2 * IntPtr.Size) * (long) EdgeCount + _nodes.MemoryUsage; } }

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
                    if (node.EdgesCount != 0)
                        _nodes.Remove(node);
                    node.Hash ^= nextHash;
                    _nodes.Add(node);
                }

                if (node == _ending)
                    _ending = null;

                char c = value[from];

                // Find the outgoing edge index, or insert it if not there yet
                int n = -1;
                int nmin = 0, nmax = node.EdgesCount - 1;
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
                var wantedHash = GetEdgeNode(node, n).Hash ^ nextHash;
                for (var candidate = _nodes.GetFirstInBucket(wantedHash); candidate != null; candidate = candidate.HashNext)
                    if (candidate.Hash == wantedHash && MatchesSameWithAdd(GetEdgeNode(node, n), value, from + 1, candidate))
                    {
                        var old = GetEdgeNode(node, n);
                        SetEdgeNode(node, n, candidate);
                        candidate.RefCount++;
                        dereference(old);
                        return;
                    }
                // If anything else uses the next node, we must make a copy of it, relink to the copy, and modify _that_ instead
                if (GetEdgeNode(node, n).RefCount > 1)
                {
                    var old = GetEdgeNode(node, n);
                    var newn = new DawgNode(old.EdgesCount, _edges.Add(old.EdgesCount)) { Hash = old.Hash };
                    SetEdgeNode(node, n, newn);
#warning Optimize this copy
                    for (int i = 0; i < old.EdgesCount; i++)
                    {
                        SetEdgeAccepting(newn, i, GetEdgeAccepting(old, i));
                        SetEdgeChar(newn, i, GetEdgeChar(old, i));
                        SetEdgeNode(newn, i, GetEdgeNode(old, i));
                        GetEdgeNode(newn, i).RefCount++;
                    }
                    EdgeCount += old.EdgesCount;
                    dereference(old);
                    newn.RefCount++;
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
                int nmin = 0, nmax = node.EdgesCount - 1;
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

        private void dereference(DawgNode node)
        {
            node.RefCount--;
            if (node.RefCount < 0)
                throw new Exception("836");
            if (node.RefCount == 0)
            {
                if (node.EdgesCount != 0)
                    _nodes.Remove(node);
                EdgeCount -= node.EdgesCount;
                for (int i = 0; i < node.EdgesCount; i++)
                    dereference(GetEdgeNode(node, i));
                _edges.Reuse(node.EdgesCount, node.EdgesOffset);
            }
        }

        private void addNewTo(DawgNode node, int edge, string value, int from)
        {
            while (true)
            {
                // The edge has just been created; must initialize every field
                EdgeCount++;
                SetEdgeChar(node, edge, value[from]);
                SetEdgeAccepting(node, edge, from == value.Length - 1);
                if (GetEdgeAccepting(node, edge))
                {
                    if (_ending == null)
                        _ending = new DawgNode(0, -1);
                    SetEdgeNode(node, edge, _ending);
                    _ending.RefCount++;
                    return;
                }

                // Now link this edge to the next node
                from++;

                // See if any existing nodes match just the remaining suffix
                var hash = FnvHash(value, from);
                var n = _nodes.GetFirstInBucket(hash);
                while (n != null)
                {
                    if (n.Hash == hash && MatchesOnly(n, value, from))
                    {
                        SetEdgeNode(node, edge, n);
                        n.RefCount++;
                        return;
                    }
                    n = n.HashNext;
                }

                // No suitable nodes found. Create a new one with one edge, to be initialized by the next iteration.
                SetEdgeNode(node, edge, new DawgNode(1, _edges.Add(1)) { Hash = hash });
                node = GetEdgeNode(node, edge);
                edge = 0;
                node.RefCount++;
                _nodes.Add(node);
            }
        }

        private bool MatchesOnly(DawgNode node, string value, int from)
        {
            for (; from < value.Length; from++)
            {
                if (node.EdgesCount != 1) return false;
                if (GetEdgeChar(node, 0) != value[from]) return false;
                if (GetEdgeAccepting(node, 0) != (from == value.Length - 1)) return false;
                node = GetEdgeNode(node, 0);
            }
            return node.EdgesCount == 0;
        }

        private bool MatchesSame(DawgNode thisNode, DawgNode otherNode)
        {
            if (thisNode.EdgesCount != otherNode.EdgesCount)
                return false;
            for (int i = 0; i < thisNode.EdgesCount; i++)
                if (GetEdgeChar(thisNode, i) != GetEdgeChar(otherNode, i) || GetEdgeAccepting(thisNode, i) != GetEdgeAccepting(otherNode, i))
                    return false;
            for (int i = 0; i < thisNode.EdgesCount; i++)
                if (!MatchesSame(GetEdgeNode(thisNode, i), GetEdgeNode(otherNode, i)))
                    return false;
            return true;
        }

        private bool MatchesSameWithAdd(DawgNode thisNode, string add, int from, DawgNode otherNode)
        {
            if (from == add.Length)
                return MatchesSame(thisNode, otherNode);
            if (thisNode.EdgesCount < otherNode.EdgesCount - 1 || thisNode.EdgesCount > otherNode.EdgesCount)
                return false;

            char c = add[from];
            bool accepting = from == add.Length - 1;
            bool had = false;
            int t, o;
            for (t = o = 0; t < thisNode.EdgesCount && o < otherNode.EdgesCount; t++, o++)
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
                if (t != thisNode.EdgesCount || o != otherNode.EdgesCount - 1 || c != GetEdgeChar(otherNode, o) || accepting != GetEdgeAccepting(otherNode, o))
                    return false;
                if (!MatchesOnly(GetEdgeNode(otherNode, o), add, from + 1))
                    return false;
            }

            return true;
        }

        internal void InsertEdgeAt(DawgNode node, int pos)
        {
            if (node.EdgesCount == 0)
            {
                node.EdgesOffset = _edges.Add(1);
                node.EdgesCount++;
            }
            else
            {
                var newOffset = _edges.Add(node.EdgesCount + 1);
                Array.Copy(
                    _edges._chunks[node.EdgesOffset >> _edges._shifts], node.EdgesOffset & _edges._mask,
                    _edges._chunks[newOffset >> _edges._shifts], newOffset & _edges._mask,
                    pos);
                if (pos < node.EdgesCount)
                    Array.Copy(
                        _edges._chunks[node.EdgesOffset >> _edges._shifts], (node.EdgesOffset & _edges._mask) + pos,
                        _edges._chunks[newOffset >> _edges._shifts], (newOffset & _edges._mask) + pos + 1,
                        node.EdgesCount - pos);
                _edges.Reuse(node.EdgesCount, node.EdgesOffset);
                node.EdgesOffset = newOffset;
                node.EdgesCount++;
            }
        }

        internal IEnumerable<string> Suffixes(DawgNode node)
        {
            return suffixes(node, "");
        }

        private IEnumerable<string> suffixes(DawgNode node, string prefix)
        {
            for (int i = 0; i < node.EdgesCount; i++)
            {
                if (GetEdgeAccepting(node, i))
                    yield return prefix + GetEdgeChar(node, i);
                foreach (var suf in suffixes(GetEdgeNode(node, i), prefix + GetEdgeChar(node, i)))
                    yield return suf;
            }
        }

        internal string NodeToString(DawgNode node)
        {
            return "Node: " + string.Join("|", Suffixes(node).Select(s => s == "" ? "<acc>" : s).ToArray());
        }

        internal bool GetEdgeAccepting(DawgNode node, int index)
        {
            return _edges._chunks[node.EdgesOffset >> _edges._shifts][(node.EdgesOffset & _edges._mask) + index].Accepting;
        }

        internal char GetEdgeChar(DawgNode node, int index)
        {
            return _edges._chunks[node.EdgesOffset >> _edges._shifts][(node.EdgesOffset & _edges._mask) + index].Char;
        }

        internal DawgNode GetEdgeNode(DawgNode node, int index)
        {
            return _edges._chunks[node.EdgesOffset >> _edges._shifts][(node.EdgesOffset & _edges._mask) + index].Node;
        }

        internal void SetEdgeAccepting(DawgNode node, int index, bool value)
        {
            _edges._chunks[node.EdgesOffset >> _edges._shifts][(node.EdgesOffset & _edges._mask) + index].Accepting = value;
        }

        internal void SetEdgeChar(DawgNode node, int index, char value)
        {
            _edges._chunks[node.EdgesOffset >> _edges._shifts][(node.EdgesOffset & _edges._mask) + index].Char = value;
        }

        internal void SetEdgeNode(DawgNode node, int index, DawgNode value)
        {
            _edges._chunks[node.EdgesOffset >> _edges._shifts][(node.EdgesOffset & _edges._mask) + index].Node = value;
        }

        internal IEnumerable<DawgEdge> EnumEdges(DawgNode node)
        {
            if (node.EdgesCount == 0)
                yield break;
            var chunk = _edges._chunks[node.EdgesOffset >> _edges._shifts];
            for (int i = 0; i < node.EdgesCount; i++)
                yield return chunk[(node.EdgesOffset & _edges._mask) + i];
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
            var dummy = new DawgNode(0, -1); // dummy node
            var curnode = dummy;
            foreach (var n in GetNodes())
            {
                curnode.HashNext = n;
                curnode = n;
            }
            // Merge sort them by decreasing RefCount
            var first = mergeSort(dummy.HashNext, NodeCount - 1);
            // Assign integer id's and establish char frequencies
            curnode = first;
            var chars = new Dictionary<char, int>();
            for (int id = 0; curnode != null; id++, curnode = curnode.HashNext)
            {
                curnode.Hash = (uint) id;
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
            Util.OptimWrite(stream, _starting.Hash);
            // Write out nodes
            curnode = first;
            while (curnode != null)
            {
                Util.OptimWrite(stream, (uint) curnode.EdgesCount);
                foreach (var e in EnumEdges(curnode))
                {
                    int f = 0;
                    for (; f < charset.Length; f++)
                        if (charset[f] == e.Char)
                            break;
                    Util.OptimWrite(stream, (uint) ((f << 1) + (e.Accepting ? 1 : 0)));
                    Util.OptimWrite(stream, e.Node.Hash);
                }
                curnode = curnode.HashNext;
            }
        }

        private DawgNode mergeSort(DawgNode first, int count)
        {
            if (count <= 1)
                return first;
            // Divide
            int count1 = count / 2;
            int count2 = count - count1;
            var first1 = first;
            var first2 = first;
            for (int i = 0; i < count1; i++)
                first2 = first2.HashNext;
            var next = first2;
            for (int i = 0; i < count2; i++)
                next = next.HashNext;
            // Recurse
            first1 = mergeSort(first1, count1);
            first2 = mergeSort(first2, count2);
            // Merge
            DawgNode dummy = new DawgNode(0, -1);
            DawgNode cur = dummy;
            while (count1 > 0 || count2 > 0)
            {
                if ((count2 <= 0) || (count1 > 0 && first1.RefCount >= first2.RefCount))
                {
                    cur.HashNext = first1;
                    cur = cur.HashNext;
                    first1 = first1.HashNext;
                    count1--;
                }
                else
                {
                    cur.HashNext = first2;
                    cur = cur.HashNext;
                    first2 = first2.HashNext;
                    count2--;
                }
            }
            cur.HashNext = next;
            return dummy.HashNext;
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
            var nodes = new DawgNode[Util.OptimRead(stream)];
            result.WordCount = (int) Util.OptimRead(stream);
            result._containsEmpty = stream.ReadByte() != 0;
            for (int n = 0; n < nodes.Length; n++)
                nodes[n] = new DawgNode(0, -1);
            result._starting = nodes[Util.OptimRead(stream)];
            for (int n = 0; n < nodes.Length; n++)
            {
                nodes[n].EdgesCount = (short) Util.OptimRead(stream);
                nodes[n].EdgesOffset = result._edges.Add(nodes[n].EdgesCount);
                for (int i = 0; i < nodes[n].EdgesCount; i++)
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

        internal IEnumerable<DawgNode> GetNodes()
        {
            yield return _starting;
            foreach (var node in _nodes)
                yield return node;
            if (_ending != null)
                yield return _ending;
        }
    }
}
