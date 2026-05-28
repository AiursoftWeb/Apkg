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
                      metadata;                 metadata;
                      see AptBucket below)      see AptBucket below)
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
  SecondaryBucketId → MirrorSyncJob 运行期间：正在拉取的新快照（保护区）。
                      升级完成后变为旧 Primary — 保护仍在流式读取旧数据
                      的 RepositorySyncJob 不被 GC 截断。

AptRepository
  PrimaryBucketId  → 当前对外服务的已签名快照（APT 客户端只看这个）
  SecondaryBucketId → 已构建但未签名的新快照（等待 SignJob）

AptBucket
  ReleaseContent   → 未签名的 Release 文件正文。格式为标准 APT Release 文件：
                    头部字段 (Origin/Label/Suite/Codename/Date/Architectures/
                    Components) + SHA256 校验和列表（每个 component/binary-arch
                    的 Packages 和 Packages.gz 的 SHA256/大小/路径）。
  InReleaseContent → GPG Clearsigned 的 InRelease（null = 尚未签名）
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
| SHA256 计算一次后不再重复 | 上传时由服务端从 .deb 文件计算并存入 LocalPackage.SHA256；镜像拉取时直接复制上游声明值。计算后即视为权威值 |
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
- **Condition 语法**：MSBuild 风格 — `'$(Suite)' == 'resolute'`，可用 `$(Distro)`、`$(Suite)`、`$(Arch)`（别名 `$(Architecture)`）、`$(Component)`、`$(UpstreamDistro)`、`$(UpstreamSuite)`、`$(UpstreamArch)`（别名 `$(UpstreamArchitecture)`）
- **版本模板变量**：`$(UpstreamVersion)` 可在 `PackageVersion` 中使用，构建时自动替换为上游包的实际版本号
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
| 文件存储 | `SHA256`, `Size`, `Filename`, `MD5sum`, `SHA1`, `SHA512` | SHA256 由服务端从 .deb 文件计算；Filename 格式: `pool/{component}/{pkg[0]}/{pkg}/{pkg}_{ver}_{arch}.deb` |

**存储**：`LocalPackages/{repositoryId}/{package}_{version}_{arch}.deb`（独立于 Mirror 的 CAS 存储 `Objects/{sha256[..2]}/{sha256}.deb`）

**权限**：`CanUploadToRestrictedRepositories` — 持有者可向受限仓库上传包，无此权限只能上传到开放仓库。

## 6. APT 兼容性 & 服务器路由

### 6.1 路径结构

Apkg 服务器完全伪装成标准 Debian/Ubuntu APT 服务器：

- `/artifacts/{distro}/dists/{suite}/InRelease` — 内嵌签名的发布文件（现代 APT 首选）
- `/artifacts/{distro}/dists/{suite}/Release` — 未签名的发布文件
- `/artifacts/{distro}/dists/{suite}/{component}/binary-{arch}/Packages.gz` — 包索引

除上述基于 Distro 的路径外，还支持按仓库名 (`/artifacts/repo/{repoName}/dists/{suite}/...`) 和直接套件名 (`/artifacts/dists/{suite}/...`) 路由。

Pool 路径 (`/artifacts/{distro}/pool/{**path}`) 按需从内容寻址存储 (CAS) 提供 `.deb` 二进制文件。

证书公钥在 `/artifacts/certs/{name}` 提供，类型为 `application/pgp-keys`。

### 6.2 验证链

```
GPG 公钥 (本地 /etc/apt/trusted.gpg.d/) → 验证 InRelease (服务端)
InRelease (包含哈希) → 验证 Packages.xz
Packages.xz (包含哈希) → 验证 chromium.deb
```

### 6.3 c-n-f 与辅助元数据（规划中）

命令未找到提示 (`cnf/Commands-{arch}.xz`)、AppStream (`dep11`) 和翻译 (`i18n`) 数据的同步和路由暂未实现，属于未来规划。

### 6.4 下载

`.deb` 文件下载通过 `artifacts/{distro}/pool/{**path}` 路径提供（AptMirrorController.GetPool）。若请求的包为虚包（`IsVirtual=true`），则在本次请求中从上游 RemoteUrl 下载并写入 CAS 存储（`Objects/{sha256[..2]}/{sha256}.deb`），后续请求直接走快速路径返回。通用文件下载由 `FilesController`（`download/{**folderNames}`）独立处理，附带基于 `LastWriteTime ^ Length` 的 ETag 用于 HTTP 304。

