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

using System.Reflection;
using System.Runtime.InteropServices;

namespace UeSaveGame.G1R
{
	/// <summary>
	/// Minimal wrapper around the Oodle data compression runtime (oo2core).
	/// </summary>
	/// <remarks>
	/// Oodle is proprietary and not redistributable. Provide the DLL by either placing
	/// oo2core_9_win64.dll next to the executable, in the working directory, or by setting
	/// the OODLE_PATH environment variable to the full path of the DLL.
	/// </remarks>
	internal static class Oodle
	{
		// Compressor / level / thread-phase constants (see oodle2.h)
		public const int Kraken = 8;
		public const int LevelNormal = 4;
		private const int FuzzSafeYes = 1;
		private const int Unthreaded = 3;

		static Oodle()
		{
			NativeLibrary.SetDllImportResolver(typeof(Oodle).Assembly, Resolve);
		}

		/// <summary>Triggers the static constructor / resolver registration.</summary>
		public static void EnsureInitialized() { }

		private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			if (!libraryName.StartsWith("oo2core", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;

			foreach (string candidate in Candidates())
			{
				if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
				{
					return handle;
				}
			}
			throw new DllNotFoundException(
				"Could not locate the Oodle runtime (oo2core_9_win64.dll). Place it next to the " +
				"executable or in the working directory, or set the OODLE_PATH environment variable.");
		}

		private static IEnumerable<string> Candidates()
		{
			string? env = Environment.GetEnvironmentVariable("OODLE_PATH");
			if (!string.IsNullOrEmpty(env)) yield return env;

			string[] names = { "oo2core_9_win64.dll", "oo2core_8_win64.dll", "oo2core_win64.dll" };
			string[] dirs = { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
			foreach (string dir in dirs)
				foreach (string name in names)
					yield return Path.Combine(dir, name);
		}

		/// <summary>Decompresses a single Oodle block to exactly <paramref name="rawLen"/> bytes.</summary>
		public static byte[] Decompress(byte[] compressed, int rawLen)
		{
			byte[] raw = new byte[rawLen];
			long got = OodleLZ_Decompress(compressed, compressed.Length, raw, rawLen,
				FuzzSafeYes, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, Unthreaded);
			if (got != rawLen) throw new InvalidDataException($"Oodle decompress failed (returned {got}, expected {rawLen}).");
			return raw;
		}

		/// <summary>Compresses a single block. Returns the compressed bytes.</summary>
		public static byte[] Compress(byte[] raw, int compressor = Kraken, int level = LevelNormal)
		{
			byte[] buffer = new byte[raw.Length + 4096]; // generous slack for worst-case overhead
			long len = OodleLZ_Compress(compressor, raw, raw.Length, buffer, level,
				IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
			if (len <= 0) throw new InvalidDataException("Oodle compress failed.");
			byte[] result = new byte[len];
			Array.Copy(buffer, result, (int)len);
			return result;
		}

		[DllImport("oo2core", CallingConvention = CallingConvention.Cdecl)]
		private static extern long OodleLZ_Decompress(
			byte[] compBuf, long compBufSize, byte[] rawBuf, long rawLen,
			int fuzzSafe, int checkCRC, int verbosity,
			IntPtr decBufBase, long decBufSize, IntPtr fpCallback, IntPtr callbackUserData,
			IntPtr decoderMemory, long decoderMemorySize, int threadPhase);

		[DllImport("oo2core", CallingConvention = CallingConvention.Cdecl)]
		private static extern long OodleLZ_Compress(
			int compressor, byte[] rawBuf, long rawLen, byte[] compBuf, int level,
			IntPtr options, IntPtr dictionaryBase, IntPtr lrm, IntPtr scratchMem, long scratchSize);
	}
}
