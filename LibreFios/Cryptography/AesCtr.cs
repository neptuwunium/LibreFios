// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

namespace LibreFios.Cryptography;

public static class AesCtr {
	public static void Crypt(byte[] key, Span<byte> iv, Span<byte> encrypted, Span<byte> decrypted) {
		using var aes = Aes.Create();
		aes.Key = key;
		var blockKey = (stackalloc byte[16]);
		var counter = (stackalloc byte[16]);
		if (!iv.IsEmpty) {
			iv[..Math.Min(iv.Length, 16)].CopyTo(counter);
		}

		for (var i = 0; i < encrypted.Length; ++i) {
			var keyIdx = i % 16;

			if (keyIdx == 0) {
				aes.EncryptEcb(counter, blockKey, PaddingMode.None);

				for (var k = 15; k >= 0; k--) {
					counter[k]++;
					if (counter[k] != 0) {
						break;
					}
				}
			}

			decrypted[i] = (byte) (encrypted[i] ^ blockKey[keyIdx]);
		}
	}
}
