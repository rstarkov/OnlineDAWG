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
        private DawgNode _ending = new DawgNode(0) { Accepting = true, Hash = 2166136261 };
        private DawgHashTable _hashtable = new DawgHashTable();

        /// <summary>Gets the number of distinct "words" (values added with <see cref="Add"/>) that this graph accepts.</summary>
        public int WordCount { get; private set; }
        /// <summary>Gets the number of nodes in the graph.</summary>
        public int NodeCount { get { return _hashtable.Count + 2; } }
        /// <summary>Gets the number of edges in the graph.</summary>
        public int EdgeCount { get; private set; }
        /// <summary>Gets the approximate number of bytes consumed by this graph.</summary>
        public long MemoryUsage { get { return (8 * IntPtr.Size + 2 * 4) * (long) NodeCount + (2 * IntPtr.Size) * (long) EdgeCount + _hashtable.MemoryUsage; } }

        /// <summary>
        /// Adds the specified value to the DAWG. This method *will* result in corruption if this value
        /// is already present; filter out any duplicates using the <see cref="Contains"/> method.
        /// </summary>
        public void Add(string value)
        {
            var node = _starting;
            uint nexthash = 0;
            for (int index = 1; index <= value.Length + 1; index++)
            {
                if (node != _starting)
                {
                    if (!node.IsBlank())
                        _hashtable.Remove(node);
                    node.Hash ^= nexthash;
                    _hashtable.Add(node);
                }

                if (node == _ending)
                    _ending = new DawgNode(0) { Accepting = true, Hash = 2166136261 };

                if (index - 1 == value.Length)
                {
                    node.Accepting = true;
                    break;
                }

                char c = value[index - 1];

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
                if (nmin > nmax)
                {
                    n = nmin;
                    node.InsertBlankAt(n);
                    node.Edges[n].Char = c;
                    node.Edges[n].Node = addNew(value, index);
                    node.Edges[n].Node.RefCount++;
                    EdgeCount++;
                    break;
                }

                nexthash = FnvHash(value, index);
                bool done = false;
                var wantedhash = node.Edges[n].Node.Hash ^ nexthash;
                var candidate = _hashtable.GetFirstInBucket(wantedhash);
                while (candidate != null)
                {
                    if (candidate.Hash == wantedhash)
                    {
                        if (matchesSameWithAdd(node.Edges[n].Node, value, index, candidate))
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

                if (node.Edges[n].Node.RefCount > 1)
                {
                    var old = node.Edges[n].Node;
                    var newn = node.Edges[n].Node = new DawgNode(old.Edges.Length) { Hash = old.Hash, Accepting = old.Accepting };
                    for (int i = 0; i < old.Edges.Length; i++)
                    {
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
                node = node.Edges[n].Node;
            }
            return node.Accepting;
        }

        private void dereference(DawgNode node)
        {
            node.RefCount--;
            if (node.RefCount < 0)
                throw new Exception("836");
            if (node.RefCount == 0)
            {
                if (!node.IsBlank())
                    _hashtable.Remove(node);
                EdgeCount -= node.Edges.Length;
                for (int i = 0; i < node.Edges.Length; i++)
                    dereference(node.Edges[i].Node);
            }
        }

        private DawgNode addNew(string value, int from)
        {
            if (from == value.Length)
                return _ending;
            var hash = FnvHash(value, from);
            var n = _hashtable.GetFirstInBucket(hash);
            while (n != null)
            {
                if (n.Hash == hash && matchesOnly(n, value, from))
                    return n;
                n = n.HashNext;
            }

            var node = new DawgNode(1) { Hash = hash };
            node.Edges[0].Char = value[from];
            node.Edges[0].Node = addNew(value, from + 1);
            node.Edges[0].Node.RefCount++;
            EdgeCount++;
            _hashtable.Add(node);
            return node;
        }

        private static bool matchesOnly(DawgNode thisNode, string value, int from)
        {
            var node = thisNode;
            for (; from < value.Length; from++)
            {
                if (node.Edges.Length != 1) return false;
                if (node.Edges[0].Char != value[from]) return false;
                node = node.Edges[0].Node;
            }
            return node.Accepting && node.IsBlank();
        }

        private static bool matchesSame(DawgNode thisNode, DawgNode otherNode)
        {
            if (thisNode.Accepting != otherNode.Accepting)
                return false;
            return matchesHelper(thisNode, otherNode);
        }

        private static bool matchesSameWithAdd(DawgNode thisNode, string add, int from, DawgNode otherNode)
        {
            if ((thisNode.Accepting || from == add.Length) != otherNode.Accepting)
                return false;
            if (from == add.Length)
                return matchesHelper(thisNode, otherNode);
            if (thisNode.Edges.Length < otherNode.Edges.Length - 1 || thisNode.Edges.Length > otherNode.Edges.Length)
                return false;

            // Shallow test to make sure the characters match
            char c = add[from];
            bool had = false;
            int t, o;
            for (t = o = 0; t < thisNode.Edges.Length && o < otherNode.Edges.Length; t++, o++)
            {
                if (otherNode.Edges[o].Char == c)
                {
                    had = true;
                    if (thisNode.Edges[t].Char != c)
                        t--;
                }
                else if (thisNode.Edges[t].Char == c)
                    return false;
                else if (thisNode.Edges[t].Char != otherNode.Edges[o].Char)
                    return false;
            }
            if (!had && (t != thisNode.Edges.Length || o != otherNode.Edges.Length - 1 || c != otherNode.Edges[o].Char))
                return false;

            // Deep test to ensure that the nodes match
            had = false;
            for (t = o = 0; t < thisNode.Edges.Length && o < otherNode.Edges.Length; t++, o++)
            {
                if (otherNode.Edges[o].Char == c)
                {
                    had = true;
                    if (thisNode.Edges[t].Char == c)
                    {
                        if (!matchesSameWithAdd(thisNode.Edges[t].Node, add, from + 1, otherNode.Edges[o].Node))
                            return false;
                    }
                    else
                    {
                        if (!matchesOnly(otherNode.Edges[o].Node, add, from + 1))
                            return false;
                        t--;
                    }
                }
                else if (thisNode.Edges[t].Char == otherNode.Edges[o].Char)
                    if (!matchesSame(thisNode.Edges[t].Node, otherNode.Edges[o].Node))
                        return false;
            }
            if (!had)
                if (!matchesOnly(otherNode.Edges[o].Node, add, from + 1))
                    return false;

            return true;
        }

        private static bool matchesHelper(DawgNode thisNode, DawgNode otherNode)
        {
            if (thisNode.Edges.Length != otherNode.Edges.Length)
                return false;
            for (int i = 0; i < thisNode.Edges.Length; i++)
                if (thisNode.Edges[i].Char != otherNode.Edges[i].Char)
                    return false;
            for (int i = 0; i < thisNode.Edges.Length; i++)
                if (!matchesSame(thisNode.Edges[i].Node, otherNode.Edges[i].Node))
                    return false;
            return true;
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
                if (n == _ending) // the ending node is handled separately
                    continue;
                curnode.HashNext = n;
                curnode = n;
            }
            // Merge sort them by decreasing RefCount
            var first = mergeSort(dummy.HashNext, NodeCount - 1);
            // Prepend the ending node, because all links to it are always accepting (-0 = 0) and its RefCount is usually large.
            _ending.HashNext = first;
            first = _ending;
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
            optimWrite(stream, (uint) charset.Length);
            foreach (var c in charset)
                optimWrite(stream, c);
            optimWrite(stream, (uint) NodeCount);
            optimWrite(stream, _starting.Hash);
            // Write out nodes
            curnode = first;
            while (curnode != null)
            {
                optimWrite(stream, (uint) (curnode.Edges.Length * 2 + (curnode.Accepting ? 1 : 0)));
                foreach (var e in curnode.Edges)
                {
                    int f = 0;
                    for (; f < charset.Length; f++)
                        if (charset[f] == e.Char)
                            break;
                    optimWrite(stream, (uint) f);
                }
                foreach (var e in curnode.Edges)
                    optimWrite(stream, e.Node.Hash);
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
            fillBuffer(stream, buf, 0, 6);
            if (Encoding.UTF8.GetString(buf, 0, 6) != "DAWG.1")
                throw new InvalidDataException();
            var result = new DawgGraph();

            var charset = new char[optimRead(stream)];
            for (int i = 0; i < charset.Length; i++)
                charset[i] = (char) optimRead(stream);

            var nodes = new DawgNode[optimRead(stream)];
            for (int n = 0; n < nodes.Length; n++)
                nodes[n] = new DawgNode(0);
            result._starting = nodes[optimRead(stream)];
            for (int n = 0; n < nodes.Length; n++)
            {
                uint acclen = optimRead(stream);
                nodes[n].Edges = new DawgEdge[acclen >> 1];
                nodes[n].Accepting = (acclen & 1) != 0;
                for (int i = 0; i < nodes[n].Edges.Length; i++)
                    nodes[n].Edges[i].Char = charset[optimRead(stream)];
                for (int i = 0; i < nodes[n].Edges.Length; i++)
                    nodes[n].Edges[i].Node = nodes[optimRead(stream)];
            }
            return result;
        }

        private static int fillBuffer(Stream stream, byte[] buffer, int offset, int length)
        {
            int totalRead = 0;
            while (length > 0)
            {
                var read = stream.Read(buffer, offset, length);
                if (read == 0)
                    return totalRead;
                offset += read;
                length -= read;
                totalRead += read;
            }
            return totalRead;
        }

        private static void optimWrite(Stream stream, uint val)
        {
            while (val >= 128)
            {
                stream.WriteByte((byte) (val | 128));
                val >>= 7;
            }
            stream.WriteByte((byte) val);
        }

        private static uint optimRead(Stream stream)
        {
            byte b = 255;
            int shifts = 0;
            uint res = 0;
            while (b > 127)
            {
                int read = stream.ReadByte();
                if (read < 0) throw new InvalidOperationException("Unexpected end of stream (#25753)");
                b = (byte) read;
                res = res | ((uint) (b & 127) << shifts);
                shifts += 7;
            }
            return res;
        }

        internal IEnumerable<DawgNode> GetNodes()
        {
            yield return _starting;
            foreach (var node in _hashtable)
                yield return node;
            yield return _ending;
        }
    }
}
