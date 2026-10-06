// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

namespace LibreFios.Structures;

[Flags]
public enum PSARCArchiveFlags : uint {
	CaseInsensitivePaths = 1 << 0,
	AbsolutePaths = 1 << 1,
	EncryptedFiles = 1 << 2,
}

public static class PSARCArchiveFlagsExtensions {
	public static bool HasFlagFast(this PSARCArchiveFlags value, PSARCArchiveFlags archiveFlags) => (value & archiveFlags) != 0;
}
