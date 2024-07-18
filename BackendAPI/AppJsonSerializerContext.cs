using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace BackendAPI;

[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(ProblemDetails))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}