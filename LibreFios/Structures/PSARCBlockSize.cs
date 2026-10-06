// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibreFios.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 2)]
public readonly record struct PSARCBlockSize(int Size) {
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static implicit operator PSARCBlockSize(int value) => new(BinaryPrimitives.ReverseEndianness(value));

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static implicit operator int(PSARCBlockSize value) => BinaryPrimitives.ReverseEndianness(value.Size);
}
