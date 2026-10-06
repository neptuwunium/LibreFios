// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers;

namespace LibreFios;

public interface IPSARCBuffer : IDisposable {
	public static NullBuffer Empty { get; } = new();

	public int Length { get; }
	public ReadOnlySpan<byte> Span { get; }
	public ReadOnlyMemory<byte> Memory { get; }
	public byte this[int offset] { get; }
}

public sealed class NullBuffer : IPSARCBuffer {
	public int Length => 0;
	public ReadOnlySpan<byte> Span => ReadOnlySpan<byte>.Empty;
	public ReadOnlyMemory<byte> Memory => ReadOnlyMemory<byte>.Empty;
	public byte this[int offset] => 0;

	public void Dispose() { }
}

public sealed class PSARCMemoryBuffer : IPSARCBuffer {
	public PSARCMemoryBuffer(byte[] buffer, int size) {
		Buffer = buffer;
		Size = size;
	}

	public PSARCMemoryBuffer(int size) {
		Buffer = ArrayPool<byte>.Shared.Rent(size);
		Size = size;
	}

	private byte[] Buffer { get; }
	private int Size { get; }
	public Span<byte> WritableSpan => Buffer.AsSpan(0, Size);
	public ReadOnlyMemory<byte> WritableMemory => Buffer.AsMemory(0, Size);
	public int Length => Size;
	public ReadOnlySpan<byte> Span => WritableSpan;
	public ReadOnlyMemory<byte> Memory => WritableMemory;
	public byte this[int offset] => Span[offset];

	public void Dispose() => ArrayPool<byte>.Shared.Return(Buffer);
}
