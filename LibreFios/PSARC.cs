// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LibreFios.Compression;
using LibreFios.Cryptography;
using LibreFios.Structures;
using LZMADecoder = SevenZip.Compression.LZMA.Decoder;

namespace LibreFios;

public enum PSARCTarget {
	Normal,
	PlaystationAllStarsBattleRoyale,
}

public sealed class PSARC : IDisposable {
	private static readonly char[] LINE_SEPARATORS = ['\n', (char) 0];

	public PSARC(Stream stream, PSARCTarget target = PSARCTarget.Normal) {
		BaseStream = stream;
		Target = target;

		if (stream.Length < 0x20) {
			FileEntries = [];
			Manifest = [];
			BlockSizeBuffer = MemoryPool<byte>.Shared.Rent(0);
			return;
		}

		Span<PSARCHeader> header = stackalloc PSARCHeader[1];
		BaseStream.ReadExactly(MemoryMarshal.AsBytes(header));
		Header = header[0];

		Manifest = new Dictionary<string, PSARCHash>(Header.ArchiveFlags.HasFlagFast(PSARCArchiveFlags.CaseInsensitivePaths) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

		// check if the file starts with "PSAR"
		if (Header.Magic != PSARCHeader.PSAR) {
			throw new InvalidDataException("Not a PSARC File");
		}

		// we only support version 1.x
		if (Header.Version > 2 || Header.Version < 1) {
			throw new NotSupportedException("PSARC has an unsupported version");
		}

		// some sanity checks to ensure things are still valid
		if (Header.FileTable.EntrySize < 0x1e || Header.BlockSize < 0x100) {
			throw new InvalidDataException("PSARC File is corrupt");
		}

		// read entries in one go
		var entries = MemoryPool<PSARCFileEntry>.Shared.Rent(Header.FileTable.Count);
		var entriesSpan = entries.Memory.Span[..Header.FileTable.Count];
		var entriesBytes = MemoryMarshal.AsBytes(entriesSpan);
		BaseStream.ReadExactly(entriesBytes);

		// amortize it into a dictionary.
		FileEntries = new Dictionary<PSARCHash, PSARCFileEntry>(Header.FileTable.Count);
		foreach (var entry in entriesSpan) {
			FileEntries[entry.Hash] = entry;
		}

		// read the compression block list in one go
		BlockSizeBufferSize = Header.FileTable.Size - entriesBytes.Length;
		BlockSizeBuffer = MemoryPool<byte>.Shared.Rent(BlockSizeBufferSize);
		BaseStream.ReadExactly(BlockSizeBuffer.Memory.Span[..BlockSizeBufferSize]);

		// read manifest (if it exists)
		// manifest has no hash.
		using var manifest = OpenFile(default(PSARCHash));
		if (manifest.Length <= 0 || manifest.Span[0] == 0) {
			return;
		}

		// todo: check if this always matches the file order, it might be possible to just skip md5-ing the path.
		foreach (var filePath in Encoding.ASCII.GetString(manifest.Span).Split(LINE_SEPARATORS, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
			// normalize paths.
			// todo: can relative paths start with '/'?
			Manifest[filePath.Replace('\\', '/')] = new PSARCHash(Header.ArchiveFlags.HasFlagFast(PSARCArchiveFlags.CaseInsensitivePaths) ? filePath.ToUpperInvariant() : filePath);
		}
	}

	// allocate 255 bytes for random slop to avoid making allocations over and over and over again.
	internal byte[] ScratchPad { get; } = ArrayPool<byte>.Shared.Rent(byte.MaxValue);
	internal PSARCHeader Header { get; set; }
	internal int BlockSizeBufferSize { get; set; }
	internal IMemoryOwner<byte> BlockSizeBuffer { get; set; }

	public Stream BaseStream { get; set; }
	public PSARCTarget Target { get; set; }
	public Dictionary<PSARCHash, PSARCFileEntry> FileEntries { get; set; }
	public Dictionary<string, PSARCHash> Manifest { get; set; }
	public IEnumerable<string> Paths => Manifest.Keys;

	public void Dispose() {
		ArrayPool<byte>.Shared.Return(ScratchPad);
		BaseStream.Dispose();
		BlockSizeBuffer.Dispose();
	}

	internal IPSARCBuffer OpenFile(PSARCFileEntry file) {
		// unfortunately, Span<T> only holds 2 GB.
		// it's not impossible to refactor this support larger (i.e. a PSARCMemoryStream implementation of IPSARCBuffer)
		if (file.DecompressedSize > int.MaxValue) {
			throw new NotSupportedException("Large files are not supported.");
		}

		BaseStream.Position = file.Offset;
		// rent some data from the memory pool for reading and decompressing.
		using var rentedBlockBuffer = MemoryPool<byte>.Shared.Rent(Header.BlockSize);
		using var rentedDataBuffer = MemoryPool<byte>.Shared.Rent(Header.BlockSize);
		using var rentedEncBuffer = MemoryPool<byte>.Shared.Rent(Header.BlockSize);
		var blockBuffer = rentedBlockBuffer.Memory.Span[..Header.BlockSize];
		var dataBuffer = rentedDataBuffer.Memory.Span[..Header.BlockSize];
		var encBuffer = rentedEncBuffer.Memory.Span[..Header.BlockSize];
		var blockIndex = file.BlockIndex;
		var resultBuffer = new PSARCMemoryBuffer((int) file.DecompressedSize);
		var resultSpan = resultBuffer.WritableSpan;
		var wasEncrypted = false;

		var fill = (int) file.DecompressedSize;
		while (fill > 0) {
			// read the next block
			var blockSize = GetCompressedBlockSize(blockIndex++);
			var blockSlice = blockBuffer[..(blockSize == 0 ? Header.BlockSize : blockSize)];
			BaseStream.ReadExactly(blockSlice);

			// if it has the encrypted flag, decrypt it here
			if (Header.ArchiveFlags.HasFlagFast(PSARCArchiveFlags.EncryptedFiles)) {
				// we've never seen this file or this implemented in any implementation.
				// but we've seen the flag in two places.
				var key = BlockSizeBuffer.Memory.Span.Slice(BlockSizeBufferSize - 0x20, 0x10);
				var iv = BlockSizeBuffer.Memory.Span.Slice(BlockSizeBufferSize - 0x10, 0x10);
				using var aes = Aes.Create();
				key.CopyTo(ScratchPad);
				aes.Key = ScratchPad[..0x10];
				// todo: maybe ECB?
				var n = aes.DecryptCbc(blockSlice, iv, rentedDataBuffer.Memory.Span);

				// copy data back into the compressed buffer
				rentedDataBuffer.Memory.Span[..blockSlice.Length].CopyTo(blockSlice);
				blockSlice = blockBuffer[..n];
			}

			// if the block is exactly the same size as the remaining buffer, it's not compressed (usually.)
			if (blockSlice.Length == fill || blockSize == 0) {
				blockSlice.CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
				fill -= blockSlice.Length;
				continue;
			}

			var compressedBlockBuffer = rentedBlockBuffer;
			switch (Header.CompressionType) {
				// this is not actually a valid compression type, but here for completeness
				case PSARCCompressionType.Invalid:
				case PSARCCompressionType.None: {
					blockSlice.CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= blockSlice.Length;
					break;
				}
				case PSARCCompressionType.ZLib: {
					if (!IsZlib(blockSlice)) {
						if (!TryDecrypt(blockSlice, encBuffer)) {
							return IPSARCBuffer.Empty;
						}

						wasEncrypted = true;
						compressedBlockBuffer = rentedEncBuffer;
					}

					// calculate how many bytes we'll be reading
					var n = Math.Min(fill, Header.BlockSize);

					unsafe {
						// wrap the compressed buffer span into a stream
						using var unmanagedCompressedPin = compressedBlockBuffer.Memory.Pin();
						using var unmanagedCompressed = new UnmanagedMemoryStream((byte*) unmanagedCompressedPin.Pointer, blockBuffer.Length);
						Stream compressionStream;
						if (wasEncrypted && Target == PSARCTarget.PlaystationAllStarsBattleRoyale) {
							compressionStream = new DeflateStream(unmanagedCompressed, CompressionMode.Decompress);
						} else {
							compressionStream = new ZLibStream(unmanagedCompressed, CompressionMode.Decompress);
						}

						try {
							compressionStream.ReadExactly(dataBuffer[..n]);
						} finally {
							compressionStream.Dispose();
						}
					}

					// copy the decompressed data into the resulting buffer
					dataBuffer[..n].CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= n;

					break;
				}
				case PSARCCompressionType.LZMA: {
					// note: find a better lzma implementation
					if (!IsLZMA(blockSlice)) {
						if (!TryDecrypt(blockSlice, encBuffer)) {
							return IPSARCBuffer.Empty;
						}

						wasEncrypted = true;
						compressedBlockBuffer = rentedEncBuffer;
					}

					var lzma = new LZMADecoder();
					var uncompressedSize = BinaryPrimitives.ReadUInt64LittleEndian(blockBuffer[5..]);
					blockBuffer[..5].CopyTo(ScratchPad);
					lzma.SetDecoderProperties(ScratchPad[..5]);
					var n = Math.Min(fill, Header.BlockSize);
					unsafe {
						using var unmanagedCompressedPin = compressedBlockBuffer.Memory.Pin();
						using var unmanagedCompressed = new UnmanagedMemoryStream((byte*) unmanagedCompressedPin.Pointer, blockBuffer.Length);
						using var unmanagedDecompressedPin = rentedDataBuffer.Memory.Pin();
						using var unmanagedDecompressed = new UnmanagedMemoryStream((byte*) unmanagedDecompressedPin.Pointer, dataBuffer.Length);
						unmanagedCompressed.Position = 13;
						lzma.Code(unmanagedCompressed, unmanagedDecompressed, unmanagedCompressed.Length - 13, unmanagedDecompressed.Length, null);
					}

					if ((ulong) n > uncompressedSize) {
						// this should NEVER trigger.
						n = (int) uncompressedSize;
					}

					dataBuffer[..n].CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= n;
					break;
				}
				case PSARCCompressionType.Oodle: {
					// note: provide oo2core.dll (rename it)
					if (!IsOodle(blockSlice)) {
						if (!TryDecrypt(blockSlice, encBuffer)) {
							return IPSARCBuffer.Empty;
						}

						wasEncrypted = true;
						compressedBlockBuffer = rentedEncBuffer;
					}

					var n = Oodle.Decompress(compressedBlockBuffer.Memory[..Header.BlockSize], rentedDataBuffer.Memory[..Header.BlockSize]);
					dataBuffer[..n].CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= n;
					break;
				}
				case PSARCCompressionType.ZStandard: {
					// note: provide libzstd.dll
					if (!IsZStandard(blockSlice)) {
						if (!TryDecrypt(blockSlice, encBuffer)) {
							return IPSARCBuffer.Empty;
						}

						wasEncrypted = true;
						compressedBlockBuffer = rentedEncBuffer;
					}

					using var zstd = new ZStandard();
					var n = zstd.Decompress(compressedBlockBuffer.Memory[..Header.BlockSize], rentedDataBuffer.Memory[..Header.BlockSize]);
					dataBuffer[..n].CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= n;
					break;
				}
				default: throw new NotSupportedException("Unknown compression method");
			}
		}

		return resultBuffer;
	}

	private bool TryDecrypt(Span<byte> encrypted, Span<byte> decrypted) {
		switch (Target) {
			case PSARCTarget.PlaystationAllStarsBattleRoyale: {
				Span<byte> iv = stackalloc byte[16];
				BinaryPrimitives.WriteUInt32BigEndian(iv, 0xdeadbeef);
				BinaryPrimitives.WriteUInt32BigEndian(iv[4..], (uint) encrypted.Length);
				BinaryPrimitives.WriteUInt32BigEndian(iv[8..], (uint) ((encrypted[0] << 8) | encrypted[1]));
				AesCtr.Crypt(KeyVault.PLAYSTATION_ALL_STARS_BATTLE_ROYALE_KEY, iv, encrypted[2..], decrypted);
				return true;
			}
			default:
				return false;
		}
	}

	private static bool IsZlib(Span<byte> span) => span[0] == 0x78 && span[1] > 0x1F;
	private static bool IsLZMA(Span<byte> span) => span[0] is 0x5D or 0x2C;
	private static bool IsOodle(Span<byte> span) => (span[0] & 0x3F) == 0xC;
	private static bool IsZStandard(Span<byte> span) => span[1] == 0xb5 && span[2] == 0x2f && span[3] == 0xfd;

	public IPSARCBuffer OpenFile(PSARCHash hash) => FileEntries.TryGetValue(hash, out var file) ? OpenFile(file) : IPSARCBuffer.Empty;
	public IPSARCBuffer OpenFile(string path) => Manifest.TryGetValue(path.Replace('\\', '/'), out var hash) ? OpenFile(hash) : OpenFile(new PSARCHash(Header.ArchiveFlags.HasFlagFast(PSARCArchiveFlags.CaseInsensitivePaths) ? path.ToUpperInvariant() : path));

	public Dictionary<PSARCHash, string> BuildReversePaths() => Manifest.ToDictionary(x => x.Value, x => x.Key);

	private int GetCompressedBlockSize(int index) {
		var offset = Header.CompressedBlockSize * index;
		var bufferSpan = BlockSizeBuffer.Memory.Span;
		return Header.CompressedBlockSize switch {
			0 => 0,
			1 => bufferSpan[offset],
			2 => (bufferSpan[offset++] << 8) | bufferSpan[offset],
			3 => (bufferSpan[offset++] << 16) | (bufferSpan[offset++] << 8) | bufferSpan[offset],
			4 => (bufferSpan[offset++] << 24) | (bufferSpan[offset++] << 16) | (bufferSpan[offset++] << 8) | bufferSpan[offset],
			_ => 0,
		};
	}
}
