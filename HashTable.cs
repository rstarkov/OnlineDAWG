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
    class DawgHashTable : IEnumerable<int>
    {
        private int[] _table = new int[65536];
        private int _mask = 0xFFFF;
        private int _threshold = 65536 * 3;
        private DawgNodeList _nodes;
        public int Count { get; private set; }

        public DawgHashTable(DawgNodeList nodes)
        {
            _nodes = nodes;
        }

        /// <summary>
        /// Adds the node to the hash table. No check is made for duplicate nodes; passing in duplicates will
        /// result in unspecified behaviour.
        /// </summary>
        public void Add(int value)
        {
            var valueI = _nodes[value];
            int index = (int) (valueI.Hash & _mask);
            valueI.Next = _table[index];
            _table[index] = value;
            Count++;
            if (Count > _threshold)
                grow();
        }

        /// <summary>
        /// Removes the specified node from the hash table.
        /// </summary>
        public void Remove(int value)
        {
            var valueI = _nodes[value];
            int index = (int) (valueI.Hash & _mask);
            if (_table[index] == value)
            {
                Count--;
                _table[index] = _nodes[_table[index]].Next;
                return;
            }
            var node = _table[index];
            var nodeI = _nodes[node];
            while (node != 0)
            {
                if (nodeI.Next == value)
                {
                    Count--;
                    nodeI.Next = _nodes[nodeI.Next].Next;
                    return;
                }
                node = nodeI.Next;
                nodeI = _nodes[node];
            }
        }

        /// <summary>Doubles the number of buckets in the hash table.</summary>
        private void grow()
        {
            _mask = ((_mask + 1) << 1) - 1;
            _threshold *= 2;
            var old = _table;
            _table = new int[old.Length * 2];
            // Redistribute all the nodes into the new buckets
            for (int o = 0; o < old.Length; o++)
            {
                var n = old[o];
                while (n != 0)
                {
                    var nI = _nodes[n];
                    var next = nI.Next;
                    int index = (int) (nI.Hash & _mask);
                    nI.Next = _table[index];
                    _table[index] = n;
                    n = next;
                }
            }
        }

        /// <summary>
        /// Enumerates all DAWG nodes that have the specified hash.
        /// </summary>
        public IEnumerable<int> GetValuesExact(uint hash)
        {
            var node = GetFirstInBucket(hash);
            while (node != 0)
            {
                if (_nodes[node].Hash == hash)
                    yield return node;
                node = _nodes[node].Next;
            }
        }

        /// <summary>
        /// Returns a list that contains all nodes with the specified hash, and potentially other nodes.
        /// This list must not be modified as it holds the actual data for the hash table.
        /// </summary>
        public int GetFirstInBucket(uint hash)
        {
            int index = (int) (hash & _mask);
            return _table[index];
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (int h = 0; h < _table.Length; h++)
            {
                var node = _table[h];
                while (node != 0)
                {
                    yield return node;
                    node = _nodes[node].Next;
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public long MemoryUsage { get { return System.IntPtr.Size * (_table.Length + 3 + 4); } }
    }
}
