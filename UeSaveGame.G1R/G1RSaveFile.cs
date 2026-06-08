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
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using UeSaveGame.Util;

namespace UeSaveGame.G1R
{
	/// <summary>
	/// Reads, edits and writes Gothic 1 Remake (G1R) save files.
	/// </summary>
	/// <remarks>
	/// Container layout (all integers little-endian):
	///   "GSAV" magic (4)
	///   u8  version (= 2)
	///   u32 dataEnd            (offset of the trailing footer = file size - footer length)
	///   u32 customPayloadSize  (size of the uncompressed CustomPayload property block)
	///   CustomPayload property block (uncompressed)
	///   Compressed section:
	///     i64 totalUncompressed
	///     FString "Oodle"
	///     i32 0x9E2A83C1 (PACKAGE_FILE_TAG) | i32 0x22222222 | i64 blockSize | u8 flags(=2)
	///     i64 totalCompressed | i64 totalUncompressed
	///     chunk table: per block (i64 compressedSize, i64 uncompressedSize)
	///     Oodle compressed blocks
	///   44-byte footer (two save-id GUIDs; reused verbatim)
	///
	/// The compressed payload is a standard UE5 property list:
	///   FString SaveClass | u8 0 | property list (None terminated + trailing int32)
	/// </remarks>
	public sealed class G1RSaveFile
	{
		private const uint PackageFileTag = 0x9E2A83C1;
		private const uint Marker = 0x22222222;
		private const long BlockSize = 131072;
		private const int FooterLength = 44;

		/// <summary>The save game class path (e.g. /Script/Angelscript.GothicFinalDataGame).</summary>
		public FString? SaveClass { get; set; }

		/// <summary>The top level property list of the save.</summary>
		public IList<FPropertyTag> Properties { get; set; } = new List<FPropertyTag>();

		// Preserved container pieces for a faithful round trip.
		private byte[] mPrefix = Array.Empty<byte>();   // GSAV header + CustomPayload (verbatim)
		private byte[] mFooter = Array.Empty<byte>();   // trailing 44 bytes (verbatim)
		private byte mFlags;
		private byte mUnknownByte;
		private PackageVersion mVersion = PackageVersion.LatestTested;

		private G1RSaveFile() { }

		/// <summary>Loads and fully parses a G1R save file.</summary>
		public static G1RSaveFile Load(string path)
		{
			Oodle.EnsureInitialized();
			byte[] file = File.ReadAllBytes(path);
			G1RSaveFile save = new();

			int magic = IndexOfUInt32(file, PackageFileTag);
			if (magic < 0) throw new InvalidDataException("Not a recognized GSAV/Oodle save (compression magic not found).");

			int oodle = IndexOfAscii(file, "Oodle");
			if (oodle < 0) throw new InvalidDataException("Compression format marker 'Oodle' not found.");
			int sectionStart = oodle - 4 /*FString len*/ - 8 /*leading i64*/;

			// chunk table = first run of (x, BlockSize) i64 pairs after the magic
			int table = -1;
			for (int t = magic + 4; t < magic + 80; t++)
			{
				bool ok = true;
				for (int k = 0; k < 6; k++)
					if (t + 16 * k + 16 > file.Length || BitConverter.ToInt64(file, t + 16 * k + 8) != BlockSize) { ok = false; break; }
				if (ok) { table = t; break; }
			}
			if (table < 0) throw new InvalidDataException("Could not locate the Oodle chunk table.");

			var comp = new List<long>();
			var unc = new List<long>();
			int p = table;
			while (p + 16 <= file.Length)
			{
				long c = BitConverter.ToInt64(file, p);
				long u = BitConverter.ToInt64(file, p + 8);
				comp.Add(c); unc.Add(u); p += 16;
				if (u != BlockSize) break; // remainder block ends the table
			}
			int dataStart = p;

			byte[] body = new byte[unc.Sum()];
			long outPos = 0; int inPos = dataStart;
			for (int b = 0; b < comp.Count; b++)
			{
				byte[] src = new byte[comp[b]];
				Array.Copy(file, inPos, src, 0, (int)comp[b]);
				byte[] dst = Oodle.Decompress(src, (int)unc[b]);
				Array.Copy(dst, 0, body, outPos, dst.Length);
				outPos += dst.Length; inPos += (int)comp[b];
			}
			int dataEnd = inPos;

			save.mPrefix = file[0..sectionStart];
			save.mFlags = file[magic + 16];
			save.mFooter = file[dataEnd..];
			if (save.mFooter.Length != FooterLength)
				throw new InvalidDataException($"Unexpected footer length {save.mFooter.Length} (expected {FooterLength}).");

			using var ms = new MemoryStream(body);
			using var reader = new BinaryReader(ms, Encoding.ASCII, true);
			save.SaveClass = reader.ReadUnrealString();
			save.mUnknownByte = reader.ReadByte();
			save.Properties = new List<FPropertyTag>(
				PropertySerializationHelper.ReadProperties(reader, save.mVersion, true));

			return save;
		}

