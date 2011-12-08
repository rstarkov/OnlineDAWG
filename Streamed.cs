using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace OnlineDAWG
{
    public class DawgGraphStreamed : IDisposable
    {
        private int _startingIndex;
        private bool _containsEmpty;
        private char[] _charset;
        private long[] _seekIndex;
        private int _seekIndexInterval;
        private Stream _stream;

        /// <summary>Gets the number of distinct "words" (values added with <see cref="Add"/>) that this graph accepts.</summary>
        public int WordCount { get; private set; }
        /// <summary>Gets the number of nodes in the graph.</summary>
        public int NodeCount { get; private set; }
        /// <summary>Gets the number of edges in the graph.</summary>
        public int EdgeCount { get; private set; }
        /// <summary>Gets the approximate number of bytes of memory that this graph consumes.</summary>
        public long MemoryUsage { get { return 8 * _seekIndex.LongLength + 2 * _charset.LongLength + 10 * IntPtr.Size + 5 * 4; } }

        /// <summary>Loads the DAWG from the specified file.</summary>
        public static DawgGraphStreamed Load(string filename)
        {
            return Load(File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        /// <summary>Loads the DAWG from the specified compatible stream.</summary>
        public static DawgGraphStreamed Load(Stream stream)
        {
            if (!stream.CanSeek)
                throw new ArgumentException("Only seekable streams are supported.");
            long startPosition = stream.Position;
            var buf = new byte[64];
            Util.FillBuffer(stream, buf, 0, 6);
            if (Encoding.UTF8.GetString(buf, 0, 6) != "DAWG.2")
                throw new InvalidDataException();
            var result = new DawgGraphStreamed();
            result._stream = stream;

            result._charset = new char[Util.OptimRead(stream)];
            for (int i = 0; i < result._charset.Length; i++)
                result._charset[i] = (char) Util.OptimRead(stream);

            result.EdgeCount = (int) Util.OptimRead(stream);
            result.NodeCount = (int) Util.OptimRead(stream);
            result.WordCount = (int) Util.OptimRead(stream);
            result._containsEmpty = stream.ReadByte() != 0;
            result._startingIndex = (int) Util.OptimRead(stream);

            Util.FillBuffer(stream, buf, 0, 8);
            stream.Position = BitConverter.ToInt64(buf, 0) + startPosition;

            result._seekIndexInterval = (int) Util.OptimRead(stream);
            result._seekIndex = new long[Util.OptimRead(stream)];

            long pos = startPosition;
            for (int i = 0; i < result._seekIndex.Length; i++)
            {
                pos += Util.OptimRead(stream);
                result._seekIndex[i] = pos;
            }

            return result;
        }

        /// <summary>Looks up the specified value if the DAWG and returns a value to indicate if it's present.</summary>
        public bool Contains(string value)
        {
            var nodeIndex = _startingIndex;
            bool accepting = _containsEmpty;
            for (int index = 0; index < value.Length; index++)
                if (!FollowEdge(nodeIndex, value[index], out nodeIndex, out accepting))
                    return false;
            return accepting;
        }

        private bool FollowEdge(int nodeIndex, char edgeChar, out int nextNodeIndex, out bool edgeAccepting)
        {
            if (NodeCount == 0)
            {
                nextNodeIndex = -1;
                edgeAccepting = false;
                return false;
            }

            // Locate the node in the stream
            int ii = nodeIndex / _seekIndexInterval;
            if (ii >= _seekIndex.Length)
                ii = _seekIndex.Length - 1;
            int curIndex = ii * _seekIndexInterval;
            _stream.Position = _seekIndex[ii];
            while (curIndex < nodeIndex)
            {
                Util.OptimSkip(_stream, (int) Util.OptimRead(_stream) * 2);
                curIndex++;
            }

            // Follow the desired edge
            int edgeCount = (int) Util.OptimRead(_stream);
            for (int e = 0; e < edgeCount; e++)
            {
                var characc = Util.OptimRead(_stream);
                nextNodeIndex = (int) Util.OptimRead(_stream);
                if (_charset[characc >> 1] == edgeChar)
                {
                    edgeAccepting = (characc & 1) != 0;
                    return true;
                }
            }

            nextNodeIndex = -1;
            edgeAccepting = false;
            return false;
        }

        public void Dispose()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }
    }
}
