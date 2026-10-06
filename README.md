<!--
SPDX-FileCopyrightText: 2026 Neptuwunium

SPDX-License-Identifier: MIT
-->

# LibreFios

PSARC archive reader/writer.

## Usage

### Reading

```csharp
using var stream = new FileStream(psarcPath, FileMode.Open, FileAccess.Read);
using var psarc = new PSARC(stream);

foreach(var (path, hash) in psarc.Manifest) {
	using var namedFile = psarc.OpenFile(hash);
}

using var knownFile = psarc.OpenFile("SOMEFILE.BIN");
using var anotherKnownFile = psarc.OpenFile(someMd5Hash);
```

### Writing

```csharp
using var stream = new FileStream(psarcPath, FileMode.Open, FileAccess.Read);
using var existingPsarc = new PSARC(stream);
using var builder = new PSARCBuilder(existingPsarc); // or new PSARCBuilder(null)
// if arg0 is not null, it will automatically import all files.

builder.DeleteFile("SOMEFILE.BIN");
builder.AddFile("EXISTING.BIN", existingData); // if EXISTING.BIN exists, it will overwrite the data.
builder.AddFile("NEW.BIN", newData);

using var output = new FileStream("new.psarc", FileMode.Create, FileAccess.ReadWrite);
builder.Build(output);
```

### Attribution

Special thanks to [PSAS-PSARC](https://github.com/Project-RePSASBR/psas-psarc/) for PSASBR Decryption.
