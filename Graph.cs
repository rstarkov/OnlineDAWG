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
        private DawgHashTable _nodes = new DawgHashTable();

        /// <summary>Gets the number of distinct "words" (values added with <see cref="Add"/>) that this graph accepts.</summary>
        public int WordCount { get; private set; }
        /// <summary>Gets the number of nodes in the graph.</summary>
        public int NodeCount { get { return _nodes.Count + 2; } }
        /// <summary>Gets the number of edges in the graph.</summary>
        public int EdgeCount { get { return GetNodes().Sum(n => n.Edges.Length); } }
        /// <summary>Gets the approximate number of bytes consumed by this graph.</summary>
        public long MemoryUsage { get { return (3 * IntPtr.Size + 6 * 4) * NodeCount + (IntPtr.Size + 4) * EdgeCount; } }

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
                        _nodes.Remove(node);
                    node.Hash ^= nexthash;
                    _nodes.Add(node);
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
                    break;
                }

                nexthash = FnvHash(value, index);
                bool done = false;
                var wantedhash = node.Edges[n].Node.Hash ^ nexthash;
                var candidates = _nodes.GetValuesApprox(wantedhash);
                var candidate = candidates.First;
                while (candidate != null)
                {
                    if (candidate.Value.Hash == wantedhash)
                    {
                        if (node.Edges[n].Node.MatchesSameWithAdd(value, index, candidate.Value))
                        {
                            var old = node.Edges[n].Node;
                            node.Edges[n].Node = candidate.Value;
                            node.Edges[n].Node.RefCount++;
                            dereference(old);
                            done = true;
                            break;
                        }
                    }
                    candidate = candidate.Next;
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
                    _nodes.Remove(node);
                for (int i = 0; i < node.Edges.Length; i++)
                    dereference(node.Edges[i].Node);
            }
        }

        private DawgNode addNew(string value, int from)
        {
            if (from == value.Length)
                return _ending;
            var hash = FnvHash(value, from);
            foreach (var n in _nodes.GetValuesApprox(hash))
                if (n.Hash == hash && n.MatchesOnly(value, from))
                    return n;

            var node = new DawgNode(1) { Hash = hash };
            node.Edges[0].Char = value[from];
            node.Edges[0].Node = addNew(value, from + 1);
            node.Edges[0].Node.RefCount++;
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
        /// Saves the DAWG in binary format to a file at the specified location.
        /// </summary>
        public void Save(string path)
        {
            using (var s = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                Save(s);
        }

        /// <summary>
        /// Saves the DAWG in binary format to the specified stream.
        /// </summary>
        public void Save(Stream stream)
        {
            var nodes = GetNodes().OrderByDescending(n => n.RefCount).ToArray();
            var chars = new Dictionary<char, int>();
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].Hash = (uint) i;
                foreach (var e in nodes[i].Edges)
                    if (chars.ContainsKey(e.Char))
                        chars[e.Char]++;
                    else
                        chars[e.Char] = 1;
            }
            var charset = chars.OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).ToArray();

            stream.Write(Encoding.UTF8.GetBytes("DAWG.1"), 0, 6);
            optimWrite(stream, (uint) charset.Length);
            foreach (var c in charset)
                optimWrite(stream, c);
            optimWrite(stream, (uint) nodes.Length);
            optimWrite(stream, _starting.Hash);
            foreach (var node in nodes)
            {
                optimWrite(stream, (uint) (node.Edges.Length * 2 + (node.Accepting ? 1 : 0)));
                foreach (var e in node.Edges)
                {
                    int f = 0;
                    for (; f < charset.Length; f++)
                        if (charset[f] == e.Char)
                            break;
                    optimWrite(stream, (uint) f);
                }
                foreach (var e in node.Edges)
                    optimWrite(stream, e.Node.Hash);
            }
        }

        /// <summary>
        /// Loads the DAWG from the specified stream, assuming it was saved by <see cref="Save"/>.
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

        internal DawgNode[] GetNodes()
        {
            var allnodes = new HashSet<DawgNode>();
            var queue = new Queue<DawgNode>();
            queue.Enqueue(_starting);
            while (queue.Any())
            {
                var n = queue.Dequeue();
                if (allnodes.Contains(n))
                    continue;
                allnodes.Add(n);
                foreach (var e in n.Edges)
                    if (!allnodes.Contains(e.Node))
                        queue.Enqueue(e.Node);
            }
            return allnodes.ToArray();
        }
    }
}
