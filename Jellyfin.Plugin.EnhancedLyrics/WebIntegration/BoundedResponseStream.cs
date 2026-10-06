namespace Jellyfin.Plugin.EnhancedLyrics.WebIntegration;

/// <summary>Buffers small index responses; oversized output passes through without growing memory.</summary>
internal sealed class BoundedResponseStream(Stream destination, bool head, int limit) : Stream
{
    private readonly MemoryStream _buffer = new();
    internal bool Overflowed { get; private set; }
    internal byte[] ToArray() => _buffer.ToArray();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!Overflowed && _buffer.Length + count <= limit) { _buffer.Write(buffer, offset, count); return; }
        if (!Overflowed)
        {
            Overflowed = true;
            if (!head) { _buffer.Position = 0; _buffer.CopyTo(destination); }
            _buffer.SetLength(0);
        }
        if (!head) destination.Write(buffer, offset, count);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!Overflowed && _buffer.Length + buffer.Length <= limit)
        {
            await _buffer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!Overflowed)
        {
            Overflowed = true;
            if (!head)
            {
                _buffer.Position = 0;
                await _buffer.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
            _buffer.SetLength(0);
        }
        if (!head) await destination.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _buffer.Dispose();
        base.Dispose(disposing);
    }
}
