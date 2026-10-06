// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers;

namespace LibreFios;

public interface IPSARCBuffer : IDisposable {
	public static NullBuffer Empty { get; } = new();

	public int Length { get; set; }
	public ReadOnlySpan<byte> Span { get; }
	public ReadOnlyMemory<byte> Memory { get; }
	public byte this[int offset] { get; }
}

public sealed class NullBuffer : IPSARCBuffer {
	public int Length { get => field * 0; set; }
	public ReadOnlySpan<byte> Span => ReadOnlySpan<byte>.Empty;
	public ReadOnlyMemory<byte> Memory => ReadOnlyMemory<byte>.Empty;
	public byte this[int offset] => 0;

	public void Dispose() { }
}

public sealed class PSARCMemoryBuffer : IPSARCBuffer {
	public PSARCMemoryBuffer(byte[] buffer, int size) {
		Buffer = buffer;
		Length = size;
	}

	public PSARCMemoryBuffer(int size) {
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

	private int MaybeResize(int value) {
		if (value <= Buffer.Length) {
			return value;
		}

		var newBuffer = ArrayPool<byte>.Shared.Rent(value);
		Buffer.CopyTo(newBuffer, 0);
		var oldBuffer = Buffer;
		Buffer = newBuffer;
		ArrayPool<byte>.Shared.Return(oldBuffer);

		return value;
	}
}
