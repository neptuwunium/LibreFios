// SPDX-FileCopyrightText: 2026 Neptuwunium
//
// SPDX-License-Identifier: MIT

using System.CommandLine;
using System.Globalization;
using Serilog;
using Serilog.Events;

namespace LibreFios.Unpack;

internal enum VerboseLevel {
	Minimal,
	Normal,
	Very,
}

internal record ProgramOptions(string[] Paths, string OutputPath, bool Dry, bool NoClobber, bool Recursive);

internal static class Program {
	private static void Main(string[] args) {
		var root = new RootCommand();
		var verboseOpt = root.AddOption<bool>("-v", "--verbose", "Verbose output logging (overrides --log-level)");
		var logLevelOpt = root.AddOption<LogEventLevel>("-l", "--log-level", "Output logging level");
		logLevelOpt.SetDefaultValue(LogEventLevel.Information);
		var recursiveOpt = root.AddOption<bool>("-r", "--recursive", "Traverse directories recursively when looking for PSARCs to open");
		var noClobberOpt = root.AddOption<bool>("-n", "--no-clobber", "Silently skip existing files");
		var dryOpt = root.AddOption<bool>("-d", "--dry", "Do not write anything");
		var outputPathArg = root.AddArgument<string>("output-path", "Directory to write to");
		outputPathArg.LegalFilePathsOnly();
		var pathsArg = root.AddArgument<string[]>("input-paths", "Files to unpack or directories to iterate");
		pathsArg.LegalFilePathsOnly();

		root.SetHandler(context => {
			var verbose = context.ParseResult.GetValueForOption(verboseOpt);
			var logLevel = context.ParseResult.GetValueForOption(logLevelOpt);
			if (verbose) {
				logLevel = LogEventLevel.Debug;
			}

			Log.Logger = new LoggerConfiguration().MinimumLevel.Is(logLevel).WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateLogger();

			var paths = context.ParseResult.GetValueForArgument(pathsArg);
			var outputPath = context.ParseResult.GetValueForArgument(outputPathArg);
			var recursive = context.ParseResult.GetValueForOption(recursiveOpt);
			var noClobber = context.ParseResult.GetValueForOption(noClobberOpt);
			var dry = context.ParseResult.GetValueForOption(dryOpt);
			var options = new ProgramOptions(paths, outputPath, dry, noClobber, recursive);
			IterateCore(options);
		});
		root.Invoke(args);
	}

	private static Option<T> AddOption<T>(this Command command, string name, string alias, string description) {
		var opt = new Option<T>(name, description);
		opt.AddAlias(alias);
		command.AddOption(opt);
		return opt;
	}

	private static Argument<T> AddArgument<T>(this Command command, string name, string description) {
		var arg = new Argument<T>(name, description);
		command.AddArgument(arg);
		return arg;
	}

	private static void IterateCore(ProgramOptions options) {
		var iteratorOptions = new EnumerationOptions {
			MatchCasing = MatchCasing.CaseInsensitive,
			RecurseSubdirectories = options.Recursive,
			MatchType = MatchType.Simple,
		};

		foreach (var file in new FileEnumerator(options.Paths, iteratorOptions, "*.psarc")) {
			Log.Verbose("Opening PSARC {Path}", file);
			using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			try {
				using var psarc = new PSARC(stream);

				var reversePaths = psarc.BuildReversePaths();
				foreach (var (hash, entry) in psarc.FileEntries) {
					string? path;
					if (hash == default) {
						path = "_PSARCManifest.txt";
					} else if (!reversePaths.TryGetValue(hash, out path)) {
						path = $"_{hash}.bin";
					}

					Log.Information("{Path}", path);
					Log.Debug("Size = {Size}, Hash = {Hash}", (long) entry.DecompressedSize, entry.Hash);

					if (options.Dry) {
						continue;
					}

					var targetPath = Path.Combine(options.OutputPath, path.Replace('\\', '/').TrimStart('/', '.'));
					var pathTraversalTest = Path.GetRelativePath(options.OutputPath, targetPath);
					if (pathTraversalTest.StartsWith('.')) {
						pathTraversalTest = pathTraversalTest.Replace('\\', '/').TrimStart('/', '.');
						Log.Warning("{Path} tried to path traversal out of output directory, adjusting to {Safe}", path, pathTraversalTest);
						targetPath = Path.Combine(options.OutputPath, pathTraversalTest);
					}

					try {
						using var data = psarc.OpenFile(hash);
						Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
						using var outputStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
						if (data.Length > 0) {
							outputStream.Write(data.Span);
						} else {
							Log.Warning("PSARC File {Hash} ({Path}) is empty!", hash, path);
						}
					} catch (Exception e) {
						Log.Error(e, "Error opening PSARC File {Hash} ({Path})", hash, path);
					}
				}
			} catch (Exception e) {
				Log.Error(e, "Error opening PSARC {Path}", file);
			}
		}
	}
}
