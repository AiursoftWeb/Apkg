using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.AptClient.Abstractions;

[ExcludeFromCodeCoverage]
public class DebianPackage
{
    // ==========================================
    // 1. 必需属性 (Required Properties)
    // 根据统计，这些属性出现在 100% 的包中
    // ==========================================

    // 注入的元数据
    public required string OriginSuite { get; set; }
    public required string OriginComponent { get; set; }

    // 包核心标识
    public required string Package { get; set; }
    public required string Version { get; set; }
    public required string Architecture { get; set; }
    public required string Maintainer { get; set; }

    // 描述与元数据
    public required string Description { get; set; }
    public required string DescriptionMd5 { get; set; }
    public required string Section { get; set; }
    public required string Priority { get; set; }
    public required string Origin { get; set; }
    public required string Bugs { get; set; }

    // 文件与校验信息
    public required string Filename { get; set; }
    public required string Size { get; set; } // 虽然是数字，但为了保持原始精度且符合你的string要求，暂存为string
    // ReSharper disable once InconsistentNaming
    public required string MD5sum { get; set; }
    // ReSharper disable once InconsistentNaming
    public required string SHA1 { get; set; }
    // ReSharper disable once InconsistentNaming
    public required string SHA256 { get; set; }
    // ReSharper disable once InconsistentNaming
    public required string SHA512 { get; set; }

    // ==========================================
    // 2. 常用可选属性 (Common Optional Properties)
    // 这些属性覆盖率很高，值得拥有独立字段
    // ==========================================

    public string? InstalledSize { get; set; }      // 99.9%
    public string? OriginalMaintainer { get; set; } // 94.5%
    public string? Homepage { get; set; }           // 92.2%
    public string? Depends { get; set; }            // 87.7%
    public string? Source { get; set; }             // 69.5%
    public string? MultiArch { get; set; }          // 38.4%

    // 依赖关系全家桶
    public string? Provides { get; set; }
    public string? Suggests { get; set; }
    public string? Recommends { get; set; }
    public string? Conflicts { get; set; }
    public string? Breaks { get; set; }
    public string? Replaces { get; set; }

    // ==========================================
    // 3. 兜底属性 (The Long Tail)
    // 存放所有未被显式定义的字段 (如 Ruby-Versions, Python-Egg-Name 等)
    // ==========================================
    public Dictionary<string, string> Extras { get; set; } = new();
}
