using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using RT.Util.ExtensionMethods;

namespace ZoneFile
{
    partial class Graph
    {
        private Node _starting = new Node(0);
        private NodeHashTable _nodes = new NodeHashTable();

        public int WordCount { get; private set; }
        //public int NodeCount { get { _starting.ResetHashCacheWithChildren(); var r = _starting.Count(); _starting.ResetHashCacheWithChildren(); return r; } }

        public void Add(string value)
        {
            var node = _starting;
            uint nexthash = 0;
            for (int index = 1; index <= value.Length + 1; index++)
            {
                char c = value[0];
                string rest = value.Substring(1);

                if (node != _starting)
                {
                    if (!node.IsBlank())
                        _nodes.Remove(node.Hash, node);
                    if (nexthash == 0)
                        nexthash = FnvHash(value);
                    node.Hash ^= nexthash;
                    _nodes.Add(node.Hash, node);
                }

                if (value.Length == 0)
                {
                    node.Accepting = true;
                    break;
                }

                int n = -1;
                for (int i = 0; i < node.Cs.Length; i++)
                    if (node.Cs[i] == c)
                    {
                        n = i;
                        break;
                    }
                    else if (node.Cs[i] > c)
                    {
                        node.InsertBlankAt(i);
                        node.Cs[i] = c;
                        n = i;
                        break;
                    }
                if (n < 0)
                {
                    n = node.AppendBlank();
                    node.Cs[n] = c;
                }

                if (node.Ns[n] == null)
                {
                    node.Ns[n] = addNew(rest);
                    node.Ns[n].Reference();
                    break;
                }

                if (node.Ns[n].RefCount > 1)
                {
                    var old = node.Ns[n];
                    node.Ns[n] = duplicate(old, rest);
                    dereference(old);
                    node.Ns[n].Reference();
                }

                nexthash = FnvHash(rest);
                bool done = false;
                var candidates = _nodes.GetValues(node.Ns[n].Hash ^ nexthash);
                var candidate = candidates.First;
                while (candidate != null)
                {
                    if (node.Ns[n].MatchesSameWithAdd(rest, candidate.Value))
                    {
                        var old = node.Ns[n];
                        node.Ns[n] = candidate.Value;
                        node.Ns[n].Reference();
                        dereference(old);
                        done = true;
                        break;
                    }
                    candidate = candidate.Next;
                }
                if (done)
                    break;

                node = node.Ns[n];
                value = rest;
            }

            WordCount++;
            //Verify();
        }

        private Node duplicate(Node node, string path)
        {
            if (path == "")
                throw new Exception("198729");
            var result = new Node(node.Ns.Length) { Hash = node.Hash };
            for (int i = 0; i < node.Ns.Length; i++)
            {
                result.Cs[i] = node.Cs[i];
                if (node.Cs[i] == path[0])
                    result.Ns[i] = duplicate(node.Ns[i], path.Substring(1));
                else
                    result.Ns[i] = node.Ns[i];
                result.Ns[i].Reference();
            }
            result.Accepting = node.Accepting;
            return result;
        }

        private void dereference(Node node)
        {
            node.RefCount--;
            if (node.RefCount < 0)
                throw new Exception("836");
            if (node.RefCount == 0)
            {
                if (!node.IsBlank())
                    _nodes.Remove(node.Hash, node);
                for (int i = 0; i < node.Ns.Length; i++)
                    dereference(node.Ns[i]);
            }
        }

        private Node addNew(string value)
        {
            if (value == "")
                return new Node(0) { Accepting = true, Hash = FnvHash("") };
            foreach (var n in _nodes.GetValues(FnvHash(value)))
                if (n.MatchesOnly(value))
                    return n;

            var node = new Node(1) { Hash = FnvHash(value) };
            node.Cs[0] = value[0];
            node.Ns[0] = addNew(value.Substring(1));
            node.Ns[0].Reference();
            _nodes.Add(node.Hash, node);
            return node;
        }

        public void MergeEndingNode()
        {
            var node = new Node(0) { Accepting = true };
            _starting.MergeEndingNode(node);
        }

        public static uint FnvHash(string str)
        {
            uint hash = 2166136261;
            for (int i = 0; i < str.Length; i++)
                hash = (hash ^ str[i]) * 16777619;
            return hash;
        }

