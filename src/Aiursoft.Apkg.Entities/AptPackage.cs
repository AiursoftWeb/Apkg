using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Aiursoft.AptClient.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
[Index(nameof(BucketId))]
[Index(nameof(Package), nameof(Version), nameof(Architecture), nameof(Component))]
[Index(nameof(Filename))]
public class AptPackage : DebianPackage
{
    [Key]
    public int Id { get; set; }

    public int BucketId { get; set; }
    
    [ForeignKey(nameof(BucketId))]
    public AptBucket? Bucket { get; set; }

    [Required]
    [MaxLength(100)]
    public required string Component { get; set; }

    // IsVirtual means the binary is not yet in our local pool, needs lazy sync.
    public bool IsVirtual { get; set; } = true;

    // The upstream URL to fetch the binary if it's virtual.
    public string? RemoteUrl { get; set; }
}
