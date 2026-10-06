// SPDX-FileCopyrightText: 2023-2026 Neptuwunium
//
// SPDX-License-Identifier: EUPL-1.2
// SPDX-Note: https://codeberg.org/neptuwunium/neptune

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibreFios.Compression;

// ReSharper disable once RedundantUnsafeContext
public sealed unsafe partial class ZStandard : IDisposable {
	public enum CompressionLevel {
		None = 0,
		Fast = 1,
		DecompressFast = 2,
		Greedy = 3,
		Lazy = 4,
		VeryLazy = 5,
		Lazy2 = 6,
		Optimal = 7,
		Ultra = 8,
		VeryUltra = 9,
	}

	public enum CompressionParameter {
		CompressionLevel = 100,
		WindowLog = 101,
		HashLog = 102,
		ChainLog = 103,
		SearchLog = 104,
		MinMatch = 105,
		TargetLength = 106,
		Strategy = 107,
		TargetCBlockSize = 130,
		EnableLongDistanceMatching = 160,
		LdmHashLog = 161,
		LdmMinMatch = 162,
		LdmBucketSizeLog = 163,
		LdmHashRateLog = 164,
		ContentSizeFlag = 200,
		ChecksumFlag = 201,
		DictionaryIdFlag = 202,
		NbWorkers = 400,
		JobSize = 401,
		OverlapLog = 402,
		RSyncable = 500,
		Format = 10,
		ForceMaxWindow = 1000,
		ForceAttachDict = 1001,
		LiteralCompressionMode = 1002,
		SrcSizeHint = 1004,
		EnableDedicatedDictSearch = 1005,
		StableInBuffer = 1006,
		StableOutBuffer = 1007,
		BlockDelimiters = 1008,
		ValidateSequences = 1009,
		UseBlockSplitter = 1010,
		UseRowMatchFinder = 1011,
		DeterministicRefPrefix = 1012,
		PrefetchCDictTables = 1013,
		EnableSeqProducerFallback = 1014,
		MaxBlockSize = 1015,
		SearchForExternalRepCodes = 1016,
	}

	public enum DecompressionParameter {
		WindowLogMax = 100,
		Format = 1000,
		StableOutBuffer = 1001,
		ForceIgnoreChecksum = 1002,
		RefMultipleDDicts = 1003,
		DisableHuffmanAssembly = 1004,
		MaxBlockSize = 1005,
	}

	public enum DictionaryContentType {
		Auto = 0,
		RawContent = 1,
		Full = 2,
	}

	public enum DictionaryLoadMethod {
		ByCopy = 0,
		ByRef = 1,
	}

	public enum ForceIgnoreChecksum {
		ValidateChecksum = 0,
		IgnoreChecksum = 1,
	}

	public enum Format {
		Normal = 0,
		Magicless = 1,
	}

	public enum ReferenceMultipleDictionaries {
		Single = 0,
		Multiple = 1,
	}

	public ZStandard() {
		DContext = NativeMethods.ZSTD_createDCtx();
		if (DContext < 0) {
			throw new OutOfMemoryException();
		}

		CContext = NativeMethods.ZSTD_createCCtx();
		if (CContext < 0) {
			throw new OutOfMemoryException();
		}
	}

	private ReadOnlyMemory<byte> Dict { get; set; } = Memory<byte>.Empty;
	private nint DContext { get; set; }
	private nint CContext { get; set; }
	private MemoryHandle DictPin { get; set; }

	public void Dispose() {
		ReleaseUnmanagedResources();
		GC.SuppressFinalize(this);
	}

	public bool SetParameter(DecompressionParameter parameter, int value) => NativeMethods.ZSTD_DCtx_setParameter(DContext, parameter, value) == 0;
	public bool SetParameter(CompressionParameter parameter, int value) => NativeMethods.ZSTD_CCtx_setParameter(CContext, parameter, value) == 0;

	public int GetParameter(DecompressionParameter parameter) => NativeMethods.ZSTD_DCtx_getParameter(CContext, parameter, out var value) == 0 ? value : 0;
	public int GetParameter(CompressionParameter parameter) => NativeMethods.ZSTD_CCtx_getParameter(CContext, parameter, out var value) == 0 ? value : 0;

	public bool LoadDict(ReadOnlyMemory<byte> dict, DictionaryLoadMethod loadMethod = DictionaryLoadMethod.ByRef, DictionaryContentType contentType = DictionaryContentType.Auto) {
		if (dict.IsEmpty) {
			return UnloadDict();
		}

		Dict = dict;
		DictPin = Dict.Pin();
		var result = NativeMethods.ZSTD_DCtx_loadDictionary_advanced(DContext, (byte*) DictPin.Pointer, Dict.Length, loadMethod, contentType);
		if (result < 0) {
			FreeDict();
			return false;
		}

		result = NativeMethods.ZSTD_CCtx_loadDictionary_advanced(CContext, (byte*) DictPin.Pointer, Dict.Length, loadMethod, contentType);
		if (result < 0) {
			FreeDict();
			return false;
		}

		if (loadMethod != DictionaryLoadMethod.ByRef) {
			FreeDict();
		}

		return true;
	}

