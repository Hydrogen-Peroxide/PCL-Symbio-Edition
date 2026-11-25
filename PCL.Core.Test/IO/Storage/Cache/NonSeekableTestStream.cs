using System;
using System.IO;

namespace PCL.Core.Test.IO.Storage.Cache;

internal sealed class NonSeekableTestStream(byte[] buffer) : MemoryStream(buffer)
{
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
}