        private static uint _unique;
        public void UniqueIdIntoHash()
        {
            _unique = 0;
            resetHashes(_starting);
            assignIds(_starting);
        }

        private void resetHashes(Node node)
        {
            node.Hash = 0;
            foreach (var n in node.Ns)
                resetHashes(n);
        }

        private void assignIds(Node node)
        {
            _unique++;
            node.Hash = _unique;
            foreach (var n in node.Ns)
                if (n.Hash == 0)
                    assignIds(n);
        }
    }

    class Node
    {
        public Node[] Ns;
        public char[] Cs;
        public bool Accepting;
        public int RefCount;
        public uint Hash = 0;

        public Node(int blanks)
        {
            Ns = new Node[blanks];
            Cs = new char[blanks];
        }

        public void Reference()
        {
            RefCount++;
        }

        public bool IsBlank()
        {
            // OPT: inline everywhere
            return Ns.Length == 0;
        }

        public bool MatchesOnly(string value)
        {
            if (value == "")
                return Accepting && IsBlank();
            if (Ns.Length != 1)
                return false;
            return Cs[0] == value[0] && Ns[0].MatchesOnly(value.Substring(1));
            // OPT: remove recursion and substring
        }

        public bool MatchesSame(Node other)
        {
            if (Accepting != other.Accepting)
                return false;
            return matchesHelper(other);
        }

        public bool MatchesSameWithAdd(string add, Node other)
        {
            if ((Accepting || add == "") != other.Accepting)
                return false;
            if (add == "")
                return matchesHelper(other);
            if (this.Ns.Length < other.Ns.Length - 1 || this.Ns.Length > other.Ns.Length)
                return false;

            // Shallow test to make sure the characters match
            char c = add[0];
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
                        if (!this.Ns[t].MatchesSameWithAdd(add.Substring(1), other.Ns[o]))
                            return false;
                    }
                    else
                    {
                        if (!other.Ns[o].MatchesOnly(add.Substring(1)))
                            return false;
                        t--;
                    }
                }
                else if (this.Cs[t] == other.Cs[o])
                    if (!this.Ns[t].MatchesSame(other.Ns[o]))
                        return false;
            }
            if (!had)
                if (!other.Ns[o].MatchesOnly(add.Substring(1)))
                    return false;

            return true;
        }

        private bool matchesHelper(Node other)
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
            return "Node: " + Suffixes().Select(s => s == "" ? "<acc>" : s).JoinString("|");
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

        public void MergeEndingNode(Node endingNode)
        {
            for (int i = 0; i < Ns.Length; i++)
                if (Ns[i].IsBlank())
                    Ns[i] = endingNode;
                else
                    Ns[i].MergeEndingNode(endingNode);
        }

        public void InsertBlankAt(int pos)
        {
            var newNs = new Node[Ns.Length + 1];
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
            var newNs = new Node[Ns.Length + 1];
            Array.Copy(Ns, newNs, Ns.Length);
            Ns = newNs;
            var newCs = new char[Cs.Length + 1];
            Array.Copy(Cs, newCs, Cs.Length);
            Cs = newCs;
            return Ns.Length - 1;
        }
    }

    class NodeHashTable : IEnumerable<KeyValuePair<uint, LinkedList<Node>>>
    {
        private Dictionary<uint, LinkedList<Node>> _values = new Dictionary<uint, LinkedList<Node>>();

        public void Add(uint hash, Node value)
        {
            if (_values.ContainsKey(hash))
            {
                _values[hash].AddLast(value);
            }
            else
            {
                var list = new LinkedList<Node>();
                list.AddLast(value);
                _values[hash] = list;
            }
        }

        public void Remove(uint hash, Node value)
        {
            _values[hash].Remove(value);
            if (_values[hash].Count == 0)
                _values.Remove(hash);
        }

        private static LinkedList<Node> _empty = new LinkedList<Node>();

        public LinkedList<Node> GetValues(uint hash)
        {
            if (!_values.ContainsKey(hash))
                return _empty;
            return _values[hash];
        }


        public IEnumerator<KeyValuePair<uint, LinkedList<Node>>> GetEnumerator() { return _values.GetEnumerator(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
}
