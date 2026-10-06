// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace LibreFios.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 0xC)]
public record struct PSARCFileTableHeader {
	private int SizeBE { get; set; }
	private int EntrySizeBE { get; set; }
	private int CountBE { get; set; }

	public int Size {
		get => BinaryPrimitives.ReverseEndianness(SizeBE);
		set => SizeBE = BinaryPrimitives.ReverseEndianness(value);
	}

	public int EntrySize {
		get => BinaryPrimitives.ReverseEndianness(EntrySizeBE);
		set => EntrySizeBE = BinaryPrimitives.ReverseEndianness(value);
	}

	public int Count {
		get => BinaryPrimitives.ReverseEndianness(CountBE);
		set => CountBE = BinaryPrimitives.ReverseEndianness(value);
	}
}
