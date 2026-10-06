// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

namespace LibreFios.Structures;

public enum PSARCCompressionType : uint {
	Invalid = 0,
	None = 0x656E6F6E, // never valid, but someone probably is funny enough to do this.
	ZLib = 0x62696C7A, // always valid (as far as we can tell.)
	LZMA = 0x616D7A6C, // not used since around 2016? -- not always valid.
	Oodle = 0x6C646F6F, // used since around 2023? -- not always valid.
	ZStandard = 0x6474737A, // does not exist but it could one day and it's trivial to implement.
}
