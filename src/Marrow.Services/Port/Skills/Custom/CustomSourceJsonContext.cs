using System.Text.Json.Serialization;

namespace Marrow.Services.Port.Skills.Custom;

[JsonSerializable(typeof(OwnItem[]))]
internal sealed partial class CustomSourceJsonContext : JsonSerializerContext;
