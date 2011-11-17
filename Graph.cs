using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using RT.Util.ExtensionMethods;

namespace ZoneFile
{
    class Graph
    {
        private Node _starting = new Node(0);
        private HashTable<Node> _nodes = new HashTable<Node>();

        public int WordCount { get; private set; }
        public int NodeCount { get { _starting.ResetHashCacheWithChildren(); var r = _starting.Count(); _starting.ResetHashCacheWithChildren(); return r; } }

        public void Add(string value)
        {
            var node = _starting;
            var tohash = new List<Node>();

            while (true)
            {
                if (value == "")
                {
                    node.Accepting = true;
                    break;
                }
                if (node != _starting)
                {
                    _nodes.Remove(node.Hash(), node);
                    node.HashCached = 0;
                    tohash.Add(node);
                }

                char c = value[0];
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

                string rest = value.Substring(1);
                if (node.Ns[n] == null)
                {
                    node.Ns[n] = addNew(rest);
                    node.Ns[n].Reference();
                    var nd = node.Ns[n];
                    while (nd.RefCount == 1 && !nd.IsBlank())
                    {
                        tohash.Add(nd);
                        nd = nd.Ns[0];
                    }
                    break;
                }

                if (node.Ns[n].RefCount > 1)
                {
                    var old = node.Ns[n];
                    node.Ns[n] = duplicate(old, rest, tohash);
                    dereference(old);
                    node.Ns[n].Reference();
                }

                uint futurehash = node.Ns[n].HashWithAdd(rest);
                bool done = false;
                foreach (var candidate in _nodes.GetValues(futurehash))
                    if (node.Ns[n].MatchesSameWithAdd(rest, candidate))
                    {
                        var old = node.Ns[n];
                        node.Ns[n] = candidate;
                        node.Ns[n].Reference();
                        dereference(old);
                        done = true;
                        break;
                    }
                if (done)
                    break;

                node = node.Ns[n];
                value = rest;
            }

            foreach (var hn in tohash)
                if (hn.RefCount > 0) // can go back to 0 if an earlier step duplicates but a later step optimizes away
                    _nodes.Add(hn.Hash(), hn);

            WordCount++;
            //Verify();
        }

        private Node duplicate(Node node, string path, List<Node> tohash)
        {
            if (path == "")
                throw new Exception("198729");
            var result = new Node(node.Ns.Length);
            for (int i = 0; i < node.Ns.Length; i++)
            {
                result.Cs[i] = node.Cs[i];
                if (node.Cs[i] == path[0])
                    result.Ns[i] = duplicate(node.Ns[i], path.Substring(1), tohash);
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
                _nodes.Remove(node.Hash(), node);
                node.HashCached = 0;
                for (int i = 0; i < node.Ns.Length; i++)
                    dereference(node.Ns[i]);
            }
        }

        private Node addNew(string value)
        {
            if (value == "")
                return new Node(0) { Accepting = true };
            foreach (var n in _nodes.GetValues(Node.HashSingleMatch(value)))
                if (n.MatchesOnly(value))
                    return n;

            var node = new Node(1);
            node.Cs[0] = value[0];
            node.Ns[0] = addNew(value.Substring(1));
            node.Ns[0].Reference();
            return node;
        }

        public void Verify()
        {
            foreach (var kvp in _nodes)
            {
                foreach (var node in kvp.Value)
                {
                    node.ResetHashCacheWithChildren();
                    if (node.Hash() != kvp.Key)
                        throw new Exception("Wrong hash!");
                }
                foreach (var pair in kvp.Value.UniquePairs())
                    if (pair.Item1 != (object) pair.Item2)
                        if (pair.Item1.MatchesSame(pair.Item2))
                            throw new Exception("Graph is not optimal!");
            }
            if (_nodes.GetValues(_starting.Hash()).Contains(_starting))
                throw new Exception("Starting node is in hash table!");
            verifyNode(_starting);

            // the following test currently only succeeds after merging ending nodes
            //if (_nodes.Sum(n => n.Value.Count) + 2 != NodeCount)
            //    throw new Exception("node count");

            var allnodes = new List<Node>();
            var queue = new Queue<Node>();
            queue.Enqueue(_starting);
            while (queue.Any())
            {
                var n = queue.Dequeue();
                if (allnodes.Contains(n))
                    continue;
                allnodes.Add(n);
                queue.EnqueueRange(n.Ns);
            }
            if (allnodes.Count != NodeCount)
                throw new Exception("node count 2");

            //foreach (var pair in allnodes.UniquePairs())
            //    if (!pair.Item1.IsBlank() && !pair.Item2.IsBlank())
            //        if (pair.Item1.MatchesSame(pair.Item2))
            //            throw new Exception("Not optimal 2");
        }

