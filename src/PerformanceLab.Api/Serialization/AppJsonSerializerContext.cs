using System.Text.Json.Serialization;
using PerformanceLab.Application.Users.Models;
using PerformanceLab.Shared.DTOs;

namespace PerformanceLab.Api.Serialization;

/// <summary>
/// JSON source generator context for compile-time serialization code generation.
/// Eliminates reflection overhead and reduces allocations during JSON serialization.
/// 
/// IMPORTANT: When adding new DTOs that are returned from API endpoints,
/// add a [JsonSerializable(typeof(NewDto))] attribute here to generate serialization code.
/// </summary>
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(UserDto[]))]
[JsonSerializable(typeof(List<UserDto>))]
[JsonSerializable(typeof(IReadOnlyList<UserDto>))]
[JsonSerializable(typeof(PagedResult<UserDto>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default
)]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
