// Copyright 2011 Roman Starkov
// This file is part of OnlineDAWG: https://bitbucket.org/rstarkov/onlinedawg
///
// OnlineDAWG can be redistributed and/or modified under the terms of the GNU General Public License as published by the
// Free Software Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied
// warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

using System.Collections.Generic;
using System.Linq;

namespace OnlineDAWG
{
    /// <summary>
    /// Holds DAWG nodes and retrieves them by the hash, exposing direct access to the underlying buckets
    /// for maximum efficiency during enumeration.
    /// </summary>
    class DawgHashTable : IEnumerable<DawgNodeIndex>
    {
        private DawgNodeIndex[] _table = new DawgNodeIndex[65536];
        private int _mask = 0xFFFF;
        private int _threshold = 65536 * 3;
        private DawgGraph _graph;

        public int Count { get; private set; }

        public DawgHashTable(DawgGraph graph)
        {
            _graph = graph;
        }

        /// <summary>
        /// Adds the node to the hash table. No check is made for duplicate nodes; passing in duplicates will
        /// result in unspecified behaviour.
        /// </summary>
        public void Add(DawgNodeIndex value)
        {
            int index = (int) (_graph.GetNodeHash(value) & _mask);
            _graph.SetNodeHashNext(value, _table[index]);
            _table[index] = value;
            Count++;
            if (Count > _threshold)
                grow();
        }

        /// <summary>
        /// Removes the specified node from the hash table.
        /// </summary>
        public void Remove(DawgNodeIndex value)
        {
            int index = (int) (_graph.GetNodeHash(value) & _mask);
            if (_table[index] == value)
            {
                Count--;
                _table[index] = _graph.GetNodeHashNext(_table[index]);
                return;
            }
            var node = _table[index];
            while (node != DawgNodeIndex.Null)
            {
                if (_graph.GetNodeHashNext(node) == value)
                {
                    Count--;
                    _graph.SetNodeHashNext(node, _graph.GetNodeHashNext(_graph.GetNodeHashNext(node)));
                    return;
                }
                node = _graph.GetNodeHashNext(node);
            }
        }

        /// <summary>Doubles the number of buckets in the hash table.</summary>
        private void grow()
        {
            _mask = ((_mask + 1) << 1) - 1;
            _threshold *= 2;
            var old = _table;
            _table = new DawgNodeIndex[old.Length * 2];
            // Redistribute all the nodes into the new buckets
            for (int o = 0; o < old.Length; o++)
            {
                var n = old[o];
                while (n != DawgNodeIndex.Null)
                {
                    var next = _graph.GetNodeHashNext(n);
                    int index = (int) (_graph.GetNodeHash(n) & _mask);
                    _graph.SetNodeHashNext(n, _table[index]);
                    _table[index] = n;
                    n = next;
                }
                old[o] = DawgNodeIndex.Null;
            }
        }

        /// <summary>
        /// Enumerates all DAWG nodes that have the specified hash.
        /// </summary>
        public IEnumerable<DawgNodeIndex> GetValuesExact(uint hash)
        {
            var node = GetFirstInBucket(hash);
            while (node != DawgNodeIndex.Null)
            {
                if (_graph.GetNodeHash(node) == hash)
                    yield return node;
                node = _graph.GetNodeHashNext(node);
            }
        }

        /// <summary>
        /// Returns a list that contains all nodes with the specified hash, and potentially other nodes.
        /// This list must not be modified as it holds the actual data for the hash table.
        /// </summary>
        public DawgNodeIndex GetFirstInBucket(uint hash)
        {
            int index = (int) (hash & _mask);
            return _table[index];
        }

        public IEnumerator<DawgNodeIndex> GetEnumerator()
        {
            for (int h = 0; h < _table.Length; h++)
            {
                var node = _table[h];
                while (node != DawgNodeIndex.Null)
                {
                    yield return node;
                    node = _graph.GetNodeHashNext(node);
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public long MemoryUsage { get { return System.IntPtr.Size * (_table.Length + 3 + 4); } }
    }
}
