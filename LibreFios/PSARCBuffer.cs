// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers;

namespace LibreFios;

public interface IPSARCBuffer : IDisposable {
	public static PSARCMemoryBuffer Empty { get; } = new(0);

	public int Length { get; set; }
	public ReadOnlySpan<byte> Span { get; }
	public ReadOnlyMemory<byte> Memory { get; }
	public byte this[int offset] { get; }
}

public sealed class PSARCMemoryBuffer : IPSARCBuffer {
	public PSARCMemoryBuffer(byte[] buffer, int size) {
		Buffer = buffer;
		Length = size;
	}

	public PSARCMemoryBuffer(int size) {
		if (size <= 0) {
			Buffer = [];
			Length = 0;
			return;
		}

		Buffer = ArrayPool<byte>.Shared.Rent(size);
		Length = size;
	}

	private byte[] Buffer { get; set; }

	public Span<byte> WritableSpan => Buffer.AsSpan(0, Length);
	public ReadOnlyMemory<byte> WritableMemory => Buffer.AsMemory(0, Length);
	public int Length { get; set => field = MaybeResize(value); }
	public ReadOnlySpan<byte> Span => WritableSpan;
	public ReadOnlyMemory<byte> Memory => WritableMemory;
	public byte this[int offset] => Span[offset];

	public void Dispose() => ArrayPool<byte>.Shared.Return(Buffer);

	private int MaybeResize(int length) {
		if (length <= Buffer.Length) {
			return length;
		}

		var tmpBuffer = ArrayPool<byte>.Shared.Rent(length);
		if (Buffer.Length > 0) {
			Buffer.CopyTo(tmpBuffer, 0);
		}

		(Buffer, tmpBuffer) = (tmpBuffer, Buffer);

		if (tmpBuffer.Length > 0) {
			ArrayPool<byte>.Shared.Return(tmpBuffer);
		}

		return length;
	}
}
