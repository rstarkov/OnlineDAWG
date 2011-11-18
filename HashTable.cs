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
        private LinkedList<DawgNode>[] _table = new LinkedList<DawgNode>[65536];
        public int Count { get; private set; }

        /// <summary>
        /// Adds the node to the hash table. No check is made for duplicate nodes; passing in duplicates will
        /// result in unspecified behaviour.
        /// </summary>
        public void Add(DawgNode value)
        {
            int index = (int) ((value.Hash ^ (value.Hash >> 16)) & 0xFFFF);
            if (_table[index] == null)
                _table[index] = new LinkedList<DawgNode>();
            _table[index].AddFirst(value);
            Count++;
        }

        /// <summary>
        /// Removes the specified node from the hash table.
        /// </summary>
        public void Remove(DawgNode value)
        {
            int index = (int) ((value.Hash ^ (value.Hash >> 16)) & 0xFFFF);
            if (_table[index] == null)
                return;
            if (_table[index].Remove(value))
                Count--;
            if (_table[index].Count == 0)
                _table[index] = null;
        }

        private static LinkedList<DawgNode> _empty = new LinkedList<DawgNode>();

        /// <summary>
        /// Enumerates all DAWG nodes that have the specified hash.
        /// </summary>
        public IEnumerable<DawgNode> GetValuesExact(uint hash)
        {
            return GetValuesApprox(hash).Where(n => n.Hash == hash);
        }

        /// <summary>
        /// Returns a list that contains all nodes with the specified hash, and potentially other nodes.
        /// This list must not be modified as it holds the actual data for the hash table.
        /// </summary>
        public LinkedList<DawgNode> GetValuesApprox(uint hash)
        {
            int index = (int) ((hash ^ (hash >> 16)) & 0xFFFF);
            return _table[index] == null ? _empty : _table[index];
        }

        public IEnumerator<DawgNode> GetEnumerator() { return _table.Where(r => r != null).SelectMany(r => r).GetEnumerator(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
}
