using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Aiursoft.Apkg.Sdk.Models;

public sealed class PackagePreflightRequest
{
    [Required, MaxLength(128)]
    public required string Name { get; init; }

    [Required, MaxLength(100)]
    public required string Distro { get; init; }

    [Required, MaxLength(255)]
    public required string Component { get; init; }

    [Required, MinLength(1), MaxLength(256)]
    public required IReadOnlyList<PackagePreflightTarget> Targets { get; init; }

    public static PackagePreflightRequest FromPlan(PackageBuildPlan plan) => new()
    {
        Name = plan.Name,
        Distro = plan.Distro,
        Component = plan.Component,
        Targets = plan.Targets.Select(target => new PackagePreflightTarget
        {
            Suite = target.Suite,
            Architecture = target.Architecture,
            Version = target.Version
        }).ToList()
    };
}

public sealed class PackagePreflightTarget
{
    [Required, MaxLength(100)]
    public required string Suite { get; init; }

    [Required, MaxLength(128)]
    public required string Architecture { get; init; }

    [Required, MaxLength(128)]
    public required string Version { get; init; }
}

public sealed class PackagePreflightResponse
{
    public required bool AllPresent { get; init; }
    public required IReadOnlyList<PackagePreflightTargetResult> Targets { get; init; }
}

public sealed class PackagePreflightTargetResult
{
    public required string Suite { get; init; }
    public required string Architecture { get; init; }
    public required string Version { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<PackagePreflightStatus>))]
    public required PackagePreflightStatus Status { get; init; }

    public string? Message { get; init; }
    public required IReadOnlyList<PackagePreflightRepositoryResult> Repositories { get; init; }
}

public sealed class PackagePreflightRepositoryResult
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required bool Present { get; init; }
}

public enum PackagePreflightStatus
{
    Present,
    Missing,
    NoRepository,
    Forbidden
}
