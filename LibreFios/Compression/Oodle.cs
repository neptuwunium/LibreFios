// SPDX-FileCopyrightText: 2023-2026 Neptuwunium
//
// SPDX-License-Identifier: EUPL-1.2
// SPDX-Note: https://codeberg.org/neptuwunium/neptune

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibreFios.Compression;

// ReSharper disable once RedundantUnsafeContext
public static unsafe partial class Oodle {
	public enum CompressionLevel {
		None = 0,
		SuperFast = 1,
		VeryFast = 2,
		Fast = 3,
		Normal = 4,
		Optimal1 = 5,
		Optimal2 = 6,
		Optimal3 = 7,
		Optimal4 = 8,
		Optimal5 = 9,
		HyperFast1 = -1,
		HyperFast2 = -2,
		HyperFast3 = -3,
		HyperFast4 = -4,
		HyperFast = HyperFast1,
		Optimal = Optimal2,
		Max = Optimal5,
		Min = HyperFast4,
		Invalid = 0x40000000,
	}

	public enum Compressor {
		Invalid = -1,
		LZH = 0,
		LZHLW = 1,
		LZNIB = 2,
		None = 3,
		LZB16 = 4,
		LZBLW = 5,
		LZA = 6,
		LZNA = 7,
		Kraken = 8,
		Mermaid = 9,
		BitKnit = 10,
		Selkie = 11,
		Hydra = 12,
		Leviathan = 13,
	}

	public enum Jobify {
		Default = 0,
		Disable = 1,
		Normal = 2,
		Aggressive = 3,
	}

	public enum Profile {
		Main = 0,
		Reduced = 1,
	}

	public enum ThreadPhase {
		Phase1 = 1,
		Phase2 = 2,
		All = 3,
		Unthreaded = All,
	}

	public enum Verbosity {
		None = 0,
		Minimal = 1,
		Some = 2,
		Lots = 3,
	}

	static Oodle() => BlockDecoderMemorySizeNeeded = NativeMethods.OodleLZDecoder_MemorySizeNeeded(Compressor.Invalid, -1);

	public static int BlockDecoderMemorySizeNeeded { get; }

	public static string ParseOodleVersion(uint value) {
		var check = value >> 28;
		var provider = (value >> 24) & 0xF;
		var major = (value >> 16) & 0xFF;
		var minor = (value >> 8) & 0xFF;
		var table = value & 0xFF;
		return $"{check}.{major}.{minor} (provider: {provider:X1}, seek: {table})";
	}

	public static uint CreateOodleVersion(int major, int minor, int seekTableSize = 48) => (46u << 24) | (uint) (major << 16) | (uint) (minor << 8) | (uint) seekTableSize;

	public static int Decompress(ReadOnlyMemory<byte> input, Memory<byte> output) {
		using var inPin = input.Pin();
		using var outPin = output.Pin();
		using var pool = MemoryPool<byte>.Shared.Rent(BlockDecoderMemorySizeNeeded);
		using var poolPin = pool.Memory.Pin();
		return NativeMethods.OodleLZ_Decompress((byte*) inPin.Pointer, input.Length, (byte*) outPin.Pointer, output.Length, true, false, Verbosity.Minimal, default, 0, default, default, (byte*) poolPin.Pointer, BlockDecoderMemorySizeNeeded, ThreadPhase.Unthreaded);
	}

	public static IMemoryOwner<byte>? Decompress(Memory<byte> input, MemoryPool<byte>? pool = default) {
		var size = GetDecodeBufferSize(input, false);
		var output = (pool ?? MemoryPool<byte>.Shared).Rent(size);
		if (Decompress(input, output.Memory[..size]) != -1) {
			return output;
		}

		output.Dispose();
		return default;
	}

	public static IMemoryOwner<byte>? Compress(ReadOnlyMemory<byte> input, Compressor compressor, CompressionLevel level, MemoryPool<byte>? pool = default) {
		var size = GetCompressedBufferSize(compressor, input.Length);
		var output = (pool ?? MemoryPool<byte>.Shared).Rent(size);
		if (Compress(input, output.Memory[..size], compressor, level) != -1) {
			return output;
		}

		output.Dispose();
		return default;
	}

	public static int Compress(ReadOnlyMemory<byte> input, Memory<byte> output, Compressor compressor, CompressionLevel level) {
		var options = GetDefaultOptions(compressor, level);
		return Compress(input, output, Memory<byte>.Empty, compressor, level, options);
	}

