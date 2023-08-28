namespace OnlineDAWG;

struct DawgEdge
{
    public DawgNodeIndex Node;
    public char Char;
    public bool Accepting;
    public override string ToString() { return string.Format("char={0}, accept={1}", Char, Accepting); }
}

struct DawgNode
{
    public int EdgesOffset;
    public short EdgesCount;
    public int RefCount;
    public uint Hash;
    public DawgNodeIndex HashNext;
}

enum DawgNodeIndex { Null = 0 }
