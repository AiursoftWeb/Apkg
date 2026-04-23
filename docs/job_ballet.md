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

## 完整工作流程（以 Repository 为例）

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

1. **只有 SignJob 能写 PrimaryBucketId**：这是整个系统安全的根基。
2. **Secondary 是保护区，不是公开区**：任何处于 Secondary 位置的 bucket 对 APT 客户端不可见。
3. **GC 的边界由引用关系决定，不依赖时间戳**：彻底消除了"2小时猶予"这类时间魔法数字。
4. **导航属性单次 SaveChanges**：EF Core 保证 bucket 插入和外键更新在同一事务，消除孤儿窗口。
5. **每个任务独立可重试**：任何任务崩溃后重新运行，都能从正确的状态继续。