		/// <summary>Serializes the current property list back into the decompressed body bytes.</summary>
		public byte[] SerializeBody()
		{
			using var ms = new MemoryStream();
			using var writer = new BinaryWriter(ms, Encoding.ASCII, true);
			writer.WriteUnrealString(SaveClass);
			writer.Write(mUnknownByte);
			PropertySerializationHelper.WriteProperties(Properties, writer, mVersion, true);
			writer.Flush();
			return ms.ToArray();
		}

		/// <summary>Repacks and writes the save to <paramref name="path"/>.</summary>
		public void Save(string path)
		{
			byte[] body = SerializeBody();

			var comp = new List<byte[]>();
			var unc = new List<int>();
			for (long off = 0; off < body.Length; off += BlockSize)
			{
				int n = (int)Math.Min(BlockSize, body.Length - off);
				byte[] raw = new byte[n];
				Array.Copy(body, off, raw, 0, n);
				comp.Add(Oodle.Compress(raw));
				unc.Add(n);
			}
			long totalComp = comp.Sum(b => (long)b.Length);

			byte[] output;
			using (var ms = new MemoryStream())
			using (var w = new BinaryWriter(ms))
			{
				w.Write(mPrefix);
				w.Write((long)body.Length);                       // leading totalUncompressed
				w.Write(6); w.Write(Encoding.ASCII.GetBytes("Oodle\0"));
				w.Write(PackageFileTag);
				w.Write(Marker);
				w.Write(BlockSize);
				w.Write(mFlags);
				w.Write(totalComp);
				w.Write((long)body.Length);                       // summary totalUncompressed
				for (int i = 0; i < comp.Count; i++) { w.Write((long)comp[i].Length); w.Write((long)unc[i]); }
				foreach (byte[] b in comp) w.Write(b);
				w.Write(mFooter);
				w.Flush();
				output = ms.ToArray();
			}

			// Update the GSAV header dataEnd field (u32 @ offset 5) to point at the new footer.
			uint dataEnd = (uint)(output.Length - mFooter.Length);
			BitConverter.GetBytes(dataEnd).CopyTo(output, 5);

			File.WriteAllBytes(path, output);
		}

		/// <summary>
		/// Finds every quest CurrentState enum in the save, paired with the quest's path/key.
		/// </summary>
		public IReadOnlyList<QuestState> FindQuestStates()
		{
			var result = new List<QuestState>();
			void WalkList(IEnumerable<FPropertyTag> props, string context)
			{
				foreach (FPropertyTag tag in props) Walk(tag.Property, tag.Name?.Value ?? string.Empty, context);
			}
			void Walk(FProperty? prop, string name, string context)
			{
				switch (prop)
				{
					case EnumProperty e when name == "CurrentState" && (e.Value?.Value?.StartsWith("EQuestState::") ?? false):
						result.Add(new QuestState(context, e));
						break;
					case StructProperty s when s.Value is InstancedStructData isd:
						WalkList(isd.Properties, context); break;
					case StructProperty s2 when s2.Value is PropertiesStruct ps:
						WalkList(ps.Properties, context); break;
					case MapProperty m when m.Value != null:
						foreach (var kv in m.Value)
						{
							string key = (kv.Key as ObjectProperty)?.ObjectType?.Value ?? kv.Key.ToString() ?? string.Empty;
							Walk(kv.Value, name, key);
						}
						break;
					case ArrayProperty arr when arr.Value is FProperty[] items:
						foreach (FProperty item in items) Walk(item, name, context);
						break;
				}
			}
			WalkList(Properties, "<root>");
			return result;
		}

		/// <summary>Normalizes a state name to the full EQuestState::&lt;Name&gt; form.</summary>
		public static string NormalizeState(string state) =>
			state.Contains("::") ? state : "EQuestState::" + state;

		private static int IndexOfUInt32(byte[] data, uint value)
		{
			for (int i = 0; i + 4 <= data.Length; i++)
				if (BitConverter.ToUInt32(data, i) == value) return i;
			return -1;
		}

		private static int IndexOfAscii(byte[] data, string s)
		{
			byte[] pat = Encoding.ASCII.GetBytes(s);
			for (int i = 0; i + pat.Length <= data.Length; i++)
			{
				bool ok = true;
				for (int j = 0; j < pat.Length; j++) if (data[i + j] != pat[j]) { ok = false; break; }
				if (ok) return i;
			}
			return -1;
		}
	}

	/// <summary>A quest's CurrentState enum together with the quest path it belongs to.</summary>
	public sealed record QuestState(string Key, EnumProperty Property)
	{
		public string? State => Property.Value?.Value;
	}
}
