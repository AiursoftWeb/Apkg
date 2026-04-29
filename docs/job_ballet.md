# 任务芭蕾：四个后台任务的协同机制

## 概述

Apkg 的核心由四个后台任务组成，它们像芭蕾舞演员一样分工协作、互相配合。无论这四个任务以何种顺序运行、并发执行、中途崩溃或延迟，APT 客户端**永远不会收到未签名或损坏的数据包元数据**。

---

## 四位演员

| 任务 | 职责 | 触发方式 |
|---|---|---|
| `MirrorSyncJob` | 从上游 Ubuntu 拉取包列表，写入 Mirror Primary Bucket | 定时 / 手动 |
| `RepositorySyncJob`（Seed All APT repository as pending job） | 从 Mirror Primary Bucket 复制数据，生成 Release 文件，挂到 Secondary Bucket | 定时 / 手动 |
| `RepositorySignJob`（Sign Pending bucket and swap） | 对 Secondary Bucket 进行 GPG 签名，然后原子升级为 Primary Bucket | 定时 / 手动 |
| `GarbageCollectionJob` | 清理所有既不是 Primary 也不是 Secondary 的 orphan bucket | 定时 / 手动 |

---

## 核心数据结构

```
AptMirror
  PrimaryBucketId  → 当前对外服务的 Mirror 快照
  SecondaryBucketId → 正在拉取中的新快照（构建期间的保护区）

AptRepository
  PrimaryBucketId  → 当前对外服务的已签名快照（APT 客户端只看这个）
  SecondaryBucketId → 已构建但尚未签名的新快照（等待 SignJob）

AptBucket
  ReleaseContent   → 未签名的 Release 文件原文
  InReleaseContent → GPG 签名后的 InRelease 文件（null 表示尚未签名）
  SignedAt         → 签名时间戳
```

---

## 完整工作流程

### Mirror 流程

```
[MirrorSyncJob]
  1. 创建新 AptBucket
  2. 立即（单次 SaveChanges）将 mirror.SecondaryBucket = 新 bucket
     → GC 立即知道这个 bucket 是活跃的，不会删除
  3. 从上游 Ubuntu 拉取所有包（可能耗时数分钟）
  4. 将旧 Primary 保留在 Secondary（防止 RepositorySyncJob 的游标被截断）：
       oldPrimary = mirror.PrimaryBucketId
       mirror.PrimaryBucketId = mirror.SecondaryBucketId  ← 新 bucket 上线
       mirror.SecondaryBucketId = oldPrimary              ← 旧 bucket 继续受保护
  5. 下次 MirrorSyncJob 运行时，新 bucket 覆写 SecondaryBucketId，旧 bucket 变 orphan
```

### Repository 流程

```
[RepositorySyncJob]
  1. 创建新 AptBucket
  2. 立即（单次 SaveChanges）将 repo.SecondaryBucket = 新 bucket
     → GC 立即知道这个 bucket 是活跃的，不会删除
  3. 从 Mirror Primary Bucket 复制所有包（可能耗时数分钟）
  4. 生成 Release 文件，写入 bucket.ReleaseContent
  5. 任务结束（不签名、不切换 Primary）

[RepositorySignJob]
  1. 找所有 SecondaryBucketId != null 的 repo
  2. 检查 bucket.ReleaseContent != null（防止对未完成的 bucket 签名）
  3. 如果有证书：GPG 签名 → 写入 bucket.InReleaseContent + SignedAt
  4. 原子操作：repo.PrimaryBucketId = repo.SecondaryBucketId
               repo.SecondaryBucketId = null
  → 从这一刻起，APT 客户端能看到新的已签名数据

[GarbageCollectionJob]
  1. 收集所有 Mirror/Repo 的 PrimaryBucketId 和 SecondaryBucketId（共4类）
  2. 所有不在这个集合中的 bucket 就是 orphan
  3. 立即删除 orphan bucket 的：DB 包记录、DB bucket 记录、磁盘文件
  4. 清理无引用的 .deb 物理文件（CAS 对象存储）
```

---

## 为什么用户永远不会收到坏数据？

### 1. APT 客户端只看 Primary，永不接触 Secondary

