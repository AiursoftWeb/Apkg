# Apkg 设计文档

## 1. 战略愿景

Apkg 是 AnduinOS 的 APT 包管理平台 — APT 仓库服务器 + CLI 构建工具链。构建 Apkg 旨在为 AnduinOS 建立三大战略护城河：

1. **供应链主权**：通过服务端 Override 中间件，零成本清洗上游 Ubuntu 数据 — 剔除 Snap 壳、替换包、修改元数据 — 彻底终结脆弱的客户端脚本修补方案。
2. **开发者生态**：`.aosproj` 引入 MSBuild 风格的声明式构建格式，将 Linux 打包学习成本降低 90%。
3. **资产安全**：完全控制全球分发节点，快速事故响应 — 约 30 分钟内从所有镜像下架恶意包。

> Apkg 不是一个包管理器，而是一个 **meta-distro 构建平台** — 通过将变更表达为 Override 规则而非 fork 补丁来维护 Linux 发行版。

## 2. 核心架构

```
┌─────────────┐     ┌──────────────────┐     ┌─────────────────┐     ┌──────────────┐
│  Upstream   │ ──▶ │   AptMirror      │ ──▶ │  AptRepository  │ ──▶ │  APT Client  │
│  (Ubuntu)   │     │  (ingress)       │     │  (egress)       │     │  (apt update)│
└─────────────┘     └──────────────────┘     └─────────────────┘     └──────────────┘
                           │                          │
                     Mirror Bucket              Repository Bucket
                     (raw upstream              (Override'd + signed
                      metadata)                  metadata)
```

**AptMirror** — 数据入口，从上游同步整个 Suite（所有 components + architectures）到一个 Mirror Bucket。

**AptRepository** — 数据出口，APT 客户端直接连接的对象。每个 Repository 有独立的 GPG 签名密钥，可选应用 Override 规则。

**AptBucket** — 版本化快照容器。存放一个时间点的完整包元数据。切换 Bucket 是原子的指针操作。

**AptPackage** — Bucket 内的单条包记录。含 Package、Version、Architecture、Component、SHA256 以及 `IsVirtual` 标记（支持懒加载二进制下载）。

### 数据流

```
MirrorSyncJob → Mirror.Bucket (raw upstream)
     │
     ▼
RepositorySyncJob → 从 Mirror.Primary 复制 → 应用 LocalPackage 覆盖 → 生成 Release → Repo.Secondary.Bucket
     │
     ▼
RepositorySignJob → GPG 签名 → 原子 swap: Repo.Primary = Repo.Secondary
     │
     ▼
Controller → 从 Repo.Primary.Bucket 极速响应已签名的 InRelease / Packages.gz
     │
     ▼
Lazy Sync → 用户下载包时虚转实（下载 .deb → 写入 CAS → 更新 IsVirtual）
```

## 3. Pipeline：四大后台任务

### 3.1 四位演员

| 任务 | 职责 | 触发 |
|------|------|------|
| `MirrorSyncJob` | 从上游拉取包列表，写入 Mirror Primary Bucket | 定时 / 手动 |
| `RepositorySyncJob` | 从 Mirror Primary 复制数据，生成 Release，挂到 Repo Secondary | 定时 / 手动 |
| `RepositorySignJob` | 对 Secondary Bucket GPG 签名，原子升级为 Primary | 定时 / 手动 |
| `GarbageCollectionJob` | 清理所有非 Primary/Secondary 的 orphan bucket | 定时 / 手动 |

### 3.2 核心数据结构

```
AptMirror
  PrimaryBucketId  → 当前对外服务的 Mirror 快照
  SecondaryBucketId → 正在拉取中的新快照（保护区）

AptRepository
  PrimaryBucketId  → 当前对外服务的已签名快照（APT 客户端只看这个）
  SecondaryBucketId → 已构建但未签名的新快照（等待 SignJob）

AptBucket
  ReleaseContent   → 未签名的 Release 文件原文
  InReleaseContent → GPG 签名后的 InRelease（null = 尚未签名）
  SignedAt         → 签名时间戳
```

### 3.3 关键不变量

