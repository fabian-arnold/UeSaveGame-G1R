// Copyright 2025 Crystal Ferrai
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Text;
using Newtonsoft.Json;
using UeSaveGame;
using UeSaveGame.G1R;
using UeSaveGame.Json;

if (args.Length == 0) { PrintUsage(); return 1; }

try
{
	switch (args[0].ToLowerInvariant())
	{
		case "info":        return CmdInfo(args);
		case "list-quests": return CmdListQuests(args);
		case "set-quest":   return CmdSetQuest(args);
		case "unpack":      return CmdUnpack(args);
		case "dump-json":   return CmdDumpJson(args);
		case "-h": case "--help": case "help": PrintUsage(); return 0;
		default:
			Console.Error.WriteLine($"Unknown command '{args[0]}'.\n");
			PrintUsage();
			return 1;
	}
}
catch (Exception ex)
{
	Console.Error.WriteLine($"Error: {ex.Message}");
	return 1;
}

static int CmdInfo(string[] args)
{
	if (args.Length < 2) { Console.Error.WriteLine("Usage: info <save.sav>"); return 1; }
	G1RSaveFile save = G1RSaveFile.Load(args[1]);
	Console.WriteLine($"SaveClass : {save.SaveClass}");
	Console.WriteLine($"Top-level properties: {save.Properties.Count}");
	foreach (var p in save.Properties) Console.WriteLine($"  - {p.Name} ({p.Type})");

	var quests = save.FindQuestStates();
	Console.WriteLine($"\nQuests: {quests.Count}");
	foreach (var g in quests.GroupBy(q => q.State).OrderByDescending(g => g.Count()))
		Console.WriteLine($"  {g.Count(),5}  {g.Key}");
	return 0;
}

static int CmdListQuests(string[] args)
{
	if (args.Length < 2) { Console.Error.WriteLine("Usage: list-quests <save.sav> [filter]"); return 1; }
	string? filter = args.Length > 2 ? args[2] : null;
	G1RSaveFile save = G1RSaveFile.Load(args[1]);

	var quests = save.FindQuestStates();
	if (filter != null)
		quests = quests.Where(q => q.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

	foreach (var q in quests.OrderBy(q => q.Key))
		Console.WriteLine($"{q.State,-26} {q.Key}");
	Console.WriteLine($"\n{quests.Count} quest(s){(filter != null ? $" matching '{filter}'" : "")}.");
	return 0;
}

static int CmdSetQuest(string[] args)
{
	// set-quest <save.sav> [-o <out.sav>] <keySuffix>=<state> [<keySuffix>=<state> ...]
	if (args.Length < 3) { Console.Error.WriteLine("Usage: set-quest <save.sav> [-o <out.sav>] <keySuffix>=<state> ..."); return 1; }

	string input = args[1];
	string? output = null;
	var edits = new List<(string suffix, string state)>();
	for (int i = 2; i < args.Length; i++)
	{
		if (args[i] is "-o" or "--out") { output = args[++i]; continue; }
		int eq = args[i].IndexOf('=');
		if (eq <= 0) { Console.Error.WriteLine($"Invalid edit '{args[i]}' (expected keySuffix=state)."); return 1; }
		edits.Add((args[i][..eq], G1RSaveFile.NormalizeState(args[i][(eq + 1)..])));
	}
	if (edits.Count == 0) { Console.Error.WriteLine("No edits specified."); return 1; }
	output ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".",
		Path.GetFileNameWithoutExtension(input) + ".patched.sav");

	G1RSaveFile save = G1RSaveFile.Load(input);
	var quests = save.FindQuestStates();

	int changed = 0;
	foreach (var (suffix, state) in edits)
	{
		var matches = quests.Where(q => q.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
									 || q.Key.Contains(suffix, StringComparison.OrdinalIgnoreCase)).ToList();
		if (matches.Count == 0) { Console.Error.WriteLine($"  no quest matched '{suffix}'"); continue; }
		foreach (var q in matches)
		{
			if (q.State == state) { Console.WriteLine($"  = {q.Key} already {state}"); continue; }
			Console.WriteLine($"  {q.Key}\n      {q.State} -> {state}");
			q.Property.Value = new FString(state);
			changed++;
		}
	}
	if (changed == 0) { Console.Error.WriteLine("Nothing changed - no file written."); return 1; }

	save.Save(output);
	Console.WriteLine($"\nChanged {changed} quest(s). Wrote {output}");

	// Verify by reloading
	var reloaded = G1RSaveFile.Load(output).FindQuestStates();
	bool ok = true;
	foreach (var (suffix, state) in edits)
		foreach (var q in reloaded.Where(q => q.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
										   || q.Key.Contains(suffix, StringComparison.OrdinalIgnoreCase)))
			if (q.State != state) { ok = false; Console.Error.WriteLine($"  VERIFY MISMATCH: {q.Key} = {q.State}"); }
	Console.WriteLine(ok ? "Verified: reloaded save reflects the changes." : "WARNING: verification found mismatches.");
	return ok ? 0 : 1;
}

static int CmdUnpack(string[] args)
{
	if (args.Length < 3) { Console.Error.WriteLine("Usage: unpack <save.sav> <out.bin>"); return 1; }
	G1RSaveFile save = G1RSaveFile.Load(args[1]);
	File.WriteAllBytes(args[2], save.SerializeBody());
	Console.WriteLine($"Wrote decompressed body -> {args[2]}");
	return 0;
}

static int CmdDumpJson(string[] args)
{
	if (args.Length < 3) { Console.Error.WriteLine("Usage: dump-json <save.sav> <out.json> [--indent]"); return 1; }
	bool indent = args.Contains("--indent");
	G1RSaveFile save = G1RSaveFile.Load(args[1]);
	using var sw = new StreamWriter(args[2], false, new UTF8Encoding(false));
	using var jw = new JsonTextWriter(sw) { Formatting = indent ? Formatting.Indented : Formatting.None };
	PropertiesSerializer.ToJson(save.Properties, jw);
	jw.Flush();
	Console.WriteLine($"Wrote JSON -> {args[2]}");
	return 0;
}

static void PrintUsage()
{
	Console.WriteLine(@"UeSaveGame.G1R - Gothic 1 Remake save tool

Usage:
  UeSaveGame.G1R info <save.sav>
      Show save class, top-level properties and quest-state counts.

  UeSaveGame.G1R list-quests <save.sav> [filter]
      List quests and their CurrentState. Optional case-insensitive key filter.

  UeSaveGame.G1R set-quest <save.sav> [-o <out.sav>] <keySuffix>=<state> ...
      Set one or more quests' CurrentState, then repack and verify.
      <keySuffix> matches the end of (or any part of) a quest path.
      <state> is e.g. Succeeded, Running, Available, Failed, None
              (the 'EQuestState::' prefix is optional).
      If -o is omitted, writes <name>.patched.sav next to the input.

  UeSaveGame.G1R unpack <save.sav> <out.bin>
      Write the raw decompressed property-list body.

  UeSaveGame.G1R dump-json <save.sav> <out.json> [--indent]
      Export the parsed save to JSON.

Oodle runtime (oo2core_9_win64.dll) must be next to the executable, in the
working directory, or pointed to by the OODLE_PATH environment variable.

Examples:
  UeSaveGame.G1R list-quests G1R-023.sav TRIALOFFIRE
  UeSaveGame.G1R set-quest G1R-023.sav -o G1R-023.patched.sav OBJ_WATERFALL=Succeeded");
}
