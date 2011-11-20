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
    class DawgHashTable : IEnumerable<DawgNode>
    {
        private DawgNode[] _table = new DawgNode[65536];
        private int _mask = 0xFFFF;
        private int _threshold = 65536 * 3;
        public int Count { get; private set; }

        /// <summary>
        /// Adds the node to the hash table. No check is made for duplicate nodes; passing in duplicates will
        /// result in unspecified behaviour.
        /// </summary>
        public void Add(DawgNode value)
        {
            int index = (int) (value.Hash & _mask);
            value.HashNext = _table[index];
            _table[index] = value;
            Count++;
            if (Count > _threshold)
                grow();
        }

        /// <summary>
        /// Removes the specified node from the hash table.
        /// </summary>
        public void Remove(DawgNode value)
        {
            int index = (int) (value.Hash & _mask);
            if (_table[index] == value)
            {
                Count--;
                _table[index] = _table[index].HashNext;
                return;
            }
            var node = _table[index];
            while (node != null)
            {
                if (node.HashNext == value)
                {
                    Count--;
                    node.HashNext = node.HashNext.HashNext;
                    return;
                }
                node = node.HashNext;
            }
        }

        /// <summary>Doubles the number of buckets in the hash table.</summary>
        private void grow()
        {
            _mask = ((_mask + 1) << 1) - 1;
            _threshold *= 2;
            var old = _table;
            _table = new DawgNode[old.Length * 2];
            // Redistribute all the nodes into the new buckets
            for (int o = 0; o < old.Length; o++)
            {
                var n = old[o];
                while (n != null)
                {
                    var next = n.HashNext;
                    int index = (int) (n.Hash & _mask);
                    n.HashNext = _table[index];
                    _table[index] = n;
                    n = next;
                }
                old[o] = null;
            }
        }

        /// <summary>
        /// Enumerates all DAWG nodes that have the specified hash.
        /// </summary>
        public IEnumerable<DawgNode> GetValuesExact(uint hash)
        {
            var node = GetFirstInBucket(hash);
            while (node != null)
            {
                if (node.Hash == hash)
                    yield return node;
                node = node.HashNext;
            }
        }

        /// <summary>
        /// Returns a list that contains all nodes with the specified hash, and potentially other nodes.
        /// This list must not be modified as it holds the actual data for the hash table.
        /// </summary>
        public DawgNode GetFirstInBucket(uint hash)
        {
            int index = (int) (hash & _mask);
            return _table[index];
        }

        public IEnumerator<DawgNode> GetEnumerator()
        {
            for (int h = 0; h < _table.Length; h++)
            {
                var node = _table[h];
                while (node != null)
                {
                    yield return node;
                    node = node.HashNext;
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public long MemoryUsage { get { return System.IntPtr.Size * (_table.Length + 3 + 4); } }
    }
}