**APT 客户端永远只读 Primary Bucket，Secondary 对外完全不可见。** 无论 Secondary 处于什么状态（构建中、已构建未签名、半写入），用户 `apt update` 始终看到一致的已签名快照。

1. **只有 SignJob 能写 Repo.PrimaryBucketId** — 整个系统安全的根基
2. **Secondary 是保护区，不是公开区** — 对外不可见
3. **GC 的边界由引用关系决定，不依赖时间戳** — 消除了"2小时宽限期"等时间魔法数字。任何被 Mirror 或 Repository 引用的 Bucket（Primary 或 Secondary）都不会被 GC 删除
4. **导航属性单次 SaveChanges** — EF Core 保证 `repo.SecondaryBucket = bucket` 在单个事务中完成 INSERT 和 FK 更新，消除孤儿窗口
5. **每个任务独立可重试** — 任何任务崩溃后重启，都能从正确状态继续
6. **Mirror 升级时旧 Primary 留在 Secondary** — 保护正在流式读取旧 Mirror 数据的 RepositorySyncJob，防止其游标被 GC 截断

### 3.4 极端场景分析

| 场景 | 结果 |
|------|------|
| SyncJob 中途崩溃 | Secondary 有引用不被 GC 删；Primary 不变，用户仍用旧版 |
| SyncJob 完成，SignJob 还没跑 | Secondary 有数据但未签名，Primary 不变；用户仍用旧版 |
| SignJob 先于 SyncJob 手动触发 | `ReleaseContent == null`，守卫跳过，不升级 |
| GC 在 SyncJob 创建 bucket 的同时运行 | 单次 SaveChanges 保证原子性，无窗口 |
| GC 在 SignJob 升级后运行 | 旧 Primary 失去引用被正常 GC；新 Primary 有引用，安全 |
| SignJob 和 GC 并发（Mode A） | GC active set 包含 Secondary，不会删 |
| SignJob 和 GC 并发（Mode B） | GC 这轮已计算好 active set，不会误删新 Primary |
| 用户 apt update 期间 SignJob 运行 | APT 读 Primary，SignJob 写 Secondary；无冲突 |
| MirrorSyncJob 升级时 RepositorySyncJob 正在读旧 Mirror | 旧 Primary 保留在 Mirror.Secondary，GC 不删，游标不被截断 |

### 3.5 IsVirtual 生命周期：虚包与实包

| 状态 | IsVirtual | 磁盘文件 | 说明 |
|------|-----------|---------|------|
| 虚包 | `true` | 无 | 仅有元数据，二进制未下载 |
| 实包 | `false` | `Objects/{sha256[..2]}/{sha256}.deb` | CAS 存储中有对应 .deb |

**虚→实转换（懒加载）**：APT 客户端请求下载时，若 CAS 文件存在 → 快速路径直接返回 + 批量修复 IsVirtual；若不存在 → 从 RemoteUrl 下载 → 写入 CAS → 修复 IsVirtual。

**Re-sync 保护**：RepositorySyncJob 构建新 Bucket 前，先从当前 PrimaryBucket 读取所有 `IsVirtual=false` 的 SHA256，存入 `previouslyRealHashes`。复制阶段若包 SHA256 在集合中则保持 `IsVirtual=false`，不重复下载。

**GC 与 IsVirtual**：GC 不检查 IsVirtual，只看 SHA256 引用计数。活跃 Bucket 中任意 AptPackage.SHA256 引用 + LocalPackage.SHA256 引用 → 保留。无引用 → 删除。

### 3.6 哈希不错位的不变量

| 不变量 | 实现方式 |
|--------|---------|
| SHA256 从不在代码中重新计算 | 上传时客户端计算存入 LocalPackage.SHA256；上游拉取时直接复制上游声明值 |
| LocalPackage → AptPackage 字段原封不动 | RepositorySyncJob 直接赋值 SHA256，IsVirtual=false，RemoteUrl=null |
| 替换精确到 (Package, Architecture) | `WHERE Package = lp.Package AND Architecture = lp.Architecture`，不影响其他架构 |
| CAS 文件名 = SHA256 | .deb 以 SHA256 命名；GC 删文件时对比所有引用，只删无引用文件 |
| 禁用的 LocalPackage 完全不可见 | `WHERE IsEnabled = true` 守卫在写入路径；上游版本原样保留 |

