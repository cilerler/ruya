using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.OpenTelemetry;

[SuppressMessage("Usage", "CA2213", Justification = "The wrapped response stream is owned by ASP.NET Core and must remain open.")]
internal sealed class BoundedCaptureStream : Stream
{
    private readonly Stream _inner;
    private readonly MemoryStream _capture;
    private readonly int _captureLimit;
    private bool _exceededLimit;

    public BoundedCaptureStream(Stream inner, int captureLimit)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(captureLimit);

        _inner = inner;
        _captureLimit = captureLimit;
        _capture = new MemoryStream(Math.Min(captureLimit + 1, 81920));
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count)
    {
        Capture(buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Capture(buffer);
        _inner.Write(buffer);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        Capture(buffer.AsSpan(offset, count));
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        Capture(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public string? GetCapturedBody(HttpBodyCapture sanitizer, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);

        if (_exceededLimit)
        {
            return HttpBodyCapture.BodyTooLarge;
        }

        if (_capture.Length == 0)
        {
            return null;
        }

        return sanitizer.SanitizeBody(
            Encoding.UTF8.GetString(_capture.GetBuffer(), 0, checked((int)_capture.Length)),
            contentType);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _capture.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Capture(ReadOnlySpan<byte> buffer)
    {
        if (_exceededLimit)
        {
            return;
        }

        var remaining = _captureLimit + 1 - checked((int)_capture.Length);
        if (buffer.Length >= remaining)
        {
            _capture.Write(buffer[..remaining]);
            _exceededLimit = true;
            return;
        }

        _capture.Write(buffer);
    }
}
