// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace LibreFios.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 0x1E)]
public record struct PSARCFileEntry {
	public PSARCHash Hash { get; set; }
	private int BlockIndexBE { get; set; }
	public PSARCUInt40 DecompressedSize { get; set; }
	public PSARCUInt40 Offset { get; set; }

	public int BlockIndex {
		get => BinaryPrimitives.ReverseEndianness(BlockIndexBE);
		set => BlockIndexBE = BinaryPrimitives.ReverseEndianness(value);
	}
}
