﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Util.ExtensionMethods;

namespace ZoneFile
{
    partial class Graph
    {
        public void Verify()
        {
            foreach (var kvp in _nodes)
            {
                foreach (var node in kvp.Value)
                    if (node.Hash != kvp.Key)
                        throw new Exception("Wrong hash!");
                foreach (var pair in kvp.Value.UniquePairs())
                    if (pair.Item1 != (object) pair.Item2)
                        if (pair.Item1.MatchesSame(pair.Item2))
                            throw new Exception("Graph is not optimal!");
            }
            if (_nodes.GetValues(_starting.Hash).Contains(_starting))
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
            //if (allnodes.Count != NodeCount)
            //    throw new Exception("node count 2");

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
            if (node != _starting && !node.IsBlank() && node.Hash != node.Suffixes().Select(s => FnvHash(s)).Aggregate((cur, add) => cur ^ add))
                throw new Exception("Wrong node hash");
            if (node.IsBlank() && !node.Accepting)
                throw new Exception("Blank but not ending");
            else if (node.IsBlank() && node.Accepting)
            {
                if (_nodes.GetValues(node.Hash).Contains(node))
                    throw new Exception("Blank terminating node is in hash table!");
            }
            else
            {
                if (node != _starting && !_nodes.GetValues(node.Hash).Contains(node))
                    throw new Exception("Normal node not in hash table!");
                foreach (var n in node.Ns)
                    verifyNode(n);
            }
        }
    }
}
