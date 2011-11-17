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
    public class DawgNode
    {
        public DawgNode[] Ns;
        public char[] Cs;
        public bool Accepting;
        public int RefCount;
        public uint Hash = 0;

        public DawgNode(int blanks)
        {
            Ns = new DawgNode[blanks];
            Cs = new char[blanks];
        }

        public bool IsBlank()
        {
            return Ns.Length == 0;
        }

        public bool MatchesOnly(string value, int from)
        {
            var node = this;
            for (; from < value.Length; from++)
            {
                if (node.Ns.Length != 1) return false;
                if (node.Cs[0] != value[from]) return false;
                node = node.Ns[0];
            }
            return node.Accepting && node.IsBlank();
        }

        public bool MatchesSame(DawgNode other)
        {
            if (Accepting != other.Accepting)
                return false;
            return matchesHelper(other);
        }

        public bool MatchesSameWithAdd(string add, int from, DawgNode other)
        {
            if ((Accepting || from == add.Length) != other.Accepting)
                return false;
            if (from == add.Length)
                return matchesHelper(other);
            if (this.Ns.Length < other.Ns.Length - 1 || this.Ns.Length > other.Ns.Length)
                return false;

            // Shallow test to make sure the characters match
            char c = add[from];
            bool had = false;
            int t, o;
            for (t = o = 0; t < this.Cs.Length && o < other.Cs.Length; t++, o++)
            {
                if (other.Cs[o] == c)
                {
                    had = true;
                    if (this.Cs[t] != c)
                        t--;
                }
                else if (this.Cs[t] == c)
                    return false;
                else if (this.Cs[t] != other.Cs[o])
                    return false;
            }
            if (!had && (t != this.Cs.Length || o != other.Cs.Length - 1 || c != other.Cs[o]))
                return false;

            // Deep test to ensure that the nodes match
            had = false;
            for (t = o = 0; t < this.Cs.Length && o < other.Cs.Length; t++, o++)
            {
                if (other.Cs[o] == c)
                {
                    had = true;
                    if (this.Cs[t] == c)
                    {
                        if (!this.Ns[t].MatchesSameWithAdd(add, from + 1, other.Ns[o]))
                            return false;
                    }
                    else
                    {
                        if (!other.Ns[o].MatchesOnly(add, from + 1))
                            return false;
                        t--;
                    }
                }
                else if (this.Cs[t] == other.Cs[o])
                    if (!this.Ns[t].MatchesSame(other.Ns[o]))
                        return false;
            }
            if (!had)
                if (!other.Ns[o].MatchesOnly(add, from + 1))
                    return false;

            return true;
        }

        private bool matchesHelper(DawgNode other)
        {
            if (this.Ns.Length != other.Ns.Length)
                return false;
            for (int i = 0; i < Ns.Length; i++)
                if (this.Cs[i] != other.Cs[i])
                    return false;
            for (int i = 0; i < Ns.Length; i++)
                if (!this.Ns[i].MatchesSame(other.Ns[i]))
                    return false;
            return true;
        }

        public override string ToString()
        {
            return "Node: " + string.Join("|", Suffixes().Select(s => s == "" ? "<acc>" : s));
        }

        public IEnumerable<string> Suffixes()
        {
            return suffixes("");
        }

        private IEnumerable<string> suffixes(string prefix)
        {
            if (Accepting)
                yield return prefix;
            for (int i = 0; i < Ns.Length; i++)
                foreach (var suf in Ns[i].suffixes(prefix + Cs[i]))
                    yield return suf;
        }

        public void MergeEndingNode(DawgNode endingNode)
        {
            for (int i = 0; i < Ns.Length; i++)
                if (Ns[i].IsBlank())
                {
                    if (Ns[i] != endingNode)
                        endingNode.RefCount++;
                    Ns[i] = endingNode;
                }
                else
                    Ns[i].MergeEndingNode(endingNode);
        }

        public void InsertBlankAt(int pos)
        {
            var newNs = new DawgNode[Ns.Length + 1];
            Array.Copy(Ns, newNs, pos);
            Array.Copy(Ns, pos, newNs, pos + 1, Ns.Length - pos);
            Ns = newNs;
            var newCs = new char[Cs.Length + 1];
            Array.Copy(Cs, newCs, pos);
            Array.Copy(Cs, pos, newCs, pos + 1, Cs.Length - pos);
            Cs = newCs;
        }

        public int AppendBlank()
        {
            var newNs = new DawgNode[Ns.Length + 1];
            Array.Copy(Ns, newNs, Ns.Length);
            Ns = newNs;
            var newCs = new char[Cs.Length + 1];
            Array.Copy(Cs, newCs, Cs.Length);
            Cs = newCs;
            return Ns.Length - 1;
        }
    }
}
