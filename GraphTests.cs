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
            assert(NodeCount == _nodes.Count - _nodes.ReuseCount);
            assert(EdgeCount == _edges.Count - _edges.ReuseCount);
            foreach (var grp in _hashtable.GroupBy(n => GetNodeHash(n)))
            {
                var arr = grp.ToArray();
                for (int i = 0; i < arr.Length - 1; i++)
                    for (int j = i + 1; j < arr.Length; j++)
                        if (arr[i] == arr[j])
                            throw new Exception("Duplicate nodes");
                        else if (matchesSame(arr[i], arr[j]))
                            throw new Exception("Graph is not optimal!");
            }
            if (_hashtable.Contains(_starting))
                throw new Exception("Starting node is in hash table!");
            if (_hashtable.Contains(_ending))
                throw new Exception("Ending node is in hash table!");
            verifyNode(_starting);
        }

        private void verifyNode(DawgNodeIndex node)
        {
            if (node == DawgNodeIndex.Null)
                throw new Exception("Null node!");
            if (GetNodeRefCount(node) < 0)
                throw new Exception("Ref count negative");
            if (node != _starting && (GetNodeEdgesCount(node) != 0) && GetNodeHash(node) != suffixes(node).Select(s => FnvHash(s)).Aggregate((cur, add) => cur ^ add))
                throw new Exception("Wrong node hash");
            else if (GetNodeEdgesCount(node) == 0)
            {
                if (node != _ending)
                    throw new Exception("Blank accepting node != _ending");
            }
            else
            {
                if (node != _starting && (!_hashtable.GetValuesExact(GetNodeHash(node)).Contains(node)))
                    throw new Exception("Normal node not in hash table!");
                foreach (var e in getEdges(node))
                    verifyNode(e.Node);
            }
        }

        private IEnumerable<string> suffixes(DawgNodeIndex node)
        {
            return suffixes(node, "");
        }

        private IEnumerable<string> suffixes(DawgNodeIndex node, string prefix)
        {
            for (int i = 0; i < GetNodeEdgesCount(node); i++)
            {
                if (GetEdgeAccepting(node, i))
                    yield return prefix + GetEdgeChar(node, i);
                foreach (var suf in suffixes(GetEdgeNode(node, i), prefix + GetEdgeChar(node, i)))
                    yield return suf;
            }
        }

        /// <summary>
        /// Performs self-tests of the <see cref="DawgGraph"/> and <see cref="DawgGraphReadonly"/> classes.
        /// </summary>
        public static void SelfTest()
        {
            DawgGraph g;

            g = new DawgGraph();
            g.Add("far"); g.Verify(); assert(g.Contains("far"));
            g.Add("xar"); g.Verify(); assert(g.Contains("far")); assert(g.Contains("xar"));
            g.Add("fa"); g.Verify(); assert(g.Contains("far")); assert(g.Contains("xar")); assert(g.Contains("fa")); assert(g.NodeCount == 5); assert(g.EdgeCount == 5);
            g.Add("xa"); g.Verify(); assert(g.NodeCount == 4); assert(g.EdgeCount == 4);
            assertEnum(g, "fa", "far", "xa", "xar");

            g = new DawgGraph();
            g.Add("acde"); g.Verify();
            g.Add("acf"); g.Verify();
            g.Add("bcde"); g.Verify(); assert(g.NodeCount == 7); assert(g.EdgeCount == 8);
            g.Add("bcf"); g.Verify(); assert(g.NodeCount == 5); assert(g.EdgeCount == 6);
            assertEnum(g, "acde", "acf", "bcde", "bcf");

            g = new DawgGraph();
            g.Add("fos"); g.Verify();
            g.Add("as"); g.Verify();
            g.Add("fo"); g.Verify();
            assert(g.WordCount == 3);
            assert(g.NodeCount == 4);
            assert(g.NodeCount == g.getNodes().Count());
            assert(g.EdgeCount == g.getNodes().Sum(n => g.GetNodeEdgesCount(n)));
            assertEnum(g, "as", "fo", "fos");

            g = new DawgGraph();
            g.Add("xac"); g.Verify(); assert(g.NodeCount == 4); assert(g.EdgeCount == 3);
            g.Add("xacd"); g.Verify(); assert(g.NodeCount == 5); assert(g.EdgeCount == 4);
            g.Add("xbe"); g.Verify(); assert(g.NodeCount == 6); assert(g.EdgeCount == 6);
            g.Add("xbef"); g.Verify(); assert(g.NodeCount == 7); assert(g.EdgeCount == 7);
            g.Add("yac"); g.Verify(); assert(g.NodeCount == 9); assert(g.EdgeCount == 10);
            g.Add("ybe"); g.Verify(); assert(g.NodeCount == 10); assert(g.EdgeCount == 12);
            g.Add("ybef"); g.Verify(); assert(g.NodeCount == 9); assert(g.EdgeCount == 11);
            g.Add("yacd"); g.Verify(); assert(g.NodeCount == 7); assert(g.EdgeCount == 8);
            assert(g.WordCount == 8);
            assert(g.NodeCount == g.getNodes().Count());
            assert(g.EdgeCount == g.getNodes().Sum(n => g.GetNodeEdgesCount(n)));
            assertEnum(g, "xac", "xacd", "xbe", "xbef", "yac", "yacd", "ybe", "ybef");

            g = new DawgGraph();
            g.Add("xab"); g.Verify();
            g.Add("xac"); g.Verify();
            g.Add("yab"); g.Verify();
            g.Add("yac"); g.Verify(); assert(g.NodeCount == 4); assert(g.EdgeCount == 5);
            g.Add("xabc"); g.Verify();
            assert(g.WordCount == 5);
            assert(g.NodeCount == 7);
            assert(g.NodeCount == g.getNodes().Count());
            assert(g.EdgeCount == g.getNodes().Sum(n => g.GetNodeEdgesCount(n)));
            assertEnum(g, "xab", "xabc", "xac", "yab", "yac");

            var ms = new MemoryStream();
            g.Save(ms);
            ms.Position = 0;
            var g2 = DawgGraph.Load(ms);
            assert(g2.Contains("xabc"));
            assert(g2.Contains("yac"));
            assert(g2.Contains("yab"));
            assert(g2.Contains("xac"));
            assert(g2.Contains("xab"));

            assert(!g2.Contains("yabc"));
            assert(!g2.Contains("abc"));
            assert(!g2.Contains("ac"));
            assert(!g2.Contains("c"));
            assert(!g2.Contains(""));
            assert(!g2.Contains("stuff"));

            ms = new MemoryStream();
            new DawgGraph().Save(ms);
            ms.Position = 0;
            g2 = DawgGraph.Load(ms);
            assert(!g2.Contains(""));
        }

        private static void assert(bool p)
        {
            if (!p)
                throw new Exception("DawgGraph SelfTest failed.");
        }

        private static void assertEnum(DawgGraph g, params string[] values)
        {
            assert(g.SequenceEqual(values));
        }
    }
}
