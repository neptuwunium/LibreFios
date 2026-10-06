// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using LibreFios.Compression;
using LibreFios.Structures;

namespace LibreFios;

public sealed class PSARCBuilder : IDisposable {
	public PSARCBuilder(PSARC? archive, PSARCTarget target = PSARCTarget.Normal) {
		Archive = archive;
		Target = target;

		if (Archive == null || Archive.FileEntries.Count == 0) {
			return;
		}

		var reverse = Archive.BuildReversePaths();
		foreach (var (hash, entry) in Archive.FileEntries) {
			var buffer = Archive.OpenFile(entry);
			if (buffer is not PSARCMemoryBuffer memoryBuffer) {
				continue;
			}

			Files.Add(new PSARCTempFile(hash, reverse.GetValueOrDefault(hash), memoryBuffer));
		}
	}

	private PSARC? Archive { get; }
	public PSARCTarget Target { get; }
	private List<PSARCTempFile> Files { get; } = [];

	public void Dispose() {
		Archive?.Dispose();
		foreach (var file in Files) {
			file.Dispose();
		}
	}

	public void DeleteFile(string path) => DeleteFile(new PSARCHash(path));

	public void DeleteFile(PSARCHash hash) {
		var toRemove = Files.Where(x => x.Hash == hash).ToArray();
		foreach (var removed in toRemove) {
			Files.Remove(removed);
			removed.Dispose();
		}
	}

	public void AddFile(string path, PSARCMemoryBuffer buffer) {
		if (Archive?.Header.ArchiveFlags.HasFlagFast(PSARCArchiveFlags.CaseInsensitivePaths) == true) {
			path = path.ToUpperInvariant();
		}

		path = path.Replace('\\', '/');
		AddFile(new PSARCHash(path), path, buffer);
	}

	public void AddFile(PSARCHash hash, string? path, PSARCMemoryBuffer buffer) {
		if (string.IsNullOrEmpty(path)) {
			var existing = Files.FirstOrDefault(x => x.Hash == hash);
			if (existing != null) {
				path = existing.Path;
			}
		}

		DeleteFile(hash);
		Files.Add(new PSARCTempFile(hash, path, buffer));
	}

	public void Build(Stream output, PSARCVersion? version = null, PSARCCompressionType compressionType = PSARCCompressionType.ZLib, PSARCArchiveFlags flags = PSARCArchiveFlags.CaseInsensitivePaths) {
		if (compressionType is PSARCCompressionType.Invalid or PSARCCompressionType.None) {
			throw new NotSupportedException("No compression is not a valid compression type");
		}

		var manifest = new StringBuilder();
		foreach (var file in Files.Where(x => x.Path != null)) {
			manifest.Append(file.Path!);
			manifest.Append('\n');
		}

		var manifestText = manifest.ToString().TrimEnd('\n');
		var n = Encoding.ASCII.GetByteCount(manifestText);
		using var manifestBytes = new PSARCMemoryBuffer(n);
		Encoding.ASCII.GetBytes(manifestText, manifestBytes.WritableSpan);

		var largest = Files.Max(x => x.Buffer.Length);
		if (manifestBytes.Length > largest) {
			largest = manifestBytes.Length;
		}

		var blockSize = largest switch {
			> 0x1000000 => 0x1000000,
			> 0x10000 => 0x10000,
			_ => 0x100,
		};

		using var compressedStream = new MemoryStream();
		using var blockBuffer = new MemoryStream();
		var fileRecords = new List<PSARCFileEntry>();
		var blockIndex = 0;

		fileRecords.Add(new PSARCFileEntry {
			Hash = default,
			Offset = compressedStream.Length,
			BlockIndex = blockIndex,
			DecompressedSize = manifestBytes.Length,
		});

		CompressFile(manifestBytes, blockBuffer, compressedStream, compressionType, blockSize, ref blockIndex);

		foreach (var file in Files.OrderBy(x => x.Path != null)) {
			fileRecords.Add(new PSARCFileEntry {
				Hash = file.Hash,
				Offset = compressedStream.Length,
				BlockIndex = blockIndex,
				DecompressedSize = file.Buffer.Length,
			});
			CompressFile(file.Buffer, blockBuffer, compressedStream, compressionType, blockSize, ref blockIndex);
		}

		var startOffset = (int) (Unsafe.SizeOf<PSARCHeader>() + fileRecords.Count * Unsafe.SizeOf<PSARCFileEntry>() + blockBuffer.Length);

		Span<PSARCHeader> newHeader = stackalloc PSARCHeader[1];
		newHeader[0] = new PSARCHeader {
			Magic = PSARCHeader.PSAR,
			Version = version ?? PSARCVersion.Create(1, 4),
			FileTable = new PSARCFileTableHeader {
				Size = startOffset,
				EntrySize = Unsafe.SizeOf<PSARCFileEntry>(),
				Count = fileRecords.Count,
			},
			BlockSize = blockSize,
			CompressionType = compressionType,
			ArchiveFlags = flags & PSARCArchiveFlags.CaseInsensitivePaths, // we don't support any other flags yet
		};
		output.Write(MemoryMarshal.AsBytes(newHeader));
		Span<PSARCFileEntry> adjusted = stackalloc PSARCFileEntry[1];
		foreach (var entry in fileRecords) {
			adjusted[0] = entry with {
				Offset = startOffset + entry.Offset,
			};
			output.Write(MemoryMarshal.AsBytes(adjusted));
		}

		blockBuffer.Flush();
		blockBuffer.Position = 0;
		blockBuffer.CopyTo(output);
		compressedStream.Position = 0;
		compressedStream.CopyTo(output);
	}