	public static int Compress(ReadOnlyMemory<byte> input, Memory<byte> output, Memory<byte> dict, Compressor compressor, CompressionLevel level, CompressOptions options) {
		var compressorOptions = options;
		compressorOptions.Unused1 = compressorOptions.Unused2 = compressorOptions.Unused3 = compressorOptions.Unused4 = compressorOptions.Unused5 = compressorOptions.Unused6 = 0;

		int scratchBound;
		fixed (CompressOptions* compressorOptionsPin = &Unsafe.AsRef(ref compressorOptions)) {
			scratchBound = (int) NativeMethods.OodleLZ_GetCompressScratchMemBound(compressor, level, input.Length + compressorOptions.DictionarySize, compressorOptionsPin);
		}

		if (scratchBound == -1) {
			scratchBound = BlockDecoderMemorySizeNeeded;
		}

		options.DictionarySize = dict.Length;

		using var inPin = input.Pin();
		using var outPin = output.Pin();
		using var dictPin = dict.Pin();
		using var scratch = MemoryPool<byte>.Shared.Rent(scratchBound);
		using var scratchPin = scratch.Memory.Pin();
		fixed (CompressOptions* compressorOptionsPin = &Unsafe.AsRef(ref compressorOptions)) {
			return NativeMethods.OodleLZ_Compress(compressor, (byte*) inPin.Pointer, input.Length, (byte*) outPin.Pointer, level, compressorOptionsPin, (byte*) dictPin.Pointer, nint.Zero, (byte*) scratchPin.Pointer, scratchBound);
		}
	}

	public static int Compress(ReadOnlyMemory<byte> input, Memory<byte> output) => Compress(input, output, Compressor.Hydra, CompressionLevel.Max);

	private static CompressOptions GetDefaultOptions(Compressor compressor, CompressionLevel level) {
		var options = Unsafe.Read<CompressOptions>(NativeMethods.OodleLZ_CompressOptions_GetDefault(compressor, level));
		options.Unused1 = options.Unused2 = options.Unused3 = options.Unused4 = options.Unused5 = options.Unused6 = 0;
		return options;
	}

	public static int GetDecodeBufferSize(Memory<byte> input, bool corruptionPossible) => (int) NativeMethods.OodleLZ_GetDecodeBufferSize(GetCompressor(input), input.Length, corruptionPossible);

	public static int GetCompressedBufferSize(Compressor compressor, int length) => (int) NativeMethods.OodleLZ_GetCompressedBufferSizeNeeded(compressor, length);

	public static Compressor GetCompressor(Memory<byte> input) {
		using var inPin = input.Pin();
		var independent = false;
		return NativeMethods.OodleLZ_GetFirstChunkCompressor((byte*) inPin.Pointer, input.Length, ref independent);
	}

	public static string GetCompressorName(Memory<byte> input) {
		var compressor = GetCompressor(input);
		return GetCompressorName(compressor);
	}

	public static string GetCompressorName(Compressor compressor) => NativeMethods.OodleLZ_Compressor_GetName(compressor);

	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	public record struct CompressOptions {
		public int Unused1 { get; set; }
		public int MinMatchLen { get; set; }
		public bool SeekChunkReset { get; set; }
		public int SeekChunkLen { get; set; }
		public Profile Profile { get; set; }
		public int DictionarySize { get; set; }
		public int SpaceSpeedTradeoffBytes { get; set; }
		public int Unused2 { get; set; }
		public bool SendQuantumCRC { get; set; }
		public int MaxLocalDictionarySize { get; set; }
		public bool MakeLongRangeMatcher { get; set; }
		public int MatchTableSizeLog2 { get; set; }
		public Jobify Jobify { get; set; }
		public nint JobifyUserPtr { get; set; }
		public int FarMatchMinLen { get; set; }
		public int FarMatchOffsetLog2 { get; set; }
		public int Unused3 { get; set; }
		public int Unused4 { get; set; }
		public int Unused5 { get; set; }
		public int Unused6 { get; set; }
	}

	private static partial class NativeMethods {
		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial int OodleLZ_Decompress(byte* srcBuf, long srcSize, byte* rawBuf, long rawSize, [MarshalAs(UnmanagedType.I4)] bool fuzzSafe, [MarshalAs(UnmanagedType.I4)] bool checkCRC, Verbosity verbosity, byte* decBufBase, long decBufSize, void* fpCallback, void* callbackUserData, byte* decoderMemory, long decoderMemorySize, ThreadPhase threadPhase);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial int OodleLZ_Compress(Compressor compressor, byte* rawBuf, long rawSize, byte* compBuf, CompressionLevel level, CompressOptions* options, byte* dictionaryBase, nint lrm, byte* scratchMem, long scratchSize);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial Compressor OodleLZ_GetFirstChunkCompressor(byte* srcBuf, long srcSize, [MarshalAs(UnmanagedType.I4)] ref bool independent);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		[return: MarshalAs(UnmanagedType.LPStr)]
		public static partial string OodleLZ_Compressor_GetName(Compressor compressor);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial int OodleLZDecoder_MemorySizeNeeded(Compressor compressor, long size);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial long OodleLZ_GetCompressedBufferSizeNeeded(Compressor compressor, long size);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial long OodleLZ_GetDecodeBufferSize(Compressor compressor, long size, [MarshalAs(UnmanagedType.I4)] bool corruptionPossible);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial long OodleLZ_GetCompressScratchMemBound(Compressor compressor, CompressionLevel level, long size, CompressOptions* options);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial CompressOptions* OodleLZ_CompressOptions_GetDefault(Compressor compressor, CompressionLevel level);
	}
}
