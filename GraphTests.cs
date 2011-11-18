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
    partial class DawgGraph
    {
        /// <summary>
        /// Performs various self-tests on the current DAWG. This method is very slow.
        /// </summary>
        public void Verify()
        {
            foreach (var grp in _nodes.GroupBy(n => n.Hash))
            {
                var arr = grp.ToArray();
                for (int i = 0; i < arr.Length - 1; i++)
                    for (int j = i + 1; j < arr.Length; j++)
                        if (arr[i] == (object) arr[j])
                            throw new Exception("Duplicate nodes");
                        else if (arr[i].MatchesSame(arr[j]))
                            throw new Exception("Graph is not optimal!");
            }
            if (_nodes.Contains(_starting))
                throw new Exception("Starting node is in hash table!");
            verifyNode(_starting);
        }

        private void verifyNode(DawgNode node)
        {
            if (node == null)
                throw new Exception("Null node!");
            if (node.Ns.Length != node.Cs.Length)
                throw new Exception("Ns != Cs");
            if (node != _starting && !node.IsBlank() && node.Hash != node.Suffixes().Select(s => FnvHash(s)).Aggregate((cur, add) => cur ^ add))
                throw new Exception("Wrong node hash");
            if (node.IsBlank() && !node.Accepting)
                throw new Exception("Blank but not ending");
            else if (node.IsBlank())
            {
                if (_nodes.GetValuesApprox(node.Hash).Contains(node))
                    throw new Exception("Blank terminating node is in hash table!");
            }
            else
            {
                if (node != _starting && (!_nodes.GetValuesExact(node.Hash).Contains(node)))
                    throw new Exception("Normal node not in hash table!");
                foreach (var n in node.Ns)
                    verifyNode(n);
            }
        }
    }
}