### 3.7 核心测试保护的不变量

5 个测试类保护上述不变量不被回归破坏：

| 测试类 | 保护的不变量 |
|--------|------------|
| `AtomicBucketCreationTests` | 导航属性单次 SaveChanges 原子完成 INSERT + FK 更新；验证旧两阶段保存模式会被 GC 在窗口期误删 |
| `GcSignRaceConditionTests` | GC active set 包含 SecondaryBucketId；回归覆盖 Mode A（GC 删未签名 bucket → 仓库永久为空）和 Mode B（FK 约束冲突）两个生产事故 |
| `RepositorySignJobTests` | 未签名 bucket 绝不对 APT 客户端可见 — HTTP 端点验证未签名前返回 404，签名后才返回 GPG clearsigned 内容 |
| `RepositorySyncLocalPackagesTests` | LocalPackage 精确替换同 (Package, Architecture)；IsEnabled 守卫；元数据忠实传递；非冲突包共存；Re-sync 时 `previouslyRealHashes` 保护 IsVirtual 状态 |
| `BackgroundJobsTests` | 任务队列基本操作；同队列顺序执行、异队列并行执行；取消与失败处理；认证保护与 UI 触发 |

## 4. 包格式 & 构建管线

### 4.1 .aosproj → .deb → .apkg

```
.aosproj (XML 项目文件)
    │
    ▼
apkg build --suite resolute --arch amd64
    │  DebBuilder: 复制文件 → staging dir (obj/<suite>_<arch>/)
    │  → DEBIAN/control → Dependency 合并 → SystemdUnit 生成脚本 → dpkg-deb --build
    ▼
bin/pkgname_1.0.0_resolute_amd64.deb
    │
    ▼
apkg publish
    │  扫描 bin/*.deb → 生成 manifest.xml v2 → 打包为 tar.gz (.apkg)
    ▼
bin/pkgname.1.0.0.apkg
```

### 4.2 .aosproj 项目格式 & manifest.xml

完整的 `.aosproj` 语法、ItemGroup 条目类型、`manifest.xml` v2 格式及 CLI 工作流见 **[aosproj.md](aosproj.md)**。

核心要点：
- **构建矩阵**：`TargetSuites × TargetArchitectures` 笛卡尔积，每个组合产出一个 `.deb`
- **ItemGroup 条目**：`IncludeFile`、`IncludeFolder`、`IncludeScript`（自动 0755）、`ConfFile`（dpkg conffile 保护）、`SystemdUnit`（自动生成 postinst/prerm/postrm）、`Dependency`（合并为 Depends）
- **Condition 语法**：MSBuild 风格 — `'$(Suite)' == 'resolute'`，可用 `$(Distro)`、`$(Suite)`、`$(Arch)`
- **manifest.xml v2**：`apkg publish` 自动生成，声明 `Name/Version/Entries`，服务器按 `Distro+Suite+Architecture+Component` 四元组路由到目标仓库

构建中间使用 `dpkg-deb --build --root-owner-group`，在 obj/ 目录下完成。全程不出现 DEBIAN/control 手工操作。

## 5. LocalPackage 覆盖规则

LocalPackage 是用户手动上传的 .deb，代表对上游 Mirror 包的"最终话语权"。

**核心规则**：同一 Repository 内，LocalPackage 覆盖所有同 (Package, Architecture) 的上游 Mirror 包 — 不论版本号。

- RepositorySyncJob 复制 Mirror 数据后：删除所有同 (Package, Architecture) 的 AptPackage → 插入 LocalPackage 数据（IsVirtual=false, RemoteUrl=null）
- 同 (Package, Architecture) 只有一个 active LocalPackage。上传新版本自动软删除旧版本
- 禁用（IsEnabled=false）的 LocalPackage 被跳过，上游版本原样通过
- 范围精确：amd64 的 LocalPackage 不影响 arm64 的上游包

**字段分类**：

