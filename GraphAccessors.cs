using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace OnlineDAWG
{
    partial class DawgGraph
    {
        private DawgNodeIndex _cachedEdgeFor;
        private DawgEdge[] _cachedEdgeChunk;
        private int _cachedEdgeIndex;

        private bool GetEdgeAccepting(DawgNodeIndex node, int index)
        {
            if (node != _cachedEdgeFor)
            {
                int offset = _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset;
                _cachedEdgeFor = node;
                _cachedEdgeChunk = _edges._chunks[offset >> _edges._shifts];
                _cachedEdgeIndex = offset & _edges._mask;
            }
            return _cachedEdgeChunk[_cachedEdgeIndex + index].Accepting;
        }

        private char GetEdgeChar(DawgNodeIndex node, int index)
        {
            if (node != _cachedEdgeFor)
            {
                int offset = _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset;
                _cachedEdgeFor = node;
                _cachedEdgeChunk = _edges._chunks[offset >> _edges._shifts];
                _cachedEdgeIndex = offset & _edges._mask;
            }
            return _cachedEdgeChunk[_cachedEdgeIndex + index].Char;
        }

        private DawgNodeIndex GetEdgeNode(DawgNodeIndex node, int index)
        {
            if (node != _cachedEdgeFor)
            {
                int offset = _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset;
                _cachedEdgeFor = node;
                _cachedEdgeChunk = _edges._chunks[offset >> _edges._shifts];
                _cachedEdgeIndex = offset & _edges._mask;
            }
            return _cachedEdgeChunk[_cachedEdgeIndex + index].Node;
        }

        private void SetEdgeAccepting(DawgNodeIndex node, int index, bool value)
        {
            if (node != _cachedEdgeFor)
            {
                int offset = _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset;
                _cachedEdgeFor = node;
                _cachedEdgeChunk = _edges._chunks[offset >> _edges._shifts];
                _cachedEdgeIndex = offset & _edges._mask;
            }
            _cachedEdgeChunk[_cachedEdgeIndex + index].Accepting = value;
        }

        private void SetEdgeChar(DawgNodeIndex node, int index, char value)
        {
            if (node != _cachedEdgeFor)
            {
                int offset = _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset;
                _cachedEdgeFor = node;
                _cachedEdgeChunk = _edges._chunks[offset >> _edges._shifts];
                _cachedEdgeIndex = offset & _edges._mask;
            }
            _cachedEdgeChunk[_cachedEdgeIndex + index].Char = value;
        }

        private void SetEdgeNode(DawgNodeIndex node, int index, DawgNodeIndex value)
        {
            if (node != _cachedEdgeFor)
            {
                int offset = _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset;
                _cachedEdgeFor = node;
                _cachedEdgeChunk = _edges._chunks[offset >> _edges._shifts];
                _cachedEdgeIndex = offset & _edges._mask;
            }
            _cachedEdgeChunk[_cachedEdgeIndex + index].Node = value;
        }

        private int GetNodeEdgesOffset(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset;
        }

        private short GetNodeEdgesCount(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesCount;
        }

        private int GetNodeRefCount(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].RefCount;
        }

        internal uint GetNodeHash(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].Hash;
        }

        internal DawgNodeIndex GetNodeHashNext(DawgNodeIndex node)
        {
            return _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].HashNext;
        }

        private void SetNodeEdgesOffset(DawgNodeIndex node, int value)
        {
            if (_cachedEdgeFor == node) _cachedEdgeFor = DawgNodeIndex.Null;
            _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesOffset = value;
        }

        private void SetNodeEdgesCount(DawgNodeIndex node, short value)
        {
            _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].EdgesCount = value;
        }

        private int IncNodeRefCount(DawgNodeIndex node)
        {
            return ++_nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].RefCount;
        }

        private int DecNodeRefCount(DawgNodeIndex node)
        {
            return --_nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].RefCount;
        }

        private void SetNodeHash(DawgNodeIndex node, uint value)
        {
            _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].Hash = value;
        }

        internal void SetNodeHashNext(DawgNodeIndex node, DawgNodeIndex value)
        {
            _nodes._chunks[(int) node >> _nodes._shifts][(int) node & _nodes._mask].HashNext = value;
        }

    }
}
