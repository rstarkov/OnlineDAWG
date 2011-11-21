// Copyright 2011 Roman Starkov
// This file is part of OnlineDAWG: https://bitbucket.org/rstarkov/onlinedawg
///
// OnlineDAWG can be redistributed and/or modified under the terms of the GNU General Public License as published by the
// Free Software Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied
// warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

using System;
using System.IO;
using System.Linq;

namespace OnlineDAWG
{
    partial class DawgGraph
    {
        /// <summary>
        /// Performs various self-tests on the current DAWG. This method is slow.
        /// </summary>
        public void Verify()
        {
            foreach (var grp in _hashtable.GroupBy(n => _nodes[n].Hash))
            {
                var arr = grp.ToArray();
                for (int i = 0; i < arr.Length - 1; i++)
                    for (int j = i + 1; j < arr.Length; j++)
                        if (arr[i] == arr[j])
                            throw new Exception("Duplicate nodes");
                        else if (matchesSame(_nodes[arr[i]], _nodes[arr[j]]))
                            throw new Exception("Graph is not optimal!");
            }
            if (_hashtable.Contains(_starting))
                throw new Exception("Starting node is in hash table!");
            verifyNode(_starting);
        }

        private void verifyNode(int node)
        {
            if (node == 0)
                throw new Exception("Null node!");
            var nodeI = _nodes[node];
            if (nodeI.Edges.Length != nodeI.Edges.Length)
                throw new Exception("Ns != Cs");
            //if (node != _starting && !nodeI.IsBlank && nodeI.Hash != nodeI.Suffixes().Select(s => FnvHash(s)).Aggregate((cur, add) => cur ^ add))
            //    throw new Exception("Wrong node hash");
            if (nodeI.IsBlank && !nodeI.Accepting)
                throw new Exception("Blank but not ending");
            else if (nodeI.IsBlank)
            {
                if (node != _ending)
                    throw new Exception("Blank accepting node != _ending");
                if (_hashtable.GetValuesExact(nodeI.Hash).Contains(node))
                    throw new Exception("Blank terminating node is in hash table!");
            }
            else
            {
                if (node != _starting && (!_hashtable.GetValuesExact(nodeI.Hash).Contains(node)))
                    throw new Exception("Normal node not in hash table!");
                foreach (var e in nodeI.Edges)
                    verifyNode(e.Node);
            }
        }

        public static void SelfTest()
        {
            DawgGraph g;

            g = new DawgGraph();
            g.Add("fos"); g.Verify();
            g.Add("as"); g.Verify();
            g.Add("fo"); g.Verify();
            assert(g.WordCount == 3);
            assert(g.NodeCount == 5);
            assert(g.NodeCount == g.GetNodes().Count());
            assert(g.EdgeCount == g.GetNodes().Sum(n => n.Edges.Length));

            g = new DawgGraph();
            g.Add("xac"); g.Verify(); assert(g.NodeCount == 4);
            g.Add("xacd"); g.Verify(); assert(g.NodeCount == 5);
            g.Add("xbe"); g.Verify(); assert(g.NodeCount == 6);
            g.Add("xbef"); g.Verify(); assert(g.NodeCount == 7);
            g.Add("yac"); g.Verify(); assert(g.NodeCount == 9);
            g.Add("ybe"); g.Verify(); assert(g.NodeCount == 10);
            g.Add("ybef"); g.Verify(); assert(g.NodeCount == 9);
            g.Add("yacd"); g.Verify(); assert(g.NodeCount == 7);
            assert(g.WordCount == 8);
            assert(g.NodeCount == 7);
            assert(g.NodeCount == g.GetNodes().Count());
            assert(g.EdgeCount == g.GetNodes().Sum(n => n.Edges.Length));

            g = new DawgGraph();
            g.Add("xab"); g.Verify();
            g.Add("xac"); g.Verify();
            g.Add("yab"); g.Verify();
            g.Add("yac"); g.Verify();
            g.Add("xabc"); g.Verify();
            assert(g.WordCount == 5);
            assert(g.NodeCount == 7);
            assert(g.NodeCount == g.GetNodes().Count());
            assert(g.EdgeCount == g.GetNodes().Sum(n => n.Edges.Length));

            //var ms = new MemoryStream();
            //g.Save(ms);
            //ms.Position = 0;
            //var g2 = DawgGraph.Load(ms);
            //assert(g2.Contains("xabc"));
            //assert(g2.Contains("yac"));
            //assert(g2.Contains("yab"));
            //assert(g2.Contains("xac"));
            //assert(g2.Contains("xab"));

            //assert(!g2.Contains("yabc"));
            //assert(!g2.Contains("abc"));
            //assert(!g2.Contains("ac"));
            //assert(!g2.Contains("c"));
            //assert(!g2.Contains(""));
            //assert(!g2.Contains("stuff"));

            //ms = new MemoryStream();
            //new DawgGraph().Save(ms);
            //ms.Position = 0;
            //g2 = DawgGraph.Load(ms);
            //assert(!g2.Contains(""));
        }

        private static void assert(bool p)
        {
            if (!p)
                throw new Exception("DawgGraph SelfTest failed.");
        }
    }
}
