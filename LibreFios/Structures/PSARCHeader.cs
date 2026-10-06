// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace LibreFios.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 0x20)]
public record struct PSARCHeader {
	public const uint PSAR = 0x52415350;

	public uint Magic { get; set; }
	public PSARCVersion Version { get; set; }
	public PSARCCompressionType CompressionType { get; set; }
	public PSARCFileTableHeader FileTable { get; set; }
	private int BlockSizeBE { get; set; }
	private uint ArchiveFlagsBE { get; set; }

	public int CompressedBlockSize => (int) Math.Log(BlockSize - 1, 0x100) + 1;

	public int BlockSize {
		get => BinaryPrimitives.ReverseEndianness(BlockSizeBE);
		set => BlockSizeBE = BinaryPrimitives.ReverseEndianness(value);
	}

	public PSARCArchiveFlags ArchiveFlags {
		get => (PSARCArchiveFlags) BinaryPrimitives.ReverseEndianness(ArchiveFlagsBE);
		set => ArchiveFlagsBE = BinaryPrimitives.ReverseEndianness((uint) value);
	}
}