`AptMirrorController` 只从 `repo.PrimaryBucket` 读取 `InReleaseContent` 和 `ReleaseContent`。Secondary Bucket 对外完全不可见，无论它处于什么状态（正在构建、已构建未签名）。

### 2. SignJob 对未完成的 bucket 有守卫

```csharp
if (string.IsNullOrEmpty(bucketEntity.ReleaseContent))
{
    // bucket 还在构建中，跳过，不签名、不升级
    return;
}
```

即使 SignJob 在 SyncJob 还没跑完的时候手动触发，也不会把空的 bucket 升级为 Primary。

### 3. GC 永远不删活跃 bucket

GC 的 active set = Mirror Primary ∪ Mirror Secondary ∪ Repo Primary ∪ Repo Secondary。  
只要一个 bucket 被任何一个 mirror 或 repo 以任何方式引用，就不会被删除。

### 4. SyncJob 用单次 SaveChanges 消除孤儿窗口

```csharp
// 错误的老写法（有窗口）：
db.AptBuckets.Add(bucket);
await db.SaveChangesAsync(); // ← GC 如果在这里运行，bucket 是孤儿！
repo.SecondaryBucketId = bucket.Id;
await db.SaveChangesAsync();

// 正确的新写法（无窗口）：
repo.SecondaryBucket = bucket; // EF 导航属性
await db.SaveChangesAsync();   // EF 自动先 INSERT bucket，再 UPDATE SecondaryBucketId，一个事务
```

### 5. SignJob 清理悬空引用（防御性设计）

如果 Secondary Bucket 被意外删除（理论上不可能，但防御一下）：

```csharp
if (bucketEntity == null)
{
    repo.SecondaryBucketId = null; // 清掉悬空 FK，repo 不会永远卡住
    return;
}
```

---

## 各种极端场景分析

| 场景 | 结果 |
|---|---|
| SyncJob 中途崩溃 | Secondary Bucket 有引用，不被 GC 删；Primary 不变，用户仍用旧版 |
| SyncJob 完成，SignJob 还没跑 | Secondary 有数据但未签名，Primary 不变；用户仍用旧版 |
| SignJob 先于 SyncJob 手动触发 | `ReleaseContent == null`，守卫跳过，不升级 |
| GC 在 SyncJob 创建 bucket 的同时运行 | 单次 SaveChanges 保证 bucket 创建和 SecondaryBucketId 设置是原子的，无窗口 |
| GC 在 SignJob 升级后运行 | 旧 Primary bucket 失去引用，被正常 GC 掉；新 Primary 有引用，安全 |
| SignJob 和 GC 同时运行（Mode A） | GC active set 包含 Secondary，不会删；SignJob 继续正常运行 |
| SignJob 和 GC 同时运行（Mode B） | SignJob 升级后，旧 bucket 才失去引用；GC 此轮已计算好 active set，不会误删新 Primary |
| 用户在 SignJob 运行期间 apt update | APT 读 Primary，SignJob 写的是 Secondary；无冲突，用户读到一致的旧版 |
| MirrorSyncJob 升级期间 RepositorySyncJob 正在流式读取旧 Mirror Primary | MirrorSyncJob 把旧 Primary 保留在 Mirror.Secondary；GC 不删它；RepositorySyncJob 游标不被截断 |

---

## 数据流图

```
上游 Ubuntu
     │
     ▼
[MirrorSyncJob]
     │  创建 bucket，立即挂到 Mirror.Secondary
     │  拉取完成后：Secondary → Primary
     ▼
Mirror.Primary Bucket（包元数据快照）
     │
     ▼
[RepositorySyncJob]
     │  创建 bucket，立即挂到 Repo.Secondary
     │  从 Mirror.Primary 复制数据
     │  生成 Release 文件，写入 ReleaseContent
     ▼
Repo.Secondary Bucket（已构建，未签名）
     │
     ▼
[RepositorySignJob]
     │  验证 ReleaseContent != null
     │  GPG 签名 → InReleaseContent
     │  原子 swap：Secondary → Primary
     ▼
Repo.Primary Bucket（已签名，对外服务）
     │
     ▼
APT 客户端（apt update / apt install）


[GarbageCollectionJob]（随时可运行）
     │  active = {所有 Primary} ∪ {所有 Secondary}
     │  删除不在 active 中的所有旧 bucket
     ▼
磁盘 / DB 释放
```

