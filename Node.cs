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
using System.Linq;

namespace OnlineDAWG
{
    struct DawgEdge
    {
        public int Node;
        public char Char;
    }

    struct DawgNode
    {
        public DawgEdge[] Edges;
        public bool Accepting;
        public int RefCount;
        public uint Hash;
        public int Next;

        //public override string ToString()
        //{
        //    return "Node: " + string.Join("|", Suffixes().Select(s => s == "" ? "<acc>" : s).ToArray());
        //}

        //public IEnumerable<string> Suffixes()
        //{
        //    return suffixes("");
        //}

        //private IEnumerable<string> suffixes(string prefix)
        //{
        //    if (Accepting)
        //        yield return prefix;
        //    for (int i = 0; i < Edges.Length; i++)
        //        foreach (var suf in Edges[i].Node.suffixes(prefix + Edges[i].Char))
        //            yield return suf;
        //}
    }

    struct DawgNodeItem
    {
        private DawgNode[] _list;
        private int _index;

        public DawgNodeItem(DawgNode[] list, int index)
        {
            _list = list;
            _index = index;
        }

        public DawgEdge[] Edges { get { return _list[_index].Edges; } }
        public bool Accepting { get { return _list[_index].Accepting; } set { _list[_index].Accepting = value; } }
        public int RefCount { get { return _list[_index].RefCount; } }
        public uint Hash { get { return _list[_index].Hash; } set { _list[_index].Hash = value; } }
        public int Next { get { return _list[_index].Next; } set { _list[_index].Next = value; } }
        public bool IsBlank { get { return _list[_index].Edges.Length == 0; } }

        public void InsertBlankAt(int pos)
        {
            var newEdges = new DawgEdge[_list[_index].Edges.Length + 1];
            Array.Copy(_list[_index].Edges, newEdges, pos);
            if (pos < _list[_index].Edges.Length)
                Array.Copy(_list[_index].Edges, pos, newEdges, pos + 1, _list[_index].Edges.Length - pos);
            _list[_index].Edges = newEdges;
        }

        private static DawgEdge[] _edgesEmpty = new DawgEdge[0];
        public void InitEdges(int edges)
        {
            _list[_index].Edges = (edges <= 0) ? _edgesEmpty : new DawgEdge[edges];
        }

        public void IncRefCount() { _list[_index].RefCount++; }
        public void DecRefCount() { _list[_index].RefCount--; }
    }
}
