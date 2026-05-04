using System.ComponentModel.DataAnnotations;

namespace Aiursoft.Apkg.Entities;

public class DependencyCheckReport
{
    [Key]
    public int Id { get; set; }

    public int RepositoryId { get; set; }
    public AptRepository Repository { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpireAt { get; set; } = DateTime.UtcNow.AddHours(72);

    /// <summary>
    /// Total number of packages checked (including virtual packages)
    /// </summary>
    public int TotalPackages { get; set; }

    /// <summary>
    /// Number of packages with unmet dependencies
    /// </summary>
    public int ProblematicPackages { get; set; }

    /// <summary>
    /// JSON array of detailed dependency issues
    /// Format: [{ "Package": "foo", "Version": "1.0", "Architecture": "amd64", "MissingDeps": [{ "Name": "bar", "Required": ">= 1.3.0", "Available": "1.2.0" }] }]
    /// </summary>
    [MaxLength(int.MaxValue)]
    public string? DetailsJson { get; set; }

    /// <summary>
    /// Check execution status: Running, Completed, Failed
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Running";

    /// <summary>
    /// Error message if Status is Failed
    /// </summary>
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
}
