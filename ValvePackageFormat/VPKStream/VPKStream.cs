using System;
using System.IO;

public class VPKStream : Stream
{
	private Stream baseStream;
	private readonly long length;
	private readonly long baseOffset;
	public VPKStream(Stream baseStream, long offset, long length)
	{
		if (baseStream == null) throw new ArgumentNullException("baseStream");
		if (!baseStream.CanRead) throw new ArgumentException("can't read base stream");
		if (offset < 0) throw new ArgumentOutOfRangeException("offset");

		this.baseStream = baseStream;
		this.baseOffset = offset;
		this.length = length;

		if (!baseStream.CanSeek) new ArgumentNullException("can't seek base stream");
		baseStream.Seek(baseOffset, SeekOrigin.Current);
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		CheckDisposed();
		long remaining = length - Position;
		if (remaining <= 0) return 0;
		if (remaining < count) count = (int) remaining;
		int read = baseStream.Read(buffer, offset, count);
		return read;
	}
	private void CheckDisposed() { if (baseStream == null) throw new ObjectDisposedException(GetType().Name); }
	public override long Length { get { CheckDisposed(); return length; } }
	public override bool CanRead { get { CheckDisposed(); return true; } }
	public override bool CanWrite { get { CheckDisposed(); return false; } }
	public override bool CanSeek { get { CheckDisposed(); return true; } }
	public override long Position 
	{ 
		get 
		{ 
			CheckDisposed(); 
			return baseStream.Position - baseOffset; 
		} 
		set
		{
			if (value < 0) throw new IndexOutOfRangeException($"value ({value}) must be positive");
			if (value >= length) throw new IndexOutOfRangeException($"value ({value}) must be below {length}");
			baseStream.Position = baseOffset + value;
		} 
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		switch (origin)
		{
			default: throw new InvalidOperationException();
			case SeekOrigin.Begin:
				Position = offset;
				return Position;
			case SeekOrigin.End:
				Position = (length - 1) + offset;
				return Position;
			case SeekOrigin.Current:
				Position += offset;
				return Position;
		}
	}
	public override void SetLength(long value) { throw new NotSupportedException(); }
	public override void Flush() { CheckDisposed(); baseStream.Flush(); }
	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing)
		{
			if (baseStream != null)
			{
				try { baseStream.Dispose(); }
				catch { }
				baseStream = null;
			}
		}
	}
	public override void Write(byte[] buffer, int offset, int count) { throw new NotImplementedException(); }
}