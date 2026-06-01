# Apkg 开发指南

## CI 流水线

3 阶段（GitLab CI, `.gitlab-ci.yml`）：

1. **build** — `dotnet restore --no-cache` → `dotnet build -maxcpucount:1 --no-self-contained`
2. **lint** — `lint.sh`（JetBrains ReSharper 代码检查）
3. **test** — `dotnet test` + 代码覆盖率（Cobertura），通过 `reportgenerator` 生成报告

master 分支额外：`dotnet pack` → 部署 NuGet → Docker 多架构构建推送（linux/amd64, linux/arm64）。

## 测试基础设施

- **框架**: MSTest
- **数据库**: InMemory（通过 `EntryExtends.IsInUnitTests()` 自动切换）
- **测试基类**: `TestBase` — 提供 `Http`（HttpClient）、`Server`（TestServer）、`LoginAsAdmin()`、`PostForm()`
- **核心测试类**: `AtomicBucketCreationTests`、`GcSignRaceConditionTests`、`RepositorySignJobTests`、`RepositorySyncLocalPackagesTests`、`BackgroundJobsTests`

### MSTest 异步异常断言限制

MSTest 不支持 `Assert.ThrowsExceptionAsync`。异步方法抛异常的测试必须用 try-catch + Assert.Fail() 模式：

```csharp
try {
    await someAsyncOperation();
    Assert.Fail("Expected SomeException was not thrown.");
} catch (SomeException) {
    // expected
}
```

项目中 `FileAccessSecurityTest`、`GlobalSettingsTests`、`AptClient.IntegrationTests` 等多处使用了此模式。

## 开发陷阱

### Migration 超时

`ApkgDbContext.MigrateAsync()` (Entities/ApkgDbContext.cs:35-39) 设置 `Database.SetCommandTimeout(TimeSpan.FromMinutes(10))`。大型表上执行 DDL（如 `ALTER TABLE MODIFY COLUMN`）可能触发全表重建。若迁移命中 10 分钟超时，说明操作太重不适合在线迁移 — 拆分或使用后台回填。

### 多数据库迁移同步

修改 EF Core 模型后，必须为 MySQL 和 SQLite 各生成一份迁移：

```bash
dotnet ef migrations add <Name> --project src/Aiursoft.Apkg.MySql --startup-project src/Aiursoft.Apkg
dotnet ef migrations add <Name> --project src/Aiursoft.Apkg.Sqlite --startup-project src/Aiursoft.Apkg
```

两个 Provider 的迁移必须同时提交，只提交一个会导致 CI 失败。

### InMemory 数据库无 FK 约束

测试用 InMemory 数据库没有外键约束。依赖 FK 违规的测试（如 `SignJob_WhenSecondaryBucketAlreadyDeleted_ClearsDanglingReference`）只能在 InMemory 下工作 — 真实 SQLite/MySQL 会直接报错。InMemory 测试不会捕获缺失的 FK 级联。

**CI 覆盖盲区**：`EntryExtends.IsInUnitTests()` 在测试中强制使用 InMemory，SQLite 和 MySQL 路径永远不会在 CI 中运行。SQLite 特有的并发写死锁、MySQL 的 FOREIGN KEY 行为差异等 bug 无法被 CI 捕获。涉及存储层变更时需手动在两种真实数据库上验证。

### Docker 构建缓存

Dockerfile 先单独复制 `package.json`/`package-lock.json` 再复制完整源码，使 `npm install` 层可被缓存。只改 C# 代码时 npm 层复用；改 npm 依赖时缓存失效。

### StreamContent 必须用 factory 方法初始化

`new StreamContent(stream) { Headers = { ... } }` 在 using 语句中使用时，属性初始化器在 using 作用域之外执行 — 若 Headers 赋值抛出异常，stream 已离开 using 并被释放，但 StreamContent 持有已释放的引用，导致后续读取失败。修复方法：提取 factory 方法 `CreateStreamContent(stream)`，在 using 作用域内完成初始化后返回。

### 测试 Cookie 隔离

每个集成测试需要独立的 `HttpClient` 实例。多个测试共享同一个 `HttpClient` 实例时，前一个测试的认证 cookie 会泄漏到后一个测试，导致权限测试误判。`TestBase` 每次测试都创建新的 `HttpClient`。

### Redirect 路径混淆

`/ApkgPackages` vs `/ApkgPackages/Index` — ASP.NET Core 的路由匹配可能将一个重定向到另一个，导致 POST 数据在重定向中丢失（HTTP 302 将 POST 转为 GET）。始终在表单 action 中使用完整路径 `/Controller/Action`。

### 编译缓存竞争 MSB3492

`dotnet build` 在 CI 中使用 `-maxcpucount:1` 避免并行编译时的文件锁定竞争。本地遇到 `MSB3492` 错误（文件被另一个进程使用）时，删除 `obj/` 和 `bin/` 目录重新编译。

### 构建缓存与 --no-cache

CI 和 lint.sh 中的 restore 都使用 `--no-cache` 标志，因为 NuGet 缓存曾导致 CI 中出现过期包的构建失败。本地遇到诡异的包版本问题时可尝试 `dotnet restore --no-cache`。

### Razor.SectionNotResolved 抑制

UiStack Layout 使用动态 Section 渲染，Razor 编辑器无法解析跨程序集的 Section 引用。在自定义 Section 上方添加抑制注释：

```cshtml
@* ReSharper disable once Razor.SectionNotResolved *@
@section Scripts {
    ...
}
```

项目中 31 处抑制注释（GlobalSettings、Repositories、Mirrors、Buckets、Jobs 等视图）。

### Scheduled Task 调度

后台任务使用相对偏移（startDelay）调度，不是绝对壁钟时间（见 Startup.cs:72-117）：

| 任务 | 周期 | startDelay |
|------|------|-----------|
| MirrorSyncJob | 每 6h | 10min |
| GarbageCollectionJob | 每 70min | 15min |
| RepositorySyncJob | 每 4h | 20min |
| RepositorySignJob | 每 5min | 25min |
| OrphanAvatarCleanupJob | 每 6h | 5min |
| ApkgTempCleanupJob | 每 10min | 7min |

Startup.cs 注释中"an idea run steps"的时间线仅为示意，实际触发时间 = `app_start_time + startDelay + n × period`。

所有任务可通过 `/Jobs` 页面手动触发。触发前确认没有同名任务正在运行。
