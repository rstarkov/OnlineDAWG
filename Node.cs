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
        public DawgNode Node;
        public char Char;
    }

    class DawgNode
    {
        public DawgEdge[] Edges;
        public bool Accepting;
        public int RefCount;
        public uint Hash = 0;
        public DawgNode HashNext;

        private static DawgEdge[] _edgesEmpty = new DawgEdge[0];
        public DawgNode(int blanks)
        {
            Edges = (blanks <= 0) ? _edgesEmpty : new DawgEdge[blanks];
        }

        public bool IsBlank()
        {
            return Edges.Length == 0;
        }

        public override string ToString()
        {
            return "Node: " + string.Join("|", Suffixes().Select(s => s == "" ? "<acc>" : s).ToArray());
        }

        public IEnumerable<string> Suffixes()
        {
            return suffixes("");
        }

        private IEnumerable<string> suffixes(string prefix)
        {
            if (Accepting)
                yield return prefix;
            for (int i = 0; i < Edges.Length; i++)
                foreach (var suf in Edges[i].Node.suffixes(prefix + Edges[i].Char))
                    yield return suf;
        }

        public void InsertBlankAt(int pos)
        {
            var newEdges = new DawgEdge[Edges.Length + 1];
            Array.Copy(Edges, newEdges, pos);
            if (pos < Edges.Length)
                Array.Copy(Edges, pos, newEdges, pos + 1, Edges.Length - pos);
            Edges = newEdges;
        }
    }
}
