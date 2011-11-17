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
        private Node _starting = new Node();
        private HashTable<Node> _nodes = new HashTable<Node>();

        public int Count { get; private set; }

        public void Add(string value)
        {
            var node = _starting;
            var tohash = new List<Node>();

            while (true)
            {
                if (value == "")
                {
                    node.Ending = true;
                    break;
                }
                int c = Char2Index(value[0]);
                string rest = value.Substring(1);
                if (node != _starting)
                {
                    _nodes.Remove(node.Hash(), node);
                    tohash.Add(node);
                }
                if (node.Nodes[c] == null)
                {
                    node.Nodes[c] = addNew(rest);
                    node.Nodes[c].Reference();
                    var n = node.Nodes[c];
                    while (n.RefCount == 1 && !n.IsBlank())
                    {
                        tohash.Add(n);
                        n = n.SingleNext();
                    }
                    break;
                }

                if (node.Nodes[c].RefCount > 1)
                {
                    var old = node.Nodes[c];
                    node.Nodes[c] = duplicate(old, rest, tohash);
                    dereference(old);
                    node.Nodes[c].Reference();
                }

                uint futurehash = node.Nodes[c].HashWithAdd(rest);
                bool done = false;
                foreach (var candidate in _nodes.GetValues(futurehash))
                    if (node.Nodes[c].MatchesSameWithAdd(rest, candidate))
                    {
                        var old = node.Nodes[c];
                        node.Nodes[c] = candidate;
                        node.Nodes[c].Reference();
                        dereference(old);
                        done = true;
                        break;
                    }
                if (done)
                    break;

                node = node.Nodes[c];
                value = rest;
            }

            foreach (var n in tohash)
                if (n.RefCount > 0) // can go back to 0 if an earlier step duplicates but a later step optimizes away
                    _nodes.Add(n.Hash(), n);

            //Verify();
            Count++;
        }

        private Node duplicate(Node node, string path, List<Node> tohash)
        {
            if (path == "")
                throw new Exception("Not sure...");
            var result = new Node();
            for (int i = 0; i < node.Nodes.Length; i++)
                if (node.Nodes[i] != null)
                {
                    if (i == Char2Index(path[0]))
                        result.Nodes[i] = duplicate(node.Nodes[i], path.Substring(1), tohash);
                    else
                        result.Nodes[i] = node.Nodes[i];
                    result.Nodes[i].Reference();
                }
            result.Ending = node.Ending;
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
                for (int i = 0; i < node.Nodes.Length; i++)
                    if (node.Nodes[i] != null)
                        dereference(node.Nodes[i]);
            }
        }

        private Node addNew(string value)
        {
            if (value == "")
                return new Node { Ending = true };
            foreach (var n in _nodes.GetValues(FnvHash(value + "|")))
                if (n.MatchesOnly(value))
                    return n;

            var node = new Node();
            int c = Char2Index(value[0]);
            string rest = value.Substring(1);
            node.Nodes[c] = addNew(rest);
            node.Nodes[c].Reference();
            return node;
        }

        public static uint FnvHash(string value, uint hash = 2166136261)
        {
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }
            return hash;
        }

        public static int Char2Index(char c)
        {
            return c - 'a';
        }

        public static char Index2Char(int index)
        {
            return (char) ('a' + index);
        }

        public void Verify()
        {
            foreach (var kvp in _nodes)
            {
                foreach (var node in kvp.Value)
                    if (node.Hash() != kvp.Key)
                        throw new Exception("Wrong hash!");
                foreach (var pair in kvp.Value.UniquePairs())
                    if (pair.Item1 != (object) pair.Item2)
                        if (pair.Item1.MatchesSame(pair.Item2))
                            throw new Exception("Graph is not optimal!");
            }
            if (_nodes.GetValues(_starting.Hash()).Contains(_starting))
                throw new Exception("Starting node is in hash table!");
            verifyNode(_starting);
        }

        private void verifyNode(Node node)
        {
            if (node.IsBlank() && !node.Ending)
                throw new Exception("Blank but not ending");
            else if (node.IsBlank() && node.Ending)
            {
                if (_nodes.GetValues(node.Hash()).Contains(node))
                    throw new Exception("Blank terminating node is in hash table!");
            }
            else
            {
                if (node != _starting && !_nodes.GetValues(node.Hash()).Contains(node))
                    throw new Exception("Normal node not in hash table!");
                foreach (var n in node.Nodes)
                    if (n != null)
                        verifyNode(n);
            }
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
        public Node[] Nodes = new Node[26];
        public bool Ending;
        public int RefCount;

        private int _alpha;

        public void Reference()
        {
            RefCount++;
        }

        public bool IsSingleUse()
        {
            if (RefCount != 1)
                return false;
            resetAlpha();
            var tovisit = new Queue<Node>();
            tovisit.Enqueue(this);

            var visited = new List<Node>();
            while (tovisit.Count > 0)
            {
                var cur = tovisit.Dequeue();
                cur._alpha++;
                if (!visited.Contains(cur))
                {
                    visited.Add(cur);
                    for (int i = 0; i < cur.Nodes.Length; i++)
                        if (cur.Nodes[i] != null)
                            tovisit.Enqueue(cur.Nodes[i]);
                }
            }
            for (int i = 0; i < visited.Count; i++)
                if (visited[i]._alpha != visited[i].RefCount)
                    return false;
            return true;
        }

        private void resetAlpha()
        {
            _alpha = 0;
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i] != null)
                    Nodes[i].resetAlpha();
        }

        public bool IsBlank()
        {
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i] != null)
                    return false;
            if (!Ending)
                throw new Exception("8765278");
            return true;
        }

        public bool MatchesOnly(string value)
        {
            if (value == "")
                return Ending && IsBlank();

            int c = Graph.Char2Index(value[0]);
            for (int i = 0; i < Nodes.Length; i++)
                if (i == c && Nodes[i] == null)
                    return false;
                else if (i != c && Nodes[i] != null)
                    return false;
            return Nodes[c].MatchesOnly(value.Substring(1));
        }

        public bool MatchesSame(Node other)
        {
            if (Ending != other.Ending)
                return false;
            return matchesHelper(other);
        }

        public bool MatchesSameWithAdd(string add, Node other)
        {
            if ((Ending || add == "") != other.Ending)
                return false;
            if (add == "")
                return matchesHelper(other);
            int c = Graph.Char2Index(add[0]);
            for (int i = 0; i < Nodes.Length; i++)
            {
                if (i == c)
                {
                    // if other is null then fail
                    if (other.Nodes[i] == null)
                        return false;
                    // if this is not null then must equal with add rest
                    if (Nodes[i] != null && !Nodes[i].MatchesSameWithAdd(add.Substring(1), other.Nodes[i]))
                        return false;
                    // if this is null then other must single match on rest
                    if (Nodes[i] == null && !other.Nodes[i].MatchesOnly(add.Substring(1)))
                        return false;
                }
                else
                {
                    // if one null and other not null then fail
                    if ((Nodes[i] == null) != (other.Nodes[i] == null))
                        return false;
                    // if they aren't null then must normal match
                    if (Nodes[i] != null && !Nodes[i].MatchesSame(other.Nodes[i]))
                        return false;
                }
            }
            return true;
        }

        private bool matchesHelper(Node other)
        {
            for (int i = 0; i < Nodes.Length; i++)
            {
                if ((Nodes[i] == null) != (other.Nodes[i] == null))
                    return false;
                if (Nodes[i] != null && !Nodes[i].MatchesSame(other.Nodes[i]))
                    return false;
            }
            return true;
        }

        public uint Hash(uint hash = 2166136261, string prefix = "")
        {
            if (Ending)
                hash = Graph.FnvHash(prefix + "|", hash);
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i] != null)
                    hash = Nodes[i].Hash(hash, prefix + Graph.Index2Char(i));
            return hash;
        }

        public uint HashWithAdd(string add, uint hash = 2166136261, string prefix = "")
        {
            if (Ending != (add == ""))
                hash = Graph.FnvHash(prefix + "|", hash);
            if (add == "")
                return Hash(hash, prefix);

            int c = Graph.Char2Index(add[0]);
            for (int i = 0; i < Nodes.Length; i++)
                if (i == c && Nodes[i] == null)
                    hash = Graph.FnvHash(prefix + add + "|", hash);
                else if (i != c && Nodes[i] != null)
                    hash = Nodes[i].Hash(hash, prefix + Graph.Index2Char(i));
                else if (i == c && Nodes[i] != null)
                    hash = Nodes[i].HashWithAdd(add.Substring(1), hash, prefix + add[0]);
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
            if (Ending)
                result.Append(prefix + "|");
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i] != null)
                    Nodes[i].buildAllAcceptedEndings(result, prefix + Graph.Index2Char(i));
        }

        public Node SingleNext()
        {
            Node result = null;
            int count = 0;
            foreach (var node in Nodes)
                if (node != null)
                {
                    count++;
                    result = node;
                }
            if (count != 1)
                throw new Exception("9326");
            return result;
        }
    }
}