## 7. 签名 & 证书管理

每个 AptRepository 有独立 GPG 签名密钥。**RepositorySignJob 是唯一的生产 InRelease 的代码路径。**

信任模型：每个 Apkg 服务器用自己的密钥签名。用户必须信任 Apkg 服务器的密钥而非上游密钥 — 因为 Override 规则改变了包内容，上游签名必然失效。

### 穿透签名模式

Repository 可配置为穿透签名（Passthrough Signing，`EnableGpgSign = false`）：

- GPG 签名被跳过 — Bucket 在 RepositorySignJob 中原样提升为 Primary，不签名
- 适用于纯镜像节点（无签名私钥）
- 节点被黑后黑客拿不到私钥，无法伪造签名
- **代价**：`InReleaseContent` 为空，APT 客户端默认拒绝未签名仓库。管理员必须在所有客户端 `sources.list` 中加 `[trusted=yes]`，否则 `apt update` 失败

### 证书容器（规划中）

生产环境私钥可托管到外部服务（HashiCorp Vault、Azure Key Vault）— Apkg 通过网络调用签名 API，全程不接触私钥。当前未实现，签名使用本地 GPG 密钥。

### 多服务器部署（规划中）

主服务器执行 Override 计算 + 签名。节点服务器使用穿透签名 Mirror 主服务器。全球多节点，用户自动选择最近节点。当前未实现多节点协调。

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

保留最近 2-3 个 Bucket 数据不被 GC。若 Override 规则引发事故，直接将 Primary 指针切回上一个已知正常的 Bucket，瞬间恢复服务。

## 10. Community 组件（规划中）

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

- **SHA256 冲突检测**：相同 SHA256 的 .deb 已存在则拒绝。
- ** (Package, Version, Arch, Component) 槽位冲突**：已有同槽位包则拒绝，除非使用覆盖模式。
- **DEBIAN/control 解析**：从已上传 .deb 内部提取包名、版本、架构、维护者、依赖项等 APT 元数据字段，存入数据库。
- **AptRepository 匹配**：包的 Distro/Suite/Component/Arch 必须匹配目标 Repository 配置。可使用 `--skip-duplicate` 在上传已存在包时跳过而非报错。

**ApkgUpload 记录管理**：每次 `apkg push` 在服务端对应一条 `ApkgUpload` 记录，其生命周期遵循以下规则：

- **前置校验**：记录在确认 archive 内所有 `Entry` 引用的 `.deb` 文件都存在之后才创建。manifest 解析失败或文件缺失时直接返回错误，不产生数据库记录。
- **IsPublished 语义**：只有至少有一个 `.deb` 被实际上传且无未解决的错误时才标记为 `Published`。全部被跳过（`--skip-duplicate`）、全部冲突（无 flag 的 409）、或部分成功但存在冲突错误（无 flag）时记录不发布。
- **零上传清理**：如果最终没有任何包被上传（全部跳过或全部冲突），已创建的 `ApkgUpload` 记录会被立即删除。不会残留"处理过但什么都没做"的垃圾记录。
- **部分成功**：部分 repo 上传成功但部分 409 冲突（不带 `--skip-duplicate`）时，记录保留（`IsPublished=false`）并返回 409，方便排查哪些 repo 成功、哪些冲突。
- **超时兜底**：极端情况下（如进程在循环中途崩溃），`ApkgTempCleanupJob` 会清理超过 30 分钟仍未发布的记录（见 §12）。

| 场景 | ApkgUpload 结果 |
|------|----------------|
| 全新包，全部 suite 成功 | 1 条 Published 记录，返回 200 |
| 全部跳过 + `--skip-duplicate` | 0 条记录，返回 200 + warnings |
| 全部冲突，不带 flag | 0 条记录，返回 409 |
| 部分成功 + `--skip-duplicate` | 1 条 Published（仅含实际上传的包），返回 200 |
| 部分成功，不带 flag | 1 条记录（IsPublished=false），返回 409 |

## 12. 后台任务补充

除了四大 Pipeline 任务（§3），还有以下后台任务：

