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
        private int _starting;
        private int _ending;
        private DawgNodeList _nodes = new DawgNodeList();
        private DawgHashTable _hashtable;

        /// <summary>Gets the number of distinct "words" (values added with <see cref="Add"/>) that this graph accepts.</summary>
        public int WordCount { get; private set; }
        /// <summary>Gets the number of nodes in the graph.</summary>
        public int NodeCount { get { return _hashtable.Count + 2; } }
        /// <summary>Gets the number of edges in the graph.</summary>
        public int EdgeCount { get; private set; }
        /// <summary>Gets the approximate number of bytes consumed by this graph.</summary>
        public long MemoryUsage { get { return (8 * IntPtr.Size + 2 * 4) * (long) NodeCount + (2 * IntPtr.Size) * (long) EdgeCount + _hashtable.MemoryUsage; } }

        public DawgGraph()
        {
            _hashtable = new DawgHashTable(_nodes);
            _starting = _nodes.Alloc();
            _nodes[_starting].InitEdges(0);
            _ending = _nodes.Alloc();
            var eI = _nodes[_ending];
            eI.InitEdges(0);
            eI.Accepting = true;
            eI.Hash = 2166136261;
        }

        /// <summary>
        /// Adds the specified value to the DAWG. This method *will* result in corruption if this value
        /// is already present; filter out any duplicates using the <see cref="Contains"/> method.
        /// </summary>
        public void Add(string value)
        {
            var node = _starting;
            var nodeI = _nodes[node];
            uint nexthash = 0;
            for (int index = 1; index <= value.Length + 1; index++)
            {
                if (node != _starting)
                {
                    if (!nodeI.IsBlank)
                        _hashtable.Remove(node);
                    nodeI.Hash ^= nexthash;
                    _hashtable.Add(node);
                }

                if (node == _ending)
                {
                    _ending = _nodes.Alloc();
                    var eI = _nodes[_ending];
                    eI.InitEdges(0);
                    eI.Accepting = true;
                    eI.Hash = 2166136261;
                }

                if (index - 1 == value.Length)
                {
                    nodeI.Accepting = true;
                    break;
                }

                char c = value[index - 1];

                // Find the outgoing edge index, or insert it if not there yet
                int n = -1;
                int nmin = 0, nmax = nodeI.Edges.Length - 1;
                while (nmin <= nmax)
                {
                    n = (nmin + nmax) >> 1;
                    if (nodeI.Edges[n].Char < c)
                        nmin = n + 1;
                    else if (nodeI.Edges[n].Char > c)
                        nmax = n - 1;
                    else // equal
                        break;
                }
                if (nmin > nmax)
                {
                    n = nmin;
                    nodeI.InsertBlankAt(n);
                    nodeI.Edges[n].Char = c;
                    nodeI.Edges[n].Node = addNew(value, index);
                    _nodes[nodeI.Edges[n].Node].IncRefCount();
                    EdgeCount++;
                    break;
                }

                nexthash = FnvHash(value, index);
                bool done = false;
                var wantedhash = _nodes[nodeI.Edges[n].Node].Hash ^ nexthash;
                var candidate = _hashtable.GetFirstInBucket(wantedhash);
                while (candidate != 0)
                {
                    var candidateI = _nodes[candidate];
                    if (candidateI.Hash == wantedhash)
                    {
                        if (matchesSameWithAdd(_nodes[nodeI.Edges[n].Node], value, index, candidateI))
                        {
                            var old = nodeI.Edges[n].Node;
                            nodeI.Edges[n].Node = candidate;
                            _nodes[nodeI.Edges[n].Node].IncRefCount();
                            dereference(old);
                            done = true;
                            break;
                        }
                    }
                    candidate = candidateI.Next;
                }
                if (done)
                    break;

                if (_nodes[nodeI.Edges[n].Node].RefCount > 1)
                {
                    var old = nodeI.Edges[n].Node;
                    var oldI = _nodes[old];
                    nodeI.Edges[n].Node = _nodes.Alloc();
                    var newI = _nodes[nodeI.Edges[n].Node];
                    newI.InitEdges(oldI.Edges.Length);
                    newI.Hash = oldI.Hash;
                    newI.Accepting = oldI.Accepting;
                    for (int i = 0; i < oldI.Edges.Length; i++)
                    {
                        newI.Edges[i].Char = oldI.Edges[i].Char;
                        newI.Edges[i].Node = oldI.Edges[i].Node;
                        _nodes[newI.Edges[i].Node].IncRefCount();
                    }
                    EdgeCount += oldI.Edges.Length;
                    dereference(old);
                    newI.IncRefCount();
                }

                node = nodeI.Edges[n].Node;
                nodeI = _nodes[node];
            }

            WordCount++;
        }

        /// <summary>
        /// Queries the DAWG to see if it contains the specified value.
        /// </summary>
        public bool Contains(string value)
        {
            var node = _starting;
            var nodeI = _nodes[node];
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];

                int n = -1;
                int nmin = 0, nmax = nodeI.Edges.Length - 1;
                while (nmin <= nmax)
                {
                    n = (nmin + nmax) >> 1;
                    if (nodeI.Edges[n].Char < c)
                        nmin = n + 1;
                    else if (nodeI.Edges[n].Char > c)
                        nmax = n - 1;
                    else // equal
                        break;
                }
                if (nmin > nmax)
                    return false;
                node = nodeI.Edges[n].Node;
                nodeI = _nodes[node];
            }
            return nodeI.Accepting;
        }

        private void dereference(int node)
        {
            var nodeI = _nodes[node];
            nodeI.DecRefCount();
            if (nodeI.RefCount < 0)
                throw new Exception("836");
            if (nodeI.RefCount == 0)
            {
                if (!nodeI.IsBlank)
                    _hashtable.Remove(node);
                EdgeCount -= nodeI.Edges.Length;
                for (int i = 0; i < nodeI.Edges.Length; i++)
                    dereference(nodeI.Edges[i].Node);
                _nodes.Dealloc(node);
            }
        }

        private int addNew(string value, int from)
        {
            if (from == value.Length)
                return _ending;
            var hash = FnvHash(value, from);
            var n = _hashtable.GetFirstInBucket(hash);
            while (n != 0)
            {
                var nI = _nodes[n];
                if (nI.Hash == hash && matchesOnly(nI, value, from))
                    return n;
                n = nI.Next;
            }

            var node = _nodes.Alloc();
            var nodeI = _nodes[node];
            nodeI.InitEdges(1);
            nodeI.Hash = hash;
            nodeI.Edges[0].Char = value[from];
            nodeI.Edges[0].Node = addNew(value, from + 1);
            _nodes[nodeI.Edges[0].Node].IncRefCount();
            EdgeCount++;
            _hashtable.Add(node);
            return node;
        }

        private bool matchesOnly(DawgNodeItem nodeI, string value, int from)
        {
            for (; from < value.Length; from++)
            {
                if (nodeI.Edges.Length != 1) return false;
                if (nodeI.Edges[0].Char != value[from]) return false;
                nodeI = _nodes[nodeI.Edges[0].Node];
            }
            return nodeI.Accepting && nodeI.IsBlank;
        }

        private bool matchesSame(DawgNodeItem thisI, DawgNodeItem otherI)
        {
            if (thisI.Accepting != otherI.Accepting)
                return false;
            return matchesHelper(thisI, otherI);
        }

        private bool matchesSameWithAdd(DawgNodeItem thisI, string add, int from, DawgNodeItem otherI)
        {
            if ((thisI.Accepting || from == add.Length) != otherI.Accepting)
                return false;
            if (from == add.Length)
                return matchesHelper(thisI, otherI);
            if (thisI.Edges.Length < otherI.Edges.Length - 1 || thisI.Edges.Length > otherI.Edges.Length)
                return false;

            // Shallow test to make sure the characters match
            char c = add[from];
            bool had = false;
            int t, o;
            for (t = o = 0; t < thisI.Edges.Length && o < otherI.Edges.Length; t++, o++)
            {
                if (otherI.Edges[o].Char == c)
                {
                    had = true;
                    if (thisI.Edges[t].Char != c)
                        t--;
                }
                else if (thisI.Edges[t].Char == c)
                    return false;
                else if (thisI.Edges[t].Char != otherI.Edges[o].Char)
                    return false;
            }
            if (!had && (t != thisI.Edges.Length || o != otherI.Edges.Length - 1 || c != otherI.Edges[o].Char))
                return false;

            // Deep test to ensure that the nodes match
            had = false;
            for (t = o = 0; t < thisI.Edges.Length && o < otherI.Edges.Length; t++, o++)
            {
                if (otherI.Edges[o].Char == c)
                {
                    had = true;
                    if (thisI.Edges[t].Char == c)
                    {
                        if (!matchesSameWithAdd(_nodes[thisI.Edges[t].Node], add, from + 1, _nodes[otherI.Edges[o].Node]))
                            return false;
                    }
                    else
                    {
                        if (!matchesOnly(_nodes[otherI.Edges[o].Node], add, from + 1))
                            return false;
                        t--;
                    }
                }
                else if (thisI.Edges[t].Char == otherI.Edges[o].Char)
                    if (!matchesSame(_nodes[thisI.Edges[t].Node], _nodes[otherI.Edges[o].Node]))
                        return false;
            }
            if (!had)
                if (!matchesOnly(_nodes[otherI.Edges[o].Node], add, from + 1))
                    return false;

            return true;
        }

        private bool matchesHelper(DawgNodeItem thisI, DawgNodeItem otherI)
        {
            if (thisI.Edges.Length != otherI.Edges.Length)
                return false;
            for (int i = 0; i < thisI.Edges.Length; i++)
                if (thisI.Edges[i].Char != otherI.Edges[i].Char)
                    return false;
            for (int i = 0; i < thisI.Edges.Length; i++)
                if (!matchesSame(_nodes[thisI.Edges[i].Node], _nodes[otherI.Edges[i].Node]))
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

#if false
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
                curnode.Next = n;
                curnode = n;
            }
            // Merge sort them by decreasing RefCount
            var first = mergeSort(dummy.Next, NodeCount - 1);
            // Prepend the ending node, because all links to it are always accepting (-0 = 0) and its RefCount is usually large.
            _ending.Next = first;
            first = _ending;
            // Assign integer id's and establish char frequencies
            curnode = first;
            var chars = new Dictionary<char, int>();
            for (int id = 0; curnode != null; id++, curnode = curnode.Next)
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
                curnode = curnode.Next;
            }
        }

        private int mergeSort(int ifirst, int count)
        {
            if (count <= 1)
                return ifirst;
            // Divide
            int count1 = count / 2;
            int count2 = count - count1;
            var first1 = ifirst;
            var first2 = ifirst;
            for (int i = 0; i < count1; i++)
                first2 = first2.Next;
            var next = first2;
            for (int i = 0; i < count2; i++)
                next = next.Next;
            // Recurse
            first1 = mergeSort(first1, count1);
            first2 = mergeSort(first2, count2);
            // Merge
            int idummy = new DawgNode(0);
            int icur = idummy;
            while (count1 > 0 || count2 > 0)
            {
                if ((count2 <= 0) || (count1 > 0 && first1.RefCount >= first2.RefCount))
                {
                    icur.Next = first1;
                    icur = icur.Next;
                    first1 = first1.Next;
                    count1--;
                }
                else
                {
                    icur.Next = first2;
                    icur = icur.Next;
                    first2 = first2.Next;
                    count2--;
                }
            }
            icur.Next = next;
            return idummy.Next;
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
#endif

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
            yield return _nodes.GetCopy(_starting);
            foreach (var node in _hashtable)
                yield return _nodes.GetCopy(node);
            yield return _nodes.GetCopy(_ending);
        }
    }
}