| 类别 | 字段 | 说明 |
|------|------|------|
| 身份 | `RepositoryId`, `Package`, `Architecture` | 联合索引，决定覆盖范围 |
| 控制 | `IsEnabled`, `UploadedByUserId` | 启用/禁用门控；上传者所有权 |
| APT 元数据 | `Version`, `Maintainer`, `Description`, `Section`, `Priority`, `Homepage`, `Depends`, `Recommends`, `Suggests`, `Conflicts`, `Breaks`, `Replaces`, `Provides` | 从 .deb control 文件提取，直接拷贝到 AptPackage |
| 文件存储 | `SHA256`, `Size`, `Filename`, `MD5sum`, `SHA1`, `SHA512` | SHA256 由客户端上传时计算；Filename 格式: `pool/{component}/{pkg[0]}/{pkg}/{pkg}_{ver}_{arch}.deb` |

**存储**：`LocalPackages/{repositoryId}/{package}_{version}_{arch}.deb`（独立于 Mirror 的 CAS 存储 `Objects/{sha256[..2]}/{sha256}.deb`）

**权限**：`CanPublishLocalPackage` — 持有者可管理自己的 LocalPackage，管理员可管理所有。

## 6. APT 兼容性 & 服务器路由

### 6.1 路径结构

Apkg 服务器完全伪装成标准 Debian/Ubuntu APT 服务器：

- `/artifacts/{distro}/dists/{suite}/InRelease` — 内嵌签名的发布文件（现代 APT 首选）
- `/artifacts/{distro}/dists/{suite}/Release.gpg` — 分离式签名（兼容旧客户端）
- `/artifacts/{distro}/dists/{suite}/Release` — 发行版总账本（列出所有组件/架构的 Packages 文件路径、大小、SHA256）
- `/artifacts/{distro}/dists/{suite}/{component}/binary-{arch}/Packages.gz` — 包索引
- `/artifacts/{distro}/dists/{suite}/{component}/binary-{arch}/Release` — 空文件

### 6.2 验证链

```
GPG 公钥 (本地 /etc/apt/trusted.gpg.d/) → 验证 InRelease (服务端)
InRelease (包含哈希) → 验证 Packages.xz
Packages.xz (包含哈希) → 验证 chromium.deb
```

### 6.3 c-n-f 与辅助元数据

同步时必须一并抓取 `dists/{suite}/{component}/cnf/Commands-{arch}.xz`（命令未找到提示）、AppStream `dep11` 数据和 `i18n` 翻译数据。路由引擎须支持基于 suite 和 component 的回退匹配。

### 6.4 下载与 ETag

下载路径使用 Virtual Path：`/download/{repository_id}/packages/{package_id}/{version}/package.deb`。本地包直接返回，Mirror 包懒加载带缓存从上游下载。ETag 直接使用 SHA256（已在数据库中），无需按位异或计算。

## 7. 签名 & 证书管理

每个 AptRepository 有独立 GPG 签名密钥。**RepositorySignJob 是唯一的生产 InRelease 的代码路径。**

信任模型：每个 Apkg 服务器用自己的密钥签名。用户必须信任 Apkg 服务器的密钥而非上游密钥 — 因为 Override 规则改变了包内容，上游签名必然失效。

### 穿透签名模式

Repository 可配置为穿透上游签名（Passthrough Signing）：

- 本地包不可上传（无签名密钥可重新签名）
- Override 规则不可用
- Repository 为纯镜像 — 适合作 CDN 边缘节点
- 优势：GPG 私钥仅存在于主服务器，节点被黑后黑客拿不到私钥，无法伪造签名

### 证书容器

生产环境私钥可托管到外部服务（HashiCorp Vault、Azure Key Vault）— Apkg 通过网络调用签名 API，全程不接触私钥。

### 多服务器部署

主服务器执行 Override 计算 + 签名。节点服务器使用穿透签名 Mirror 主服务器。全球多节点，用户自动选择最近节点。一台节点挂掉可切换另一台，签名一致无需重新信任。

## 8. Distro / Suite / Component / Arch