	private static void CompressFile(PSARCMemoryBuffer data, MemoryStream blockBuffer, MemoryStream compressedStream, PSARCCompressionType compressionType, int blockSize, ref int blockIndex) {
		var blockStride = (int) Math.Log(blockSize - 1, 0x100) + 1;

		Span<int> lengthBuf = stackalloc int[1];
		var tempBlock = ArrayPool<byte>.Shared.Rent(blockSize);
		var tempBlockSpan = tempBlock.AsSpan();

		var span = data.Span;
		for (var i = 0; i < data.Length; i += blockSize) {
			blockIndex++;

			var slice = span[i..];
			if (slice.Length > blockSize) {
				slice = slice[..blockSize];
			}

			var sliceMem = data.Memory.Slice(i, slice.Length);

			var start = compressedStream.Length;

			switch (compressionType) {
				case PSARCCompressionType.Invalid:
				case PSARCCompressionType.None:
					compressedStream.Write(slice);
					compressedStream.Flush();
					break;
				case PSARCCompressionType.ZLib: {
					using var compressor = new ZLibStream(compressedStream, CompressionLevel.Fastest, true);
					compressor.Write(slice);
					compressor.Flush();
					break;
				}
				case PSARCCompressionType.LZMA: throw new NotImplementedException("LZMA compression is not implemented");
				case PSARCCompressionType.Oodle: {
					tempBlockSpan.Clear();
					var n = Oodle.Compress(sliceMem, tempBlock.AsMemory());
					compressedStream.Write(tempBlockSpan[..n]);
					break;
				}
				case PSARCCompressionType.ZStandard: {
					using var zstd = new ZStandard();
					var n = checked((int) zstd.Compress(sliceMem, tempBlock.AsMemory(), ZStandard.CompressionLevel.DecompressFast));
					compressedStream.Write(tempBlockSpan[..n]);
					break;
				}
				default: throw new ArgumentOutOfRangeException(nameof(compressionType), compressionType, null);
			}

			var length = (int) (compressedStream.Length - start);

			if (compressionType is PSARCCompressionType.Invalid or PSARCCompressionType.None && slice.Length == blockSize) {
				// uncompressed blocks are zero length, except on the last block.
				length = slice.Length;
			}

			// we somehow wrote more than a block so we gotta roll back.
			if (length > blockSize) {
				compressedStream.Position = start;
				compressedStream.SetLength(start);
				compressedStream.Write(slice);
				compressedStream.Flush();
				// uncompressed blocks are zero length, except on the last block.
				length = slice.Length < blockSize ? slice.Length : 0;
			}

			length = BinaryPrimitives.ReverseEndianness(length);
			if (blockStride == 4) {
				lengthBuf[0] = length;
				blockBuffer.Write(MemoryMarshal.AsBytes(lengthBuf));
			} else {
				length >>= (4 - blockStride) * 8;
				for (var j = 0; j < blockStride; ++j) {
					blockBuffer.WriteByte((byte) (length & 0xFF));
					length >>= 8;
				}
			}
		}
	}

	private sealed record PSARCTempFile(PSARCHash Hash, string? Path, PSARCMemoryBuffer Buffer) : IDisposable {
		public void Dispose() => Buffer.Dispose();
	}
}
