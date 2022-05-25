// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// <auto-generated/>

#nullable disable

using System;
using System.Text.Json;
using Azure.Core;

namespace Azure.ResourceManager.MachineLearning.Models
{
    public partial class TableVerticalLimitSettings : IUtf8JsonSerializable
    {
        void IUtf8JsonSerializable.Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            if (Optional.IsDefined(EnableEarlyTermination))
            {
                writer.WritePropertyName("enableEarlyTermination");
                writer.WriteBooleanValue(EnableEarlyTermination.Value);
            }
            if (Optional.IsDefined(ExitScore))
            {
                if (ExitScore != null)
                {
                    writer.WritePropertyName("exitScore");
                    writer.WriteNumberValue(ExitScore.Value);
                }
                else
                {
                    writer.WriteNull("exitScore");
                }
            }
            if (Optional.IsDefined(MaxConcurrentTrials))
            {
                writer.WritePropertyName("maxConcurrentTrials");
                writer.WriteNumberValue(MaxConcurrentTrials.Value);
            }
            if (Optional.IsDefined(MaxCoresPerTrial))
            {
                writer.WritePropertyName("maxCoresPerTrial");
                writer.WriteNumberValue(MaxCoresPerTrial.Value);
            }
            if (Optional.IsDefined(MaxTrials))
            {
                writer.WritePropertyName("maxTrials");
                writer.WriteNumberValue(MaxTrials.Value);
            }
            if (Optional.IsDefined(Timeout))
            {
                writer.WritePropertyName("timeout");
                writer.WriteStringValue(Timeout.Value, "P");
            }
            if (Optional.IsDefined(TrialTimeout))
            {
                writer.WritePropertyName("trialTimeout");
                writer.WriteStringValue(TrialTimeout.Value, "P");
            }
            writer.WriteEndObject();
        }

        internal static TableVerticalLimitSettings DeserializeTableVerticalLimitSettings(JsonElement element)
        {
            Optional<bool> enableEarlyTermination = default;
            Optional<double?> exitScore = default;
            Optional<int> maxConcurrentTrials = default;
            Optional<int> maxCoresPerTrial = default;
            Optional<int> maxTrials = default;
            Optional<TimeSpan> timeout = default;
            Optional<TimeSpan> trialTimeout = default;
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("enableEarlyTermination"))
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        property.ThrowNonNullablePropertyIsNull();
                        continue;
                    }
                    enableEarlyTermination = property.Value.GetBoolean();
                    continue;
                }
                if (property.NameEquals("exitScore"))
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        exitScore = null;
                        continue;
                    }
                    exitScore = property.Value.GetDouble();
                    continue;
                }
                if (property.NameEquals("maxConcurrentTrials"))
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        property.ThrowNonNullablePropertyIsNull();
                        continue;
                    }
                    maxConcurrentTrials = property.Value.GetInt32();
                    continue;
                }
                if (property.NameEquals("maxCoresPerTrial"))
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        property.ThrowNonNullablePropertyIsNull();
                        continue;
                    }
                    maxCoresPerTrial = property.Value.GetInt32();
                    continue;
                }
                if (property.NameEquals("maxTrials"))
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        property.ThrowNonNullablePropertyIsNull();
                        continue;
                    }
                    maxTrials = property.Value.GetInt32();
                    continue;
                }
                if (property.NameEquals("timeout"))
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        property.ThrowNonNullablePropertyIsNull();
                        continue;
                    }
                    timeout = property.Value.GetTimeSpan("P");
                    continue;
                }
                if (property.NameEquals("trialTimeout"))
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        property.ThrowNonNullablePropertyIsNull();
                        continue;
                    }
                    trialTimeout = property.Value.GetTimeSpan("P");
                    continue;
                }
            }
            return new TableVerticalLimitSettings(Optional.ToNullable(enableEarlyTermination), Optional.ToNullable(exitScore), Optional.ToNullable(maxConcurrentTrials), Optional.ToNullable(maxCoresPerTrial), Optional.ToNullable(maxTrials), Optional.ToNullable(timeout), Optional.ToNullable(trialTimeout));
        }
    }
}
