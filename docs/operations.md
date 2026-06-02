# Apkg 运维指南

## 快速开发启动

```bash
# 安装并注册 systemd 服务到指定端口
./install.sh 5000
```

克隆仓库 → 安装 .NET + Node 依赖 → 发布到 `/opt/apps/apkg` → 注册 systemd 服务。

## 默认管理员凭据

首次运行（空数据库）时，`Program.SeedAsync()` (ProgramExtends.cs:57) 自动创建：

- **用户名**: `admin`
- **密码**: `Admin@123456!`
- **角色**: Administrators（拥有所有权限）

**生产环境请立即修改。**

## 数据库配置

`appsettings.json` → `ConnectionStrings`:

```json
// SQLite（默认，本地开发）:
"DbType": "Sqlite",
"DefaultConnection": "DataSource=app.db;Cache=Shared"

// MySQL（生产）:
"DbType": "MySql",
"DefaultConnection": "Server=localhost;Database=apkg;Uid=apkg;Pwd=..."
```

使用 `Aiursoft.DbTools.Switchable` — `DbType` 决定启动时加载哪个 Provider。单元测试自动切换到 InMemory。

## 认证方式

```json
"AppSettings": {
  "AuthProvider": "Local",  // 或 "OIDC"
  "OIDC": { ... },
  "Local": {
    "AllowRegister": true,
    "AllowWeakPassword": true
  }
}
```

Local 和 OIDC 互斥。切换认证方式需清理现有用户会话。

## 存储路径

```json
"Storage": {
  "Path": "/tmp/data"  // 生产环境改为持久化路径
}
```

包含: LocalPackages、CAS objects（`Objects/{sha256[..2]}/{sha256}.deb`）、用户头像、GPG 密钥环。

## Docker

```bash
docker build -t apkg .
docker run -d -p 5000:5000 -v /srv/apkg:/data apkg
```

关键 Dockerfile 细节：
- **基础镜像**: `dotnetonlyruntime`（仅 ASP.NET 运行时）
- **运行时依赖**: `gnupg ubuntu-keyring`（GPG 签名 + 上游密钥验证）
- **Volume**: `/data` — 持久化数据库、包文件、GPG 密钥
- **配置 Symlink**: 首次运行时 `appsettings.json` 从 `/app` 复制到 `/data` 并建立软链接，容器重建后配置不丢失
- **HEALTHCHECK**: `wget --spider http://localhost:5000/health`，间隔 10s，3 次重试，180s 启动宽限期
- **端口**: 5000

## Lint

```bash
./lint.sh
```

运行 JetBrains ReSharper Global Tools (`jb inspectcode`)。需先安装 `dotnet tool install JetBrains.ReSharper.GlobalTools`。过滤已知误报：InconsistentNaming、AssignNullToNotNullAttribute、UnusedAutoPropertyAccessor、DuplicateResource、NotOverriddenInSpecificCulture。任何 `WARNING` 或 `ERROR` 级别的问题都会导致构建失败。

## 仓库配置与构建矩阵对齐

### 核心概念

每个 `AptRepository` 由三个字段唯一确定其路由：

| 字段 | 含义 | 示例 |
|------|------|------|
| `Distro` | 操作系统家族标识 | `anduinos`, `ubuntu`, `debian` |
| `Suite` | 发行代号/变体 | `noble-addon`, `questing-addon`, `resolute` |
| `Architecture` | CPU 架构 | `amd64`, `arm64`, `all` |

此外每个仓库有一个 `Components` 列表（如 `main restricted universe`），决定该仓库**包含哪些组件**。注意：**`Component` 不参与路由匹配**——路由只看 `(Distro, Suite, Architecture)`。

### 为什么对齐很重要

当包发布者用 `apkg push` 上传一个 `.apkg` 时，服务端对每个 Entry 执行路由：

```
Entry: (Distro, Suite, Architecture) → 查找 AptRepository → 找到 → 存 deb
                                                         → 找不到 → 跳过，丢弃
```

**被跳过的 deb 不会出现在任何 APT 仓库中，且 `apkg push` 返回 200 不报错。** 详见 [aosproj.md § 静默跳过陷阱](aosproj.md)。

### 管理员操作清单

**1. 发布前对齐**

在包发布者开始推送之前，确保服务器上的 `AptRepository` 覆盖了他们声明的所有 `(Distro, Suite, Architecture)` 组合。以 AnduinOS 为例，标准矩阵为：

| Distro | Suite | Architectures |
|--------|-------|--------------|
| anduinos | noble-addon | amd64, arm64 |
| anduinos | questing-addon | amd64, arm64 |
| anduinos | resolute-addon | amd64, arm64 |

每行对应一个 `AptRepository`（共 6 个），均配置 `Components = main`。

**2. 发布后验证**

在目标机器上执行以下命令确认包已正确发布到所有 suite：

```bash
# 检查 noble-addon
apt-cache show my-package | grep -E "^Suite:|^Version:|^Architecture:"

# 检查 questing-addon
apt-cache show my-package -o Dir::Etc::SourceList=/dev/null \
  -o APT::Default-Release=questing-addon 2>/dev/null
```

**3. 监控服务端日志**

日志中出现以下 Warning 意味着有 deb 被丢弃，通常说明你需要新建仓库：

```
No repository found for (Distro=anduinos, Suite=questing-addon, Arch=amd64)
```

**4. 公开你的仓库矩阵**

在 Web UI 或团队文档中明确列出所有可用的 `(Distro, Suite, Architecture)` 组合。包发布者编写 `.aosproj` 时应只声明此表中存在的组合。

**5. 新建 Suite/Arch 时通知发布者**

如果你新增了一个 Suite（如 `solstice-addon`），需要通知所有包发布者：
- 如果他们希望包出现在新 suite 中，需要在 `.aosproj` 的 `TargetSuites` 中加入新 suite
- 已推送的包需要**重新 `apkg push`** 一次——历史 push 中因不匹配被丢弃的 deb 不会自动恢复

### 常见误区

| 误区 | 事实 |
|------|------|
| "Component 对不上也会导致路由失败" | `Component` **不参与路由**。路由只看 `(Distro, Suite, Architecture)` |
| "我可以事后补建仓库，deb 会自动出现" | 不会。历史 push 中无匹配仓库的 deb 已被丢弃，需重新 push |
| "`apkg push` 返回 200 就说明所有 deb 都入库了" | 不是。服务端静默跳过无匹配仓库的 Entry，客户端不报错 |
| "Distro 代表 CPU 架构" | `Distro` 是 OS 家族标识（如 `anduinos`），与 CPU 架构无关 |

## 生产环境证书

每个 AptRepository 有独立 GPG 签名密钥。生产环境私钥可托管到外部服务（HashiCorp Vault、Azure Key Vault）— Apkg 通过网络调用签名 API，全程不接触私钥。

种子数据（`Program.SeedMirrorsAsync()`, ProgramExtends.cs:122）自动生成默认 GPG 证书（名称: "anduinos"，邮箱: support@aiursoft.com）。生产环境应替换为真实证书。
