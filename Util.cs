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
using System.Text;
using System.IO;

namespace OnlineDAWG
{
    static class Util
    {
        public static int FillBuffer(Stream stream, byte[] buffer, int offset, int length)
        {
            int totalRead = 0;
            while (length > 0)
            {
                var read = stream.Read(buffer, offset, length);
                if (read == 0)
                    return totalRead;
                offset += read;
                length -= read;
                totalRead += read;
            }
            return totalRead;
        }

        public static void OptimWrite(Stream stream, uint val)
        {
            while (val >= 128)
            {
                stream.WriteByte((byte) (val | 128));
                val >>= 7;
            }
            stream.WriteByte((byte) val);
        }

        public static uint OptimRead(Stream stream)
        {
            byte b = 255;
            int shifts = 0;
            uint res = 0;
            while (b > 127)
            {
                int read = stream.ReadByte();
                if (read < 0) throw new InvalidOperationException("Unexpected end of stream (#25753)");
                b = (byte) read;
                res = res | ((uint) (b & 127) << shifts);
                shifts += 7;
            }
            return res;
        }

        public static void OptimSkip(Stream stream, int count)
        {
            for (int c = 0; c < count; c++)
            {
                byte b = 255;
                while (b > 127)
                {
                    int read = stream.ReadByte();
                    if (read < 0) throw new InvalidOperationException("Unexpected end of stream (#25753)");
                    b = (byte) read;
                }
            }
        }
    }

    /// <summary>
    /// A list of nodes optimized for growth. This list can never shrink, and allocates additional storage efficiently
    /// even when a very large number of nodes are already in it.
    /// </summary>
    class ChunkyNodeList
    {
        internal DawgNode[][] _chunks = new DawgNode[4][];
        internal const int _shifts = 16, _chunkSize = 1 << _shifts, _mask = _chunkSize - 1;
        private const int _reuseInitial = 64;
        private DawgNodeIndex _next = (DawgNodeIndex) 1;
        private DawgNodeIndex[] _reuse = new DawgNodeIndex[_reuseInitial];
        private int _reuseCount = 0;

        /// <summary>
        /// Adds an to the list and returns its index. If any elements were marked for reuse, the count
        /// won't increase, and one of those elements will be reused instead.
        /// </summary>
        public DawgNodeIndex Add()
        {
            if (_reuseCount > 0)
            {
                _reuseCount--;
                return _reuse[_reuseCount];
            }

            int li = (int) _next >> _shifts;
            if (li >= _chunks.Length)
                Array.Resize(ref _chunks, _chunks.Length * 2);
            if (_chunks[li] == null)
                _chunks[li] = new DawgNode[_chunkSize];

            return _next++;
        }

        /// <summary>
        /// Marks the specified index for reuse next time an <see cref="Add"/> is requested. Will cause corruption
        /// if the index is out of range, or is already marked for reuse. To remain efficient, the number of pending
        /// reuse items should be low, i.e. <see cref="Add"/> should be called more often than this method.
        /// </summary>
        public void Reuse(DawgNodeIndex index)
        {
            if (_reuseCount >= _reuse.Length)
                Array.Resize(ref _reuse, _reuse.Length * 2);
            _reuse[_reuseCount] = index;
            _reuseCount++;
        }

        /// <summary>Gets the number of elements added to this list.</summary>
        public int Count { get { return (int) _next - 1; } }
        /// <summary>Gets the number of elements currently marked for reuse.</summary>
        public int ReuseCount { get { return _reuseCount; } }
        /// <summary>Gets the approximate number of bytes used by this entire list.</summary>
        public long MemoryUsage
        {
            get
            {
                return 4 * IntPtr.Size + 5 * 4
                    + (3 * IntPtr.Size + _reuse.LongLength * 4)
                    + (3 * IntPtr.Size + _chunks.Length * IntPtr.Size + _chunks.Where(a => a != null).Sum(a => 3 * IntPtr.Size + a.LongLength * (16 + IntPtr.Size)));
            }
        }
    }

