using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Aiursoft.AptClient.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
[Index(nameof(Package), nameof(Version), nameof(Architecture), nameof(OriginSuite), nameof(OriginComponent))]
[Index(nameof(Filename))]
public class AptPackage : DebianPackage
{
    [Key]
    public int Id { get; set; }

    public int MirrorRepositoryId { get; set; }
    
    [ForeignKey(nameof(MirrorRepositoryId))]
    public MirrorRepository? Mirror { get; set; }
}