	public int Decompress(ReadOnlyMemory<byte> input, Memory<byte> output) {
		using var inPin = input.Pin();
		using var outPin = output.Pin();
		return (int) NativeMethods.ZSTD_decompressDCtx(DContext, (byte*) outPin.Pointer, output.Length, (byte*) inPin.Pointer, input.Length);
	}

	public IMemoryOwner<byte>? Decompress(ReadOnlyMemory<byte> input, MemoryPool<byte>? pool = default) {
		var size = GetDecompressBound(input);
		if (size <= 0) {
			return null;
		}

		var output = (pool ?? MemoryPool<byte>.Shared).Rent(size);
		if (Decompress(input, output.Memory[..size]) != -1) {
			return output;
		}

		output.Dispose();
		return default;
	}

	public long Compress(ReadOnlyMemory<byte> input, Memory<byte> output, CompressionLevel compressionLevel) {
		using var inPin = input.Pin();
		using var outPin = output.Pin();
		return NativeMethods.ZSTD_compressCCtx(CContext, (byte*) outPin.Pointer, output.Length, (byte*) inPin.Pointer, input.Length, compressionLevel);
	}

	public IMemoryOwner<byte>? Compress(ReadOnlyMemory<byte> input, CompressionLevel compressionLevel, MemoryPool<byte>? pool = default) {
		var size = GetCompressBound(input);
		var output = (pool ?? MemoryPool<byte>.Shared).Rent(size);
		if (Compress(input, output.Memory[..size], compressionLevel) != -1) {
			return output;
		}

		output.Dispose();
		return default;
	}

	public static int GetCompressBound(ReadOnlyMemory<byte> bytes) => (int) NativeMethods.ZSTD_compressBound(bytes.Length);

	public static int GetDecompressBound(ReadOnlyMemory<byte> bytes) {
		using var pin = bytes.Pin();
		return (int) NativeMethods.ZSTD_getDecompressedSize((byte*) pin.Pointer, bytes.Length);
	}

	public bool UnloadDict() {
		var result1 = NativeMethods.ZSTD_DCtx_loadDictionary_advanced(DContext, (byte*) nint.Zero, 0, 0, 0);
		var result2 = NativeMethods.ZSTD_CCtx_loadDictionary_advanced(CContext, (byte*) nint.Zero, 0, 0, 0);
		if ((nint) DictPin.Pointer != nint.Zero) {
			FreeDict();
		}

		return result1 == 0 && result2 == 0;
	}

	private void FreeDict() {
		if ((nint) DictPin.Pointer == nint.Zero) {
			return;
		}

		DictPin.Dispose();
		DictPin = default;
		Dict = Memory<byte>.Empty;
	}

	private void FreeContext() {
		if (DContext > 0) {
			DContext = NativeMethods.ZSTD_freeDCtx(DContext);
		}

		if (CContext > 0) {
			CContext = NativeMethods.ZSTD_freeCCtx(CContext);
		}
	}

	private void ReleaseUnmanagedResources() {
		FreeContext();
		FreeDict();
	}

	~ZStandard() => ReleaseUnmanagedResources();

	private static partial class NativeMethods {
		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial nint ZSTD_DCtx_setParameter(nint dctx, DecompressionParameter param, int value);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial nint ZSTD_CCtx_setParameter(nint cctx, CompressionParameter param, int value);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial nint ZSTD_DCtx_getParameter(nint dctx, DecompressionParameter param, out int value);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial nint ZSTD_CCtx_getParameter(nint cctx, CompressionParameter param, out int value);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial nint ZSTD_DCtx_loadDictionary_advanced(nint dctx, byte* dict, long dictSize, DictionaryLoadMethod loadMethod, DictionaryContentType contentType);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial nint ZSTD_CCtx_loadDictionary_advanced(nint cctx, byte* dict, long dictSize, DictionaryLoadMethod loadMethod, DictionaryContentType contentType);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial nint ZSTD_createDCtx();

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial nint ZSTD_createCCtx();

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial nint ZSTD_freeDCtx(nint dctx);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static partial nint ZSTD_freeCCtx(nint cctx);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial nint ZSTD_decompressDCtx(nint dctx, byte* dst, long dstCapacity, byte* src, long srcSize);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial nint ZSTD_compressCCtx(nint cctx, byte* dst, long dstCapacity, byte* src, long srcSize, CompressionLevel compressionLevel);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial long ZSTD_getDecompressedSize(byte* src, long srcSize);

		[LibraryImport("zstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		public static unsafe partial long ZSTD_compressBound(long size);
	}
}
