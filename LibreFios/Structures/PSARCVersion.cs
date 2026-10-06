// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibreFios.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public record struct PSARCVersion(ushort MajorBE, ushort MinorBE) : IComparisonOperators<PSARCVersion, PSARCVersion, bool> {
	private ushort MajorBE { get; set; } = MajorBE;
	private ushort MinorBE { get; set; } = MinorBE;

	public ushort Major {
		get => BinaryPrimitives.ReverseEndianness(MajorBE);
		set => MajorBE = BinaryPrimitives.ReverseEndianness(value);
	}

	public ushort Minor {
		get => BinaryPrimitives.ReverseEndianness(MinorBE);
		set => MinorBE = BinaryPrimitives.ReverseEndianness(value);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static bool operator >(PSARCVersion left, PSARCVersion right) => left.Major > right.Major || (left.Major == right.Major && left.Minor > right.Minor);

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static bool operator >=(PSARCVersion left, PSARCVersion right) => left.Major > right.Major || (left.MajorBE == right.Major && left.Minor >= right.Minor);

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static bool operator <(PSARCVersion left, PSARCVersion right) => left.Major < right.Major || (left.Major == right.Major && left.Minor < right.Minor);

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static bool operator <=(PSARCVersion left, PSARCVersion right) => left.Major < right.Major || (left.Major == right.Major && left.Minor <= right.Minor);

	// 1 -> new(1, 0)
	public static implicit operator PSARCVersion(int major) => new(BinaryPrimitives.ReverseEndianness((ushort) major), 0);

	// 1.2 -> new(1, 2)
	public static implicit operator PSARCVersion(float version) => new(BinaryPrimitives.ReverseEndianness((ushort) Math.Floor(version)), BinaryPrimitives.ReverseEndianness((ushort) (Math.Truncate(version) * 10)));

	// 1.2 -> new(1, 2)
	public static implicit operator PSARCVersion(double version) => new(BinaryPrimitives.ReverseEndianness((ushort) Math.Floor(version)), BinaryPrimitives.ReverseEndianness((ushort) (Math.Truncate(version) * 10)));

	public static PSARCVersion Create(int major, int minor) => new(BinaryPrimitives.ReverseEndianness((ushort) major), BinaryPrimitives.ReverseEndianness((ushort) minor));
}
