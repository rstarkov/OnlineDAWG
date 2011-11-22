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
        public bool Accepting;
    }

    class DawgNode
    {
        public DawgEdge[] Edges;
        public int RefCount;
        public uint Hash = 0;
        public DawgNode HashNext;

        private static DawgEdge[] _edgesEmpty = new DawgEdge[0];
        public DawgNode(int blanks)
        {
            Edges = (blanks <= 0) ? _edgesEmpty : new DawgEdge[blanks];
        }

        public bool MatchesOnly(string value, int from)
        {
            var node = this;
            for (; from < value.Length; from++)
            {
                if (node.Edges.Length != 1) return false;
                if (node.Edges[0].Char != value[from]) return false;
                if (node.Edges[0].Accepting != (from == value.Length - 1)) return false;
                node = node.Edges[0].Node;
            }
            return node.Edges.Length == 0;
        }

        public bool MatchesSame(DawgNode other)
        {
            if (this.Edges.Length != other.Edges.Length)
                return false;
            for (int i = 0; i < Edges.Length; i++)
                if (this.Edges[i].Char != other.Edges[i].Char || this.Edges[i].Accepting != other.Edges[i].Accepting)
                    return false;
            for (int i = 0; i < Edges.Length; i++)
                if (!this.Edges[i].Node.MatchesSame(other.Edges[i].Node))
                    return false;
            return true;
        }

        public bool MatchesSameWithAdd(string add, int from, DawgNode other)
        {
            if (from == add.Length)
                return MatchesSame(other);
            if (this.Edges.Length < other.Edges.Length - 1 || this.Edges.Length > other.Edges.Length)
                return false;

            char c = add[from];
            bool accepting = from == add.Length - 1;
            bool had = false;
            int t, o;
            for (t = o = 0; t < this.Edges.Length && o < other.Edges.Length; t++, o++)
            {
                if (other.Edges[o].Char == c)
                {
                    had = true;
                    if (this.Edges[t].Char == c)
                    {
                        if ((accepting || this.Edges[t].Accepting) != other.Edges[o].Accepting)
                            return false;
                        if (!this.Edges[t].Node.MatchesSameWithAdd(add, from + 1, other.Edges[o].Node))
                            return false;
                    }
                    else
                    {
                        if (accepting != other.Edges[o].Accepting)
                            return false;
                        if (!other.Edges[o].Node.MatchesOnly(add, from + 1))
                            return false;
                        t--;
                    }
                }
                else if (this.Edges[t].Char == c)
                    return false;
                else if (this.Edges[t].Char != other.Edges[o].Char)
                    return false;
                else if (this.Edges[t].Accepting != other.Edges[o].Accepting)
                    return false;
                else if (!this.Edges[t].Node.MatchesSame(other.Edges[o].Node))
                    return false;
            }
            if (!had)
            {
                if (t != this.Edges.Length || o != other.Edges.Length - 1 || c != other.Edges[o].Char || accepting != other.Edges[o].Accepting)
                    return false;
                if (!other.Edges[o].Node.MatchesOnly(add, from + 1))
                    return false;
            }

            return true;
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
            for (int i = 0; i < Edges.Length; i++)
            {
                if (Edges[i].Accepting)
                    yield return prefix + Edges[i].Char;
                foreach (var suf in Edges[i].Node.suffixes(prefix + Edges[i].Char))
                    yield return suf;
            }
        }

        public void InsertEdgeAt(int pos)
        {
            var newEdges = new DawgEdge[Edges.Length + 1];
            Array.Copy(Edges, newEdges, pos);
            if (pos < Edges.Length)
                Array.Copy(Edges, pos, newEdges, pos + 1, Edges.Length - pos);
            Edges = newEdges;
        }
    }
}
