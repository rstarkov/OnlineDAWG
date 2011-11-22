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
        private DawgNode _starting = new DawgNode(0);
        private DawgNode _ending = null;
        private DawgHashTable _nodes = new DawgHashTable();
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
            if (value.Length == 0)
            {
                _containsEmpty = true;
                WordCount++;
                return;
            }

            var node = _starting;
            uint nexthash = 0;
            for (int restFrom = 1; restFrom <= value.Length + 1; restFrom++)
            {
                if (node != _starting)
                {
                    if (!node.IsBlank())
                        _nodes.Remove(node);
                    node.Hash ^= nexthash;
                    _nodes.Add(node);
                }

                if (node == _ending)
                    _ending = null;

                char c = value[restFrom - 1];

                // Find the outgoing edge index, or insert it if not there yet
                int n = -1;
                int nmin = 0, nmax = node.Edges.Length - 1;
                while (nmin <= nmax)
                {
                    n = (nmin + nmax) >> 1;
                    if (node.Edges[n].Char < c)
                        nmin = n + 1;
                    else if (node.Edges[n].Char > c)
                        nmax = n - 1;
                    else // equal
                        break;
                }
                // If the edge wasn't there, special-case the chain-insertion
                if (nmin > nmax)
                {
                    n = nmin;
                    node.InsertBlankAt(n);
                    node.Edges[n].Char = c;
                    node.Edges[n].Node = addNew(value, restFrom);
                    node.Edges[n].Node.RefCount++;
                    node.Edges[n].Accepting = restFrom == value.Length;
                    EdgeCount++;
                    break;
                }
                // If the edge was there and this is the last letter, just mark it accepting and be done
                if (restFrom == value.Length)
                {
                    node.Edges[n].Accepting = true;
                    break;
                }
                // If we already have a node exactly like the (next node + new suffix), just relink to that
                nexthash = FnvHash(value, restFrom);
                bool done = false;
                var wantedhash = node.Edges[n].Node.Hash ^ nexthash;
                var candidate = _nodes.GetFirstInBucket(wantedhash);
                while (candidate != null)
                {
                    if (candidate.Hash == wantedhash)
                    {
                        if (node.Edges[n].Node.MatchesSameWithAdd(value, restFrom, candidate))
                        {
                            var old = node.Edges[n].Node;
                            node.Edges[n].Node = candidate;
                            node.Edges[n].Node.RefCount++;
                            dereference(old);
                            done = true;
                            break;
                        }
                    }
                    candidate = candidate.HashNext;
                }
                if (done)
                    break;
                // If anything else uses the next node, we must make a copy of it, relink to the copy, and modify _that_ instead
                if (node.Edges[n].Node.RefCount > 1)
                {
                    var old = node.Edges[n].Node;
                    var newn = node.Edges[n].Node = new DawgNode(old.Edges.Length) { Hash = old.Hash };
                    for (int i = 0; i < old.Edges.Length; i++)
                    {
                        newn.Edges[i].Accepting = old.Edges[i].Accepting;
                        newn.Edges[i].Char = old.Edges[i].Char;
                        newn.Edges[i].Node = old.Edges[i].Node;
                        newn.Edges[i].Node.RefCount++;
                    }
                    EdgeCount += old.Edges.Length;
                    dereference(old);
                    newn.RefCount++;
                }

                node = node.Edges[n].Node;
            }

            WordCount++;
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
                int nmin = 0, nmax = node.Edges.Length - 1;
                while (nmin <= nmax)
                {
                    n = (nmin + nmax) >> 1;
                    if (node.Edges[n].Char < c)
                        nmin = n + 1;
                    else if (node.Edges[n].Char > c)
                        nmax = n - 1;
                    else // equal
                        break;
                }
                if (nmin > nmax)
                    return false;
                accepting = node.Edges[n].Accepting;
                node = node.Edges[n].Node;
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
                if (!node.IsBlank())
                    _nodes.Remove(node);
                EdgeCount -= node.Edges.Length;
                for (int i = 0; i < node.Edges.Length; i++)
                    dereference(node.Edges[i].Node);
            }
        }

        private DawgNode addNew(string value, int from)
        {
            if (from == value.Length)
            {
                if (_ending == null)
                    _ending = new DawgNode(0);
                return _ending;
            }
            var hash = FnvHash(value, from);
            var n = _nodes.GetFirstInBucket(hash);
            while (n != null)
            {
                if (n.Hash == hash && n.MatchesOnly(value, from))
                    return n;
                n = n.HashNext;
            }

            var node = new DawgNode(1) { Hash = hash };
            node.Edges[0].Accepting = from == value.Length - 1;
            node.Edges[0].Char = value[from];
            node.Edges[0].Node = addNew(value, from + 1);
            node.Edges[0].Node.RefCount++;
            EdgeCount++;
            _nodes.Add(node);
            return node;
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
            var dummy = new DawgNode(0); // dummy node
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
                foreach (var e in curnode.Edges)
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
                Util.OptimWrite(stream, (uint) curnode.Edges.Length);
                foreach (var e in curnode.Edges)
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
            DawgNode dummy = new DawgNode(0);
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
                nodes[n] = new DawgNode(0);
            result._starting = nodes[Util.OptimRead(stream)];
            for (int n = 0; n < nodes.Length; n++)
            {
                nodes[n].Edges = new DawgEdge[Util.OptimRead(stream)];
                for (int i = 0; i < nodes[n].Edges.Length; i++)
                {
                    var characc = Util.OptimRead(stream);
                    nodes[n].Edges[i].Accepting = (characc & 1) != 0;
                    nodes[n].Edges[i].Char = charset[characc >> 1];
                    nodes[n].Edges[i].Node = nodes[Util.OptimRead(stream)];
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