---

## 设计原则总结

1. **只有 SignJob 能写 Repo.PrimaryBucketId**：这是整个系统安全的根基。
2. **Secondary 是保护区，不是公开区**：任何处于 Secondary 位置的 bucket 对 APT 客户端不可见。
3. **GC 的边界由引用关系决定，不依赖时间戳**：彻底消除了"2小时猶予"这类时间魔法数字。
4. **导航属性单次 SaveChanges**：EF Core 保证 bucket 插入和外键更新在同一事务，消除孤儿窗口。
5. **每个任务独立可重试**：任何任务崩溃后重新运行，都能从正确的状态继续。
6. **Mirror 升级时旧 Primary 留在 Secondary**：防止正在流式读取旧 Mirror 数据的 RepositorySyncJob 的游标被 GC 截断。旧 Primary 在下一轮 MirrorSyncJob 开始时自然变成 orphan，届时 RepositorySyncJob 早已结束。

---

## 哈希不错位的不变量

哈希（SHA256）是 Apkg 包身份验证的核心。以下不变量确保 SHA256 始终跟着正确的包走：

| 不变量 | 实现方式 |
|---|---|
| SHA256 从不在代码中重新计算 | 上传时由客户端计算后存入 `LocalPackage.SHA256`；从上游拉取时直接复制上游声明的 SHA256 |
| 从 LocalPackage 到 AptPackage 的字段原封不动 | `RepositorySyncJob` 直接赋值 `SHA256 = lp.SHA256`，且 `IsVirtual = false`，`RemoteUrl = null` |
| 替换是精确范围的 `(Package, Architecture)` | 本地包替换上游时，`WHERE Package = lp.Package AND Architecture = lp.Architecture`，不影响其他架构 |
| CAS 文件名 = SHA256 | 磁盘上的 `.deb` 以其 SHA256 命名；GC 删文件时对比 DB 中所有引用的哈希，只删"没有任何包行引用"的文件 |
| 禁用的 LocalPackage 完全不可见 | `WHERE IsEnabled = true` 守卫在数据写入路径；上游版本原样保留 |

---

## 测试覆盖体系

下面五个测试类共同锁住上述所有保证，确保任何代码改动破坏这些不变量时测试会失败。

### `AtomicBucketCreationTests` — EF 原子性保证

验证"导航属性单次 SaveChanges"这一核心技术正确性。

| 测试方法 | 验证点 |
|---|---|
| `Mirror_NavigationPropertySave_BucketIdAndForeignKeyAreConsistent` | 单次保存后 `bucket.Id` 与 `SecondaryBucketId` 一致 |
| `Mirror_AfterSingleSave_GcDoesNotDeleteNewBucket` | GC 立即运行也不删刚注册的 bucket |
| `Repo_NavigationPropertySave_BucketIdAndForeignKeyAreConsistent` | 同上，AptRepository 侧 |
| `Repo_AfterSingleSave_GcDoesNotDeleteNewBucket` | 同上，AptRepository 侧 |
| **`Repo_OldTwoSavePattern_GcCanDeleteBucketInWindow`** ⚠️ | **反向测试**：故意用旧的两次 SaveChanges 写法，证明 GC **会** 删掉窗口期的 bucket——这是活的 bug 文档 |

> 反向测试的重要性：如果未来有人把 GC 改得过于保守，这个测试会反过来"通过"（bucket 幸存），从而触发警报。

### `GcSignRaceConditionTests` — GC 与 SignJob 的两种竞态回归

```
Mode A：GC 先跑                Mode B：SignJob 先跑，GC 追上来
  ↓                               ↓
Secondary bucket 被删          SignJob 提升后，GC 把新 Primary 也删了
  ↓                               ↓
SignJob 找不到 bucket            FK 约束爆炸
  ↓
repo 永久不可见（空仓库）
```

