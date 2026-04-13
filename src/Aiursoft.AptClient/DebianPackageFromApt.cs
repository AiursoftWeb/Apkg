namespace Aiursoft.AptClient;

using Abstractions;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// 组合类：包含包本身的信息 + 包的来源信息
/// 对应数据库设计中的核心实体
/// </summary>
[ExcludeFromCodeCoverage]
public class DebianPackageFromApt
{
    public required DebianPackage Package { get; set; }
    public required AptPackageSource Source { get; set; }
}
