# Apkg Pipeline V2 Design - Suite Container Mode

## 核心架构演进
从原本的“细粒度分片模式”（1 实体 = 1 Component/Arch）演进为“集装箱模式”（1 实体 = 1 Suite 包含多个分片）。

## 核心组件定义

### 1. AptMirror (镜像源/采购员)
* **地位**：数据的入站（Ingress）点。
* **职责**：定义如何从上游（如 Ubuntu 官方）抓取一整套 Suite（含 main, universe 等多个组件）。
* **产物**：产生一个 Mirror-Bucket 快照，存储该时刻上游的所有包元数据。

### 2. AptRepository (仓库/门店)
* **地位**：数据的出站（Egress）点，对外服务的招牌。
* **职责**：
    * 关联一个 AptMirror 作为供货源。
    * 持有 GPG 证书。
    * 定时从关联的 Mirror 桶中复制数据，计算哈希，并进行 GPG 重签名。
* **产物**：产生一个 Repository-Bucket 快照，存储本地签名后的 `InRelease`、`Packages` 总索引。

### 3. AptBucket (快照桶/货架)
* **地位**：版本化载体。
* **特性**：
    * 原子切换：通过修改 `CurrentBucketId` 指针瞬间完成 Primary/Secondary 轮转。
    * 自包含性：存储该版本下的 `InRelease` 和 `Release` 文本。
    * GC 基础：不再被任何实体引用的旧桶将被清理。

### 4. AptPackage (包/商品)
* **归属**：强关联到某个 `AptBucket`。
* **属性**：
    * `Component`: 标记其所属组件（main, universe...）。
    * `Architecture`: 标记其架构（amd64...）。
    * `IsVirtual`: 虚包标记，支持按需（Lazy Sync）转为实包。

## 数据流动路径 (The Grocery Store Logic)

1. **Mirror Job** -> 创建 **Mirror-Bucket** -> 写入上游包 -> 切换 Mirror 指针。
2. **Repository Job** -> 从 Mirror 指针处 **全量复制** 到 **Repository-Bucket** -> 运行 **AptMetadataService** 生成总表 -> **GPG 签名** -> 切换 Repository 指针。
3. **Controller** -> 收到请求 -> 从 Repository 指向的 **Repository-Bucket** 极速响应已签名的元数据。
4. **Lazy Sync** -> 用户下载包 -> 虚转实（阻塞下载 + 磁盘持久化 + 数据库状态更新）。

## 优势
* **强一致性**：一整个 Suite 的元数据在同一 Bucket 内闭环，消除了多组件同步的时间差问题。
* **高性能**：GPG 签名在 Job 阶段完成并存入 Bucket，请求响应时无需计算。
* **逻辑清晰**：1 个 Repository 实体对应 1 个 `InRelease` 接口。