        private void verifyNode(Node node)
        {
            if (node == null)
                throw new Exception("Null node!");
            if (node.Ns.Length != node.Cs.Length)
                throw new Exception("Ns != Cs");
            if (node.IsBlank() && !node.Accepting)
                throw new Exception("Blank but not ending");
            else if (node.IsBlank() && node.Accepting)
            {
                if (_nodes.GetValues(node.Hash()).Contains(node))
                    throw new Exception("Blank terminating node is in hash table!");
            }
            else
            {
                if (node != _starting && !_nodes.GetValues(node.Hash()).Contains(node))
                    throw new Exception("Normal node not in hash table!");
                foreach (var n in node.Ns)
                    verifyNode(n);
            }
        }

        public static void Test()
        {
            var g = new Graph();
            g.Add("ba");
            g.Verify();
            if (g._starting.Hash() != Node.HashSingleMatch("ba"))
                throw new Exception("fail");
            var hypothetic = g._starting.HashWithAdd("fo");
            g.Add("fo");
            g.Verify();
            g._starting.ResetHashCacheWithChildren();
            if (g._starting.Hash() != hypothetic)
                throw new Exception("fail");
        }

        public void MergeEndingNode()
        {
            var node = new Node(0) { Accepting = true };
            _starting.MergeEndingNode(node);
        }
    }

    class HashTable<T> : IEnumerable<KeyValuePair<uint, List<T>>>
    {
        private Dictionary<uint, List<T>> _values = new Dictionary<uint, List<T>>();

        public void Add(uint hash, T value)
        {
            if (_values.ContainsKey(hash))
            {
                if (_values[hash].Contains(value))
                    throw new Exception("already there!");
                _values[hash].Add(value);
            }
            else
            {
                var list = new List<T>();
                list.Add(value);
                _values[hash] = list;
            }
        }

        public bool Remove(uint hash, T value)
        {
            if (!_values.ContainsKey(hash))
                return false;
            return _values[hash].Remove(value);
        }

        public T[] GetValues(uint hash)
        {
            if (!_values.ContainsKey(hash))
                return new T[0];
            return _values[hash].ToArray();
        }

        public IEnumerator<KeyValuePair<uint, List<T>>> GetEnumerator() { return _values.GetEnumerator(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    class Node
    {
        public Node[] Ns;
        public char[] Cs;
        public bool Accepting;
        public int RefCount;

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

        public uint HashCached = 0;

        public uint Hash()
        {
            if (HashCached != 0)
                return HashCached;

            uint hash = 2166136261;
            if (Accepting)
                hash = (hash ^ 65536) * 16777619;
            for (int i = 0; i < Ns.Length; i++)
                hash = (((hash ^ Ns[i].Hash()) * 16777619) ^ (uint) Cs[i]) * 16777619;
            HashCached = hash;
            return hash;
        }

        public uint HashWithAdd(string add, int index = 0)
        {
            if (index >= add.Length)
                return Hash();

            uint hash = 2166136261;
            if (Accepting)
                hash = (hash ^ 65536) * 16777619;

            char c = add[index];
            bool had = false;
            for (int i = 0; i < Ns.Length; i++)
            {
                if (Cs[i] == c)
                {
                    had = true;
                    hash = (((hash ^ Ns[i].HashWithAdd(add, index + 1)) * 16777619) ^ (uint) Cs[i]) * 16777619;
                }
                else if (had || Cs[i] < c)
                {
                    hash = (((hash ^ Ns[i].Hash()) * 16777619) ^ (uint) Cs[i]) * 16777619;
                }
                else
                {
                    had = true;
                    i--;
                    hash = (((hash ^ Node.HashSingleMatch(add.Substring(index + 1))) * 16777619) ^ (uint) c) * 16777619;
                }
            }
            if (!had)
                hash = (((hash ^ Node.HashSingleMatch(add.Substring(index + 1))) * 16777619) ^ (uint) c) * 16777619;
            return hash;
        }

        public static uint HashSingleMatch(string value)
        {
            uint hash = unchecked((2166136261 ^ 65536) * 16777619);
            for (int i = value.Length - 1; i >= 0; i--)
                hash = (((2166136261 ^ hash) * 16777619) ^ (uint) value[i]) * 16777619;
            return hash;
        }

        public override string ToString()
        {
            //return (Ending ? "Node (E): " : "Node: ") + string.Join("/", Nodes.Select((n, i) => n == null ? "" : Graph.Index2Char(i).ToString()).Where(s => s != ""));
            var sb = new StringBuilder();
            buildAllAcceptedEndings(sb, "");
            sb.Remove(sb.Length - 1, 1);
            return "Node: " + (sb.Length == 0 ? "<blank>" : sb.ToString());
        }

        public void buildAllAcceptedEndings(StringBuilder result, string prefix)
        {
            if (Accepting)
                result.Append(prefix + "|");
            for (int i = 0; i < Ns.Length; i++)
                Ns[i].buildAllAcceptedEndings(result, prefix + Cs[i]);
        }

        public void ResetHashCacheWithChildren()
        {
            HashCached = 0;
            _unique = 0;
            foreach (var node in Ns)
                if (node != null)
                    node.ResetHashCacheWithChildren();
        }

        private static uint _unique;

        public int Count()
        {
            int total = 1;
            _unique++;
            HashCached = _unique;
            foreach (var node in Ns)
                if (node.HashCached == 0)
                    total += node.Count();
            return total;
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
}
