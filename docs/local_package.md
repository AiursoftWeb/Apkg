# LocalPackage 设计文档

## 概述

LocalPackage 是一个独立实体，代表用户手动上传到某个 Repository 的本地 .deb 包。不依赖 Override 系统或 Apkg 工具链即可独立工作。

核心定位：LocalPackage 是用户对上游镜像的"最终话语权"。凡是在 LocalPackage 里存在的包名，在最终的 Repository 产出中，一定会盖过来自 Mirror 的同名包。

---

## 实体设计

```
LocalPackage
  Id                  主键
  RepositoryId        归属的 AptRepository（必须）
  UploadedByUserId    上传者 User.Id（必须，用于审计）
  UploadedAt          上传时间
  IsEnabled           是否启用（默认 true，管理员可禁用）
  IsDeleted           软删除标记

  Package             包名（e.g. "vim"）
  Architecture        架构（e.g. "amd64", "arm64", "all"）
  Version             版本号（e.g. "2:9.1.0777-1"）
  Component           组件（e.g. "main", "universe"）

  Maintainer, Description, Section, Priority
  Depends, Recommends, Suggests, Conflicts, Breaks, Replaces, Provides
  InstalledSize, Homepage, Source, MultiArch

  Filename            相对于 LocalPackages 根目录的路径
  Size                文件字节数（字符串，与 APT 规范一致）
  SHA256, SHA1, MD5sum
```

---

## 存储位置

.deb 文件存储在独立目录，与 Mirror 的 Objects/ 对象存储不共享：

```
{WorkspaceFolder}/
  Objects/              Mirror 懒同步的 .deb（CAS 按 SHA256 命名）
  LocalPackages/        本地上传的 .deb（此功能新增）
    {repositoryId}/
      {package}_{version}_{architecture}.deb
```

---

## 覆盖规则

### 同名覆盖原则

在同一个 Repository 内，只要包名（Package）和架构（Architecture）相同，LocalPackage 就覆盖 Mirror 中的同名包，不管版本号。

例如：
- Mirror 有 vim 2:9.0.0-1 amd64
- LocalPackage 上传了 vim 2:9.1.0-1 amd64
- 最终产出：只有 vim 2:9.1.0-1 amd64

### 单 active 版本原则

同一个 Repository 下，同名同架构只允许一个 active 版本。上传新版本时，旧版本自动软删除。包名不同则互不影响（python2 和 python3 是独立的）。

---

## 上传流程

```
用户上传 .deb（Web UI 或 API Key）
  → 服务器解析 .deb control 文件，提取元数据
  → 检查同 Repository 下是否有相同 (Package, Architecture) 的 active LocalPackage
      存在 → 旧记录软删除（IsDeleted = true），删除旧 .deb 文件
  → 保存 .deb 到 LocalPackages/{repositoryId}/
  → 插入新 LocalPackage 记录（IsEnabled = true, IsDeleted = false）
  → 等待下一轮 RepositorySyncJob（最多约 25 分钟后可下载）
```

---

## RepositorySyncJob 合并逻辑

Mirror 数据复制完成后，在生成 Release 之前插入以下步骤：

```
[新增步骤]
  3. 加载该 Repository 下所有 IsEnabled = true, IsDeleted = false 的 LocalPackage
  4. 对每个 LocalPackage，从新 Bucket 中删除所有满足
       Package == localPkg.Package && Architecture == localPkg.Architecture
     的 AptPackage
  5. 将所有 LocalPackage 以 AptPackage 形式插入新 Bucket
       IsVirtual = false，RemoteUrl = null
```

LocalPackage 插入 Bucket 时的 Filename 格式遵循标准 APT pool 路径：
pool/{component}/{package[0]}/{package}/{package}_{version}_{arch}.deb

---

## 权限控制

新增权限：CanPublishLocalPackage

- 持有此权限：可上传、删除、禁用/启用自己的 LocalPackage
- 管理员：可管理所有用户的 LocalPackage
- 普通用户：只读

API Key 发布时还需持有 PublishLocalPackage scope（见 api_key.md）。

---

## 用户视角：我的包列表

用户可查看自己上传的所有 LocalPackage（跨 Repository）：

| 字段 | 显示内容 |
|---|---|
| 包名 | Package + Version + Architecture |
| 归属仓库 | Repository.Name |
| 组件 | Component |
| 上传时间 | UploadedAt |
| 状态 | Enabled / Deleted |

---

## GC 与清理

软删除后：
1. 不再被 RepositorySyncJob 合并入新 Bucket
2. 旧 Bucket 中已产出的包不受影响（bucket 快照保护）
3. 磁盘 .deb 文件由 GarbageCollectionJob 延迟清理

---

## 和后续功能的边界

| 功能 | 关系 |
|---|---|
| Override 系统 | 在 RepositorySyncJob 中排在 LocalPackage 之前（Mirror → Override → LocalPackage） |
| Apkg 工具链 | apkg 最终生成 LocalPackage，但 LocalPackage 不依赖 Apkg 存在 |
| 全局包 | 当前绑定到具体 Repository；跨仓库共享在 Apkg 阶段实现 |