| 测试方法 | 覆盖场景 |
|---|---|
| `GC_WhenSecondaryBucketExists_MustNotDeleteIt` | Mode A 核心：Secondary 不被 GC 删 |
| `GC_ThenSignJob_FullModeAScenario_RepoBecomesLive` | Mode A 完整链路：GC → SignJob → repo 上线，InRelease 有 GPG 签名 |
| `GC_AfterSignJobPromotes_DoesNotDeleteNewPrimaryBucket` | Mode B：提升后 GC 不删新 Primary |
| `FullLifecycle_OldBucketDeletedByGcOnlyAfterDemotion` | 完整 5 步生命周期：旧桶**只在** Secondary 被覆盖后才被删 |
| `GC_TrulyOrphanedBucket_IsDeleted` | 防止 GC 过于保守——无引用的桶必须被删 |
| `GC_WhenSecondaryBucketHasNoReleaseContent_MustNotDeleteIt` | 构建中（`ReleaseContent = null`）的桶受 Secondary 保护 |
| `SignJob_WhenSecondaryBucketHasNoReleaseContent_MustSkipIt` | SignJob 对未完成桶有守卫，不提前升级 |
| `SignJob_WhenSecondaryBucketAlreadyDeleted_ClearsDanglingReference` | 防御性：清理悬空 FK，repo 不会永久卡住 |

### `RepositorySignJobTests` — "APT 客户端永远看不到未签名内容"

最关键的两个测试直接打 HTTP 接口：

| 测试方法 | 验证点 |
|---|---|
| `AptEndpoint_WithSecondaryBucketNotYetPromoted_ReturnsNotFound` | **签名前**：`GET /artifacts/.../InRelease` 返回 **404** |
| `AptEndpoint_AfterSignJobPromotes_ServesSignedContent` | **签名后**：接口返回 200，内容包含 `-----BEGIN PGP SIGNED MESSAGE-----` |
| `RepositorySignJob_WithCertificate_SignsThenPromotes` | Primary 切换后 `InReleaseContent` 有效，`SignedAt` 有值 |
| `RepositorySignJob_WithoutCertificate_PromotesBucketWithoutSigning` | 无证书时也能升级，但 `InReleaseContent` 保持 null |
| `RepositorySignJob_WhenNoSecondaryBucket_LeavesPrimaryBucketUnchanged` | 无待处理桶时 Primary 不变 |

### `RepositorySyncLocalPackagesTests` — 哈希不错位的直接验证

| 测试方法 | 验证的哈希/元数据不变量 |
|---|---|
| `SyncJob_EnabledLocalPackage_RemovesUpstreamVersionAndInsertsLocal` | 本地包 SHA256 替换上游，只留一条记录 |
| `SyncJob_LocalPackage_RemovesAllUpstreamVersionsForSamePackageAndArch` | 同名多个上游版本全部被替换，不残留旧哈希 |
| `SyncJob_LocalPackage_ArchScope_OnlyRemovesMatchingArch` | 替换范围精确：amd64 的本地包不影响 arm64 的上游哈希 |
| `SyncJob_LocalPackage_MetadataIsPreservedFaithfully` | SHA256、Filename、IsVirtual、RemoteUrl 原封传递 |
| `SyncJob_DisabledLocalPackage_DoesNotOverrideUpstream` | disabled 时上游哈希原样保留 |
| `SyncJob_NonConflictingMirrorPackages_SurviveAlongsideLocalPackages` | 未冲突的包不受影响 |
| `SyncJob_MultipleDistinctLocalPackages_AllInserted` | 多个不同本地包全部写入 |
| `SyncJob_OnlyEnabledLocalPackage_IsInserted_WhenMixedStates` | 同名包同时有 enabled/disabled 时只取 enabled |
| `SyncJob_StandaloneRepo_LocalPackagesAreIncluded` | 无 mirror 的独立仓库也能正确合并本地包 |

### `BackgroundJobsTests` — 任务队列安全性

| 测试方法 | 验证点 |
|---|---|
| `JobQueueSequentialExecutionInSameQueueTest` | 同队列内任务**串行**执行——同一 Job 类型不会并发操作同一 repo 的 bucket |
| `JobQueueParallelExecutionTest` | 不同队列**并行**执行——各 Job 互不阻塞 |
| `JobCancellationTest` | 待处理任务可取消，不影响正在运行的任务 |
| `JobFailureHandlingTest` | 任务失败不影响队列本身，不会导致后续任务无法执行 |
