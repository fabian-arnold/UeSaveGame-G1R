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

namespace UeSaveGame.PropertyTypes
{
	public class ArrayProperty : FProperty<Array>
	{
		internal FPropertyTag? StructPrototype { get; set; }

		public FPropertyTypeName? ItemType { get; set; }

		/// <summary>
		/// Raw bytes of the element region for arrays whose elements carry extra inline data that
		/// the standard per-element readers do not consume (e.g. arrays of instanced/export objects,
		/// where each element is a class path followed by an inline property list). When set, the
		/// array is written back verbatim from these bytes to guarantee a lossless round trip.
		/// </summary>
		public byte[]? RawData { get; set; }

		public ArrayProperty(FString name)
			: base(name)
		{
		}

		public ArrayProperty(FString name, FPropertyTypeName itemType)
			: this(name)
		{
			ItemType = itemType;
		}

		public ArrayProperty(FString name, FPropertyTypeName itemType, FPropertyTag structPrototype)
			: this(name, itemType)
		{
			StructPrototype = structPrototype;
		}

		protected internal override void ProcessTypeName(FPropertyTypeName typeName, PackageVersion packageVersion)
		{
			if (packageVersion >= EObjectUE5Version.PROPERTY_TAG_COMPLETE_TYPE_NAME)
			{
				if (typeName.Parameters.Count != 1)
				{
					throw new InvalidDataException("Failed to read item type for ArrayProperty");
				}
				ItemType = typeName.Parameters[0];
			}
		}

		protected internal override void DeserializeHeader(BinaryReader reader, PackageVersion packageVersion)
		{
			if (packageVersion < EObjectUE5Version.PROPERTY_TAG_COMPLETE_TYPE_NAME)
			{
				ItemType = new(reader.ReadUnrealString()!);
			}
		}

		protected internal override void DeserializeValue(BinaryReader reader, int size, PackageVersion packageVersion)
		{
			if (ItemType == null) throw new InvalidOperationException("Cannot read array with unknown item type");

			int count = reader.ReadInt32();

			long elementStart = reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;

			Array? data;
			StructPrototype = ArraySerializationHelper.Deserialize(reader, count, size - 4, ItemType, packageVersion, out data);
			Value = data;

			// If the standard readers did not consume exactly the declared element region, the
			// elements carry extra inline data (e.g. instanced/export objects). Capture the region
			// verbatim so it can be written back losslessly, and realign the stream.
			if (elementStart >= 0 && size >= 4)
			{
				long consumed = reader.BaseStream.Position - elementStart;
				if (consumed != size - 4)
				{
					reader.BaseStream.Seek(elementStart, SeekOrigin.Begin);
					RawData = reader.ReadBytes(size - 4);
				}
			}
		}

		protected internal override void SerializeHeader(BinaryWriter writer, PackageVersion packageVersion)
		{
			if (packageVersion < EObjectUE5Version.PROPERTY_TAG_COMPLETE_TYPE_NAME)
			{
				writer.WriteUnrealString(ItemType!.Name);
			}
		}

		protected internal override int SerializeValue(BinaryWriter writer, PackageVersion packageVersion)
		{
			if (Value == null) throw new InvalidOperationException("Instance is not valid for serialization");
			if (ItemType == null) throw new InvalidOperationException("Cannot serialize array with unknown item type");

			// Arrays captured verbatim (inline object data) are written straight back.
			if (RawData != null)
			{
				writer.Write(Value.Length);
				writer.Write(RawData);
				return 4 + RawData.Length;
			}

			int size = 4;
			writer.Write(Value.Length);

			size += ArraySerializationHelper.Serialize(writer, ItemType, packageVersion, StructPrototype, Value);

			return size;
		}

		public override string? ToString()
		{
			string? valueString = Value?.Length == 1 && Value.GetValue(0) != null ? Value.GetValue(0)!.ToString() : $"Count = {Value?.Length ?? 0}";
			return $"<{ItemType}> {valueString}";
		}
	}
}