| 轴 | 含义 | 示例值 | URL 路径 |
|----|------|--------|----------|
| **Distro** | 操作系统家族 | `ubuntu`, `debian`, `anduinos` | `/artifacts/{distro}/` |
| **Suite** | 发行代号 + 变体 | `jammy`, `jammy-updates`, `noble` | `/artifacts/{distro}/dists/{suite}/` |
| **Component** | 许可证/支持类别 | `main`, `restricted`, `universe`, `multiverse`, `community` | `.../{suite}/{component}/binary-{arch}/` |
| **Arch** | CPU 架构 | `amd64`, `arm64`, `i386`, `all` | `.../binary-{arch}/Packages.gz` |

- **Distro 不是装饰性的** — 决定包落入哪个 URL 命名空间。服务器用它做路由匹配和上传匹配。
- **Suite** 是 APT 原生概念，每个 Suite 有 `-updates`、`-security`、`-backports` 子变体。
- **Architecture** — `all` 表示架构无关（脚本、数据、文档）。
- 一个 Ubuntu 发行版：4 个 Components × 3 个 Architectures × 4 个子 Suites = **48 个仓库端点**。这就是为什么 APT 镜像/同步基础设施不简单。

## 9. Override 系统（规划中）

Override 中间件管线是 Apkg 区别于简单镜像的战略核心：

```
上游数据 → [Override Rules] → Repository 输出
               │
               ├── DropPackage("chromium-browser")      → 剔除 Snap 壳
               ├── PackageVersionOverride("pkg", "2.0")  → 锁定自定义版本
               └── DependencyOverride("pkg", "Depends: ...") → 重写依赖
```

### DropPackage 与依赖处理

DropPackage 最难实现 — Drop 掉的包可能有其他包依赖它。三种策略：

- **Cascade Drop**：连带删除所有依赖包（极度危险 — 删除 libssl 可清空 90% 仓库）
- **Override Dependents**：去除依赖声明强行安装（可能导致运行时错误）
- **As Is**：保留依赖包但它们无法安装（保守但不可用）

**虚拟依赖 (MockPackage)**：为被 Drop 的包生成一个空壳包（Providing 声明覆盖原包、版本号高于上游），依赖方可正常安装但实际上什么都没装。

### Impact Analysis

应用 Override 规则前，服务器必须计算影响范围："此操作将导致 14,500 个包被移除"。管理员看到这个数字不敢点确认。

### Emergency Rollback

保留最近 2-3 个 IndexKey 数据不被 GC。若 Override 规则引发事故，直接将 Primary 指针切回上一个已知正常的 IndexKey，瞬间恢复服务。

## 10. Community 组件

除 Ubuntu 标准四组件（main, restricted, universe, multiverse）外，Apkg 增加第五个：**community** — 用户贡献包。

- 用户可上传到 community（需审核）
- 风险：恶意包可用同名 + 更高版本号劫持 `apt install`
- 防御：APT pin priority 使 main > community：

```
Package: *
Pin: release l=Official
Pin-Priority: 900

Package: *
Pin: release l=Community
Pin-Priority: 100
```

`anduinos-base` 包应默认释放此配置，用户天生安全。

## 11. 上传 & 验证

上传时服务器必须执行的关键检查：

- **虚假包名检测**：解压 .deb 读取内部 DEBIAN/control，与 manifest.xml 声明的包名比对 — 不一致直接拒绝。永远不信任用户提交的元数据，只信任二进制内部的元数据。
- **AptRepository 匹配**：包的 Distro/Suite/Component/Arch 必须匹配目标 Repository 配置。可使用 `--force` 跳过（仅匹配的部分生效）。
- **同名包权限**：上传与已有包同名的包时，必须是同一维护者才能上传新版本。
- **审核流程**：首次上传新包需管理员手动审核；后续版本更新可选择自动审核。

## 12. 后台任务补充

除了四大 Pipeline 任务（§3），还有以下后台任务：

- **版本 GC**：包持续上传新版本，旧版本标记过期。管理员设置保留天数（如 30 天），后台每天清理过期包文件和记录。
- **Orphan 清理**：上游删除了老版本包（如旧内核 `linux-image-5.15.0-60-generic`）而我们上次编译时还在 — Mirror Job 检测到上游已删除后标记为 Orphan。前端仍可搜索但不可下载。14 天后清理。
- **IndexKey GC**：每 3 小时清理所有未被 Primary/Secondary 指向的 IndexKey 数据。

