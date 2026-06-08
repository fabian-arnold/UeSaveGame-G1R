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

using UeSaveGame.Util;

namespace UeSaveGame.StructData
{
	/// <summary>
	/// Handles FInstancedStruct (/Script/StructUtils.InstancedStruct).
	/// </summary>
	/// <remarks>
	/// An instanced struct uses native/binary serialization (the owning property tag has the
	/// HasBinaryOrNativeSerialize flag set). Its value is self describing:
	///
	///   [FString  concrete struct path]   e.g. "/Script/G1R.ProfileData"
	///   [int32    serialized data size]   size in bytes of the property list that follows
	///   [property list ... None]          standard tagged property serialization
	///
	/// An empty instanced struct (no underlying struct) still serializes both leading fields:
	/// an empty (null) path followed by a zero size, and no property list.
	/// </remarks>
	public class InstancedStructData : BaseStructData
	{
		/// <summary>
		/// The path of the concrete struct type held by this instanced struct, or null if empty.
		/// </summary>
		public FString? StructPath { get; set; }

		/// <summary>
		/// The properties of the contained struct.
		/// </summary>
		public IList<FPropertyTag> Properties { get; set; }

		public override IEnumerable<string> StructTypes
		{
			get
			{
				yield return "InstancedStruct";
			}
		}

		public InstancedStructData()
		{
			Properties = new List<FPropertyTag>();
		}

		public override void Deserialize(BinaryReader reader, int size, PackageVersion packageVersion)
		{
			StructPath = reader.ReadUnrealString();

			// Serialized size of the property list that follows. Always present (zero for an
			// empty instanced struct). Not needed to read the data (the list is None terminated)
			// but consumed to stay aligned.
			int serialSize = reader.ReadInt32();

			Properties = new List<FPropertyTag>();
			if (StructPath?.Value != null && serialSize > 0)
			{
				Properties = new List<FPropertyTag>(PropertySerializationHelper.ReadProperties(reader, packageVersion, false));
			}
		}

		public override int Serialize(BinaryWriter writer, PackageVersion packageVersion)
		{
			long startPosition = writer.BaseStream.Position;

			writer.WriteUnrealString(StructPath);

			long sizeOffset = writer.BaseStream.Position;
			writer.Write(0); // placeholder for serialized data size

			int dataSize = 0;
			if (StructPath?.Value != null)
			{
				// A non-empty instanced struct always writes a (None terminated) property list,
				// even when it contains no properties.
				long dataStart = writer.BaseStream.Position;
				PropertySerializationHelper.WriteProperties(Properties, writer, packageVersion, false);
				dataSize = (int)(writer.BaseStream.Position - dataStart);
			}

			long endPosition = writer.BaseStream.Position;
			writer.BaseStream.Seek(sizeOffset, SeekOrigin.Begin);
			writer.Write(dataSize);
			writer.BaseStream.Seek(endPosition, SeekOrigin.Begin);

			return (int)(endPosition - startPosition);
		}

		public override string? ToString()
		{
			return $"[{StructPath?.Value ?? "Empty"}] {Properties.Count} Properties";
		}
	}
}
