// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// <auto-generated/>

#nullable disable

using System.Text.Json;
using Azure.Core;

namespace Azure.ResourceManager.MachineLearning.Models
{
    public partial class IdentityConfiguration : IUtf8JsonSerializable
    {
        void IUtf8JsonSerializable.Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("identityType");
            writer.WriteStringValue(IdentityType.ToString());
            writer.WriteEndObject();
        }

        internal static IdentityConfiguration DeserializeIdentityConfiguration(JsonElement element)
        {
            if (element.TryGetProperty("identityType", out JsonElement discriminator))
            {
                switch (discriminator.GetString())
                {
                    case "AMLToken": return AmlToken.DeserializeAmlToken(element);
                    case "Managed": return ManagedIdentity.DeserializeManagedIdentity(element);
                    case "UserIdentity": return UserIdentity.DeserializeUserIdentity(element);
                }
            }
            IdentityConfigurationType identityType = default;
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("identityType"))
                {
                    identityType = new IdentityConfigurationType(property.Value.GetString());
                    continue;
                }
            }
            return new IdentityConfiguration(identityType);
        }
    }
}