## 13. 网页前端

简洁搜索型主页。用户搜索跨所有 Repository 的包。点击包可见所有版本、支持平台（distro/suite/component/arch）、依赖关系、维护者、下载统计。

"添加这台服务器"：用户选择 distro → suites → components → arch，前端生成一键 bash 脚本（自动检测 OS 版本 → 配置 sources.list.d → 导入 GPG 公钥 → apt update）。

管理员后台：管理 Repository、Override 规则、审核上传包、查看后台任务运行日志。

## 14. 数据库设计要点

核心实体：AptMirror、AptRepository、AptBucket、AptPackage、LocalPackage。详细 schema 以 EF Core 迁移代码为准。

关键设计注意事项：
- **PackageBlobs**（文件去重）：同文件在 jammy 和 jammy-updates 中完全一样 — 使用 SHA256 CAS 存储去重
- **版本排序**：APT 版本比较（Epoch）很复杂，需支持 deb-version 排序或拆分 Epoch/UpstreamVersion/DebianRevision 辅助列
- **联合索引**：BuiltPackages 高频查询，需要 `(IndexKey, Distro, Suite, Component, Architecture, PackageName)` 联合索引
- **指针持久化**：Primary/Secondary 指针直接存在 AptMirror/AptRepository 实体中，服务器重启后状态不丢失

## 15. 架构决策：为什么

### 15.1 为什么 Apkg 不做仓库安装

`apkg install --file pkg.apkg` 使用 `dpkg -i` 从本地 `.apkg` 文件安装包（自动检测当前系统的 Distro/Suite/Architecture 选择匹配的 .deb）。但它不做仓库级别的安装 — 不从 APT 仓库解析依赖并自动下载。那是 `apt install` 的职责。Apkg 专注于构建 + 仓库服务，不替代 APT 客户端功能。

### 15.2 为什么支持多 Suite

.aosproj 的 `TargetSuites` 允许一个项目为多个 Ubuntu/Debian 发行版编译。每个 Suite 可能有不同的依赖版本（如 jammy 用 libc6 >= 2.35，noble 用 libc6 >= 2.39）。Condition 语法 `'$(Suite)' == 'jammy'` 让开发者在一个项目文件中表达跨 Suite 差异，而非维护多个分支。

### 15.3 为什么有 ApkgPackage 表

ApkgPackage 是 Bucket 内的包视图 — 解包后的结构化字段（Package、Version、Architecture、Component、SHA256、IsVirtual 等）。不直接解析 Packages.gz 文本而用结构化表的原因是：LocalPackage 覆盖需要按 (Package, Architecture) 精确匹配并替换行；IsVirtual 懒加载需要按 SHA256 追踪 CAS 状态；GarbageCollection 需要引用计数。用文本行做这些操作既不安全也不高效。

### 15.4 为什么用双 Bucket 而非原地更新

若直接原地修改 Primary Bucket 内容，APT 客户端在 `apt update` 期间可能读到半写入的 Packages.gz 或未签名的 InRelease。双 Bucket（Primary + Secondary）+ 原子 Swap 保证了：用户永远看到一致的已签名快照；任何步骤崩溃都可以重试而不损坏当前服务状态。

## 16. 命名约定：Apkg* vs Apt*

| 前缀 | 含义 | 示例 |
|------|------|------|
| **Apt*** | 低层 APT 协议概念 — 镜像、仓库、Bucket、包、证书。这些类直接映射到 APT 仓库结构 | `AptMirror`, `AptRepository`, `AptBucket`, `AptPackage`, `AptCertificate` |
| **Apkg*** | 平台级抽象 — 用户可见的概念、上传、SDK、DbContext。这些是 Apkg 在 APT 之上构建的附加值 | `ApkgDbContext`, `ApkgUpload`, `ApkgUploadService` |

规则：如果你在处理 APT 仓库的内部结构（Packages.gz、InRelease、apt-get update），用 `Apt*`。如果你在处理 Apkg 平台功能（用户上传、SDK 工具链、数据库），用 `Apkg*`。
