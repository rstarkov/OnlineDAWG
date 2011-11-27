﻿using System;
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
    }

    /// <summary>
    /// A list optimized for growth. This list can never shrink, and allocates additional storage efficiently
    /// even when a very large number of elements are already in it.
    /// </summary>
    class ChunkyNodeList
    {
        internal DawgNode[][] _chunks = new DawgNode[4][];
        internal int _chunkSize, _shifts, _mask;
        private DawgNodeIndex _next = (DawgNodeIndex) 1;
        private DawgNodeIndex[] _reuse;
        private int _reuseCount = 0;

        /// <summary>Constructor.</summary>
        /// <param name="chunkSizeExponent">For performance tuning. Number of elements per chunk is 2^this value.</param>
        /// <param name="reuseCapacity">For performance tuning. The initial capacity of the reuse list.</param>
        public ChunkyNodeList(int chunkSizeExponent = 16, int reuseCapacity = 65536)
        {
            _shifts = chunkSizeExponent;
            _chunkSize = 1 << _shifts;
            _mask = _chunkSize - 1;
            _reuse = new DawgNodeIndex[Math.Max(reuseCapacity, 1)];
        }

        /// <summary>Gets the number of elements added to this list.</summary>
        public int Count { get { return (int) _next - 1; } }
        public int ReuseCount { get { return _reuseCount; } }

        /// <summary>
        /// Adds an element to the list and returns its index. If any elements were marked for reuse, the count
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
    }

    class ChunkyArrayList<T>
    {
        internal T[][] _chunks = new T[4][];
        internal int _chunkSize, _shifts, _mask;
        private int _next = 0;
        private int[][] _reuse = new int[64][];
        private int[] _reuseCount = new int[64];

        public ChunkyArrayList(int chunkSizeExponent = 17, int reuseCapacity = 65536)
        {
            _shifts = chunkSizeExponent;
            _chunkSize = 1 << _shifts;
            _mask = _chunkSize - 1;
            for (int i = 0; i < _reuse.Length; i++)
                _reuse[i] = new int[Math.Max(reuseCapacity, 1)];
        }

        public int Add(int length)
        {
            if (_reuseCount[length] > 0)
                return _reuse[length][--_reuseCount[length]];

            int li = _next >> _shifts;
            if (li >= _chunks.Length)
                Array.Resize(ref _chunks, _chunks.Length * 2);
            if (_chunks[li] == null)
                _chunks[li] = new T[_chunkSize];

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
                    _chunks[li] = new T[_chunkSize];
            }

            var result = _next;
            _next += length;
            return result;
        }

        public void Reuse(int length, int index)
        {
            if (_reuseCount[length] >= _reuse[length].Length)
                Array.Resize(ref _reuse[length], _reuse[length].Length * 2);
            _reuse[length][_reuseCount[length]++] = index;
        }

        public int Count { get { return _next; } }
        public int ReuseCount { get { return _reuseCount.Select((v, i) => v * i).Sum(); } }
    }
}