    /// <summary>
    /// A list of edge sets optimized for growth. This list can never shrink, and allocates additional storage efficiently
    /// even when a very large number of elements are already in it.
    /// </summary>
    class ChunkyEdgeList
    {
        internal DawgEdge[][] _chunks = new DawgEdge[4][];
        internal const int _shifts = 16, _chunkSize = 1 << _shifts, _mask = _chunkSize - 1;
        private const int _reuseInitial = 64;
        private const int _lengthsInitial = 32;
        private int _next = 0;
        private int[][] _reuse = new int[_lengthsInitial][];
        private int[] _reuseCount = new int[_lengthsInitial];

        /// <summary>
        /// Adds a set to the list and returns the index of the first element. If any sets were marked for reuse, the count
        /// won't increase, and one of those sets will be reused instead.
        /// </summary>
        public int Add(int length)
        {
            while (length >= _reuse.Length)
            {
                Array.Resize(ref _reuse, _reuse.Length * 2);
                Array.Resize(ref _reuseCount, _reuseCount.Length * 2);
            }
            // Try to reuse a set at this exact size
            if (_reuseCount[length] > 0)
                return _reuse[length][--_reuseCount[length]];
            // Try to reuse a longer set, returning the rest to the reuse pool
            for (int l = length + 1; l < _reuseCount.Length; l++)
                if (_reuseCount[l] > 0)
                {
                    var res = _reuse[l][--_reuseCount[l]];
                    Reuse(l - length, res + length);
                    return res;
                }

            int li = _next >> _shifts;
            if (li >= _chunks.Length)
                Array.Resize(ref _chunks, _chunks.Length * 2);
            if (_chunks[li] == null)
                _chunks[li] = new DawgEdge[_chunkSize];

            int lp = _next & _mask;
            if (lp + length >= _chunkSize)
            {
                lp = _chunkSize - lp;
                Reuse(lp, _next);
                _next += lp;
                li++;
                if (li >= _chunks.Length)
                    Array.Resize(ref _chunks, _chunks.Length * 2);
                if (_chunks[li] == null)
                    _chunks[li] = new DawgEdge[_chunkSize];
            }

            var result = _next;
            _next += length;
            return result;
        }

        /// <summary>
        /// Marks the specified index for reuse next time an <see cref="Add"/> is requested. Will cause corruption
        /// if the index is out of range, or is already marked for reuse. To remain efficient, the number of pending
        /// reuse items should be low, i.e. <see cref="Add"/> should be called more often than this method.
        /// </summary>
        public void Reuse(int length, int index)
        {
            if (_reuse[length] == null)
                _reuse[length] = new int[_reuseInitial];
            if (_reuseCount[length] >= _reuse[length].Length)
                Array.Resize(ref _reuse[length], _reuse[length].Length * 2);
            _reuse[length][_reuseCount[length]++] = index;
        }

        /// <summary>Gets the number of elements added to this list.</summary>
        public int Count { get { return _next; } }
        /// <summary>Gets the number of elements currently marked for reuse.</summary>
        public int ReuseCount { get { return _reuseCount.Select((v, i) => v * i).Sum(); } }
        /// <summary>Gets the approximate number of bytes used by this entire list.</summary>
        public long MemoryUsage
        {
            get
            {
                return 5 * IntPtr.Size + 4 * 4
                    + (3 * IntPtr.Size + _chunks.Length * IntPtr.Size + _chunks.Where(a => a != null).Sum(a => 3 * IntPtr.Size + a.LongLength * 8))
                    + (3 * IntPtr.Size + _reuse.Length * IntPtr.Size + _reuse.Where(a => a != null).Sum(a => 3 * IntPtr.Size + a.LongLength * 4))
                    + (3 * IntPtr.Size + _reuseCount.LongLength * 4);
            }
        }
    }
}