| 任务 | 职责 | 触发 |
|------|------|------|
| `ApkgTempCleanupJob` | 清理超过 30 分钟仍未发布的 ApkgUpload 草稿及其临时文件 | 每 10 分钟（startDelay 7 min） |
| `OrphanAvatarCleanupJob` | 清理数据库中无引用的用户头像文件 | 每 6 小时（startDelay 5 min） |
| `RepositoryDependencyCheckJob` | 按需检查仓库 Bucket 中所有包的依赖完整性（326 行逻辑） | 手动触发（通过 `/Jobs` 页面） |

## 13. 网页前端

简洁搜索型主页。用户搜索跨所有 Repository 的包。点击包可见所有版本、支持平台（distro/suite/component/arch）、依赖关系、维护者、下载统计。

"添加这台服务器"：用户选择 distro → suites → components → arch，前端生成一键 bash 脚本（自动检测 OS 版本 → 配置 sources.list.d → 导入 GPG 公钥 → apt update）。

管理员后台：管理 Repository、Override 规则、审核上传包、查看后台任务运行日志。

## 14. 数据库设计要点

核心实体：AptMirror、AptRepository、AptBucket、AptPackage、LocalPackage。详细 schema 以 EF Core 迁移代码为准。

关键设计注意事项：
- **SHA256 CAS 去重**：相同 .deb 文件在多个 Suite/Component 中只存一份，路径为 `Objects/{sha256[..2]}/{sha256}.deb`。GarbageCollectionJob 通过引用计数决定是否删除文件
- **LocalPackage 文件独立存储**：用户上传的 .deb 存储在 `LocalPackages/{repositoryId}/{package}_{version}_{arch}.deb`，与 Mirror 的 CAS 存储分离
- **指针持久化**：Primary/Secondary 指针直接存在 AptMirror/AptRepository 实体中，服务器重启后状态不丢失

## 15. 架构决策：为什么

### 15.1 为什么 Apkg 不做仓库安装

`apkg install pkg.apkg` 使用 `dpkg -i` 从本地 `.apkg` 文件安装包（自动检测当前系统的 Distro/Suite/Architecture 选择匹配的 .deb）。但它不做仓库级别的安装 — 不从 APT 仓库解析依赖并自动下载。那是 `apt install` 的职责。Apkg 专注于构建 + 仓库服务，不替代 APT 客户端功能。

### 15.2 为什么支持多 Suite

.aosproj 的 `TargetSuites` 允许一个项目为多个 Ubuntu/Debian 发行版编译。每个 Suite 可能有不同的依赖版本（如 jammy 用 libc6 >= 2.35，noble 用 libc6 >= 2.39）。Condition 语法 `'$(Suite)' == 'jammy'` 让开发者在一个项目文件中表达跨 Suite 差异，而非维护多个分支。

### 15.3 为什么有 AptPackage 表

AptPackage 是 Bucket 内的包视图 — 解包后的结构化字段（Package、Version、Architecture、Component、SHA256、IsVirtual 等）。不直接解析 Packages.gz 文本而用结构化表的原因是：LocalPackage 覆盖需要按 (Package, Architecture) 精确匹配并替换行；IsVirtual 懒加载需要按 SHA256 追踪 CAS 状态；GarbageCollection 需要引用计数。用文本行做这些操作既不安全也不高效。

### 15.4 为什么用双 Bucket 而非原地更新

若直接原地修改 Primary Bucket 内容，APT 客户端在 `apt update` 期间可能读到半写入的 Packages.gz 或未签名的 InRelease。双 Bucket（Primary + Secondary）+ 原子 Swap 保证了：用户永远看到一致的已签名快照；任何步骤崩溃都可以重试而不损坏当前服务状态。

## 16. 命名约定：Apkg* vs Apt*

| 前缀 | 含义 | 示例 |
|------|------|------|
| **Apt*** | 低层 APT 协议概念 — 镜像、仓库、Bucket、包、证书。这些类直接映射到 APT 仓库结构 | `AptMirror`, `AptRepository`, `AptBucket`, `AptPackage`, `AptCertificate` |
| **Apkg*** | 平台级抽象 — 用户可见的概念、上传、SDK、DbContext。这些是 Apkg 在 APT 之上构建的附加值 | `ApkgDbContext`, `ApkgUpload`, `ApkgPackageManifest` |

规则：如果你在处理 APT 仓库的内部结构（Packages.gz、InRelease、apt-get update），用 `Apt*`。如果你在处理 Apkg 平台功能（用户上传、SDK 工具链、数据库），用 `Apkg*`。
