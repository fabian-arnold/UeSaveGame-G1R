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

using Newtonsoft.Json;
using UeSaveGame.StructData;

namespace UeSaveGame.Json.StructDataSerializers
{
	public class InstancedStructSerializer : StructDataSerializerBase
	{
		public override IEnumerable<string> StructTypes
		{
			get
			{
				yield return "InstancedStruct";
			}
		}

		public override void ToJson(IStructData? data, JsonWriter writer)
		{
			if (data is null)
			{
				writer.WriteNull();
				return;
			}

			InstancedStructData instanced = (InstancedStructData)data;

			writer.WriteStartObject();

			writer.WritePropertyName(nameof(InstancedStructData.StructPath));
			writer.WriteFStringValue(instanced.StructPath);

			writer.WritePropertyName(nameof(InstancedStructData.Properties));
			PropertiesSerializer.ToJson(instanced.Properties, writer);

			writer.WriteEndObject();
		}

		public override IStructData? FromJson(JsonReader reader)
		{
			InstancedStructData instanced = new();

			while (reader.Read())
			{
				if (reader.TokenType == JsonToken.EndObject)
				{
					break;
				}

				if (reader.TokenType == JsonToken.PropertyName)
				{
					switch ((string)reader.Value!)
					{
						case nameof(InstancedStructData.StructPath):
							instanced.StructPath = reader.ReadAsFString();
							break;
						case nameof(InstancedStructData.Properties):
							if (reader.ReadAndMoveToContent())
							{
								instanced.Properties = PropertiesSerializer.FromJson(reader);
							}
							break;
					}
				}
			}

			return instanced;
		}
	}
}
