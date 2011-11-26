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
        public override string ToString() { return string.Format("char={0}, accept={1}", Char, Accepting); }
    }

    class DawgNode
    {
        public int EdgesOffset;
        public short EdgesCount;
        public int RefCount;
        public uint Hash = 0;
        public DawgNode HashNext;

        public DawgNode(short edgesCount, int edgesOffset)
        {
            EdgesCount = edgesCount;
            EdgesOffset = edgesOffset;
        }
    }
}
