# Apkg 打包格式参考

本文档描述两种 DSL 的完整语法：

- **`.aosproj`** — 声明一个包的内容、元信息与构建矩阵，由 `apkg` CLI 消费
- **`manifest.xml`**（v2）— 嵌入 `.apkg` 归档内的分发清单，由 Apkg 服务器消费

---

## 一、`.aosproj` 格式

`.aosproj` 是 MSBuild 风格的 XML 文件，描述**如何把本地文件打成 `.deb`**。

### 最小示例

```xml
<Project Sdk="Aiursoft.Apkg.Sdk">
  <PropertyGroup>
    <!--
      PackageName   → deb 的包名，同时也是将来 apt install <name> 的名字。
                      只允许小写字母、数字、连字符（-）、加号（+）和点（.），
                      首字符必须为字母或数字，不允许下划线或大写。
                      正则：^[a-z0-9][a-z0-9\-+.]*$
    -->
    <PackageName>anduinos-logo</PackageName>

    <!--
      PackageVersion → 遵循 Debian 版本规范。
                       纯数字点分（1.0.0）是最常见形式。
    -->
    <PackageVersion>1.0.0</PackageVersion>

    <!--
      PackageAuthors → 填入 deb 的 Maintainer 字段。
                       格式：姓名 <email>
    -->
    <PackageAuthors>AnduinOS Team &lt;dev@anduinos.com&gt;</PackageAuthors>

    <!-- deb 的 Description 字段，一行简介 -->
    <PackageDescription>AnduinOS logo assets</PackageDescription>

    <!--
      TargetDistro → 目标发行版的字符串标识，自由命名。
                     必须与 Apkg 服务器上配置的仓库 Distro 字段一致。
                     例：anduinos、ubuntu、debian。
    -->
    <TargetDistro>anduinos</TargetDistro>

    <!--
      TargetSuites → 空格分隔的 suite 列表。
                     每个 suite 单独产出一个 .deb 文件。
                     例："resolute questing" 会产出两个 .deb。
    -->
    <TargetSuites>resolute</TargetSuites>

    <!--
      TargetArchitectures → 空格分隔的架构列表，或 "all" 表示架构无关。
                            "all" 适合纯脚本、字体、图标等不含二进制的包。
                            多架构示例："amd64 arm64"
    -->
    <TargetArchitectures>all</TargetArchitectures>

    <!--
      Component → APT 仓库中的组件分类，通常填 main。
                  其余合法值取决于服务器上仓库的 Components 配置。
    -->
    <Component>main</Component>
  </PropertyGroup>

  <ItemGroup>
    <!--
      IncludeFile → 把一个文件安装到系统指定路径。
      Include     = 相对于 .aosproj 所在目录的源文件路径。
      Target      = 安装后在系统里的绝对路径（含文件名）。
    -->
    <IncludeFile Include="logo.svg" Target="/usr/share/pixmaps/anduinos-logo.svg" />
  </ItemGroup>
</Project>
```

### PropertyGroup 字段

| 字段 | 必填 | 说明 |
|------|------|------|
| `PackageName` | ✅ | deb 包名，正则 `^[a-z0-9][a-z0-9\-+.]*$`（小写字母、数字、`-`、`+`、`.`，首字符须为字母或数字） |
| `PackageVersion` | ✅ | 版本号，遵循 Debian 版本规范（如 `1.0.0`）。支持 `$(UpstreamVersion)` 变量自动从上游 .deb 的 Version 字段派生，如 `$(UpstreamVersion)-anduinos` |
| `PackageDescription` | ✅ | 包的单行简介 |
| `TargetSuites` | ✅ | 空格分隔的 suite 列表（如 `resolute questing`），每个 suite 产出一个 `.deb` |
| `PackageAuthors` | ⚠️ | 默认 Maintainer，可被 `Maintainer` 字段覆盖。lint Warning 但 `Maintainer` 可替代 |
| `TargetDistro` | ⚠️ | 发行版标识（如 `anduinos`、`ubuntu`）。lint Warning，构建全部 target 时硬依赖，单次 build 默认 `"ubuntu"` |
| `TargetArchitectures` | ⚠️ | 空格分隔的架构列表（如 `amd64 arm64`），或 `all` 表示架构无关。lint Warning，构建全部 target 时硬依赖 |
| `Component` | — | APT 组件，默认为 `main` |
| `PackageHomepage` | — | 项目主页 URL |
| `RepositoryUrl` | — | 源码仓库 URL |
| `LicenseType` | — | SPDX 标识符，如 `MIT`、`GPL-2.0` |
| `LicenseFile` | — | 许可证文件相对路径 |
| `PackageTags` | — | 逗号分隔的包标签 |
| `Maintainer` | — | 覆盖 `PackageAuthors` 作为 deb 的 `Maintainer` 字段 |
| `Provides` | — | deb `Provides` 字段，声明此包提供哪些虚包 |
| `Conflicts` | — | deb `Conflicts` 字段，声明与哪些包冲突 |
| `Replaces` | — | deb `Replaces` 字段，声明此包替换哪些旧包 |
| `UpstreamUrl` | ⚠️ | 上游 APT 仓库的 base URL（如 `http://archive.ubuntu.com/ubuntu`）。设置 `UpstreamPackage` 时必填 |
| `UpstreamDistro` | ⚠️ | 上游仓库的发行版标识（如 `ubuntu`）。设置 `UpstreamPackage` 时必填 |
| `UpstreamPackage` | — | 上游 .deb 的包名（如 `base-files`）。一旦设置，触发上游派生模式 |
| `UpstreamSuite` | ⚠️ | 上游 suite（如 `$(Suite)` 表示与构建 suite 同名）。设置 `UpstreamPackage` 时必填 |
| `UpstreamSuiteMapping` | — | 输出 suite → 上游 suite 的映射表。格式：`out1=up1, out2=up2`。当 `UpstreamSuite` 解析后的值命中了映射的 key，则替换为对应的上游 suite |
| `UpstreamComponent` | — | 上游 APT 组件，默认为 `main`。支持 `$(Suite)` 等变量 |
| `UpstreamArch` | — | 上游包架构，默认为 `all` |

### 上游派生（UpstreamSource）

当 `.aosproj` 设置了 `<UpstreamPackage>` 时，`apkg build` 进入**上游派生模式**——不从零构建，而是从一个已存在的 `.deb` 派生：

1. **下载**：通过隔离的 `apt-get` 从 `UpstreamUrl` 的 `UpstreamSuite` 下载 `UpstreamPackage`
2. **解包**：用 `dpkg-deb -x` 提取上游数据到暂存区，用 `dpkg-deb -e` 提取控制文件
3. **PrebuildCommand**：执行预构建命令（此时可对上游文件执行 `sed` 等操作）
4. **合并**：本地条目（`IncludeFile`、`IncludeScript`、`IncludeFolder`）覆盖到暂存区
5. **合并 control 字段**：
   - **Version**：若 `PackageVersion` 包含 `$(UpstreamVersion)`，则替换为上游的实际版本号（如 `13.1ubuntu1`）。这使得派生包的版本自动跟随上游
   - `Depends`：上游依赖在前，本地 `Dependency` 附录在后。以基础包名去重（如上游有 `libc6 (>= 2.34)` 而本地声明 `libc6`，保留上游版本）
   - `Provides`、`Conflicts`、`Replaces`：本地优先，未填时回退到上游值
   - `Homepage`：本地优先，未填时回退到上游值
   - `Section`、`Priority`：始终从上继承
6. **链式 maintainer scripts**：上游 `postinst`/`prerm`/`postrm`（去除 shebang）→ 本地脚本 → systemd 自动脚本，按序追加

这是 AnduinOS 替换 Ubuntu `base-files`（`Essential: yes`）等基础包的推荐模式：通过 APT pinning 设置 `Pin-Priority: 1001`，让 AnduinOS 的派生包覆盖 Ubuntu 原包，同时保持一切兼容。

**`base-files` 示例：**

```xml
<Project Sdk="Aiursoft.Apkg.Sdk">
  <PropertyGroup>
    <PackageName>base-files</PackageName>
    <PackageVersion>$(UpstreamVersion)-anduinos</PackageVersion>
    <PackageDescription>AnduinOS base files (derived from Ubuntu)</PackageDescription>
    <Maintainer>AnduinOS Team &lt;dev@anduinos.com&gt;</Maintainer>
    <TargetDistro>anduinos</TargetDistro>
    <TargetSuites>resolute questing</TargetSuites>
    <TargetArchitectures>amd64 arm64</TargetArchitectures>

    <!-- 上游派生：从 Ubuntu archive 下载 base-files .deb 并在此基础上叠加本地文件 -->
    <UpstreamUrl>http://archive.ubuntu.com/ubuntu</UpstreamUrl>
    <UpstreamDistro>ubuntu</UpstreamDistro>
    <UpstreamPackage>base-files</UpstreamPackage>
    <UpstreamSuite>$(Suite)</UpstreamSuite>
    <UpstreamComponent>main</UpstreamComponent>
    <UpstreamArch>amd64</UpstreamArch>
  </PropertyGroup>

  <!-- 覆盖上游文件：用 AnduinOS 定制的 /etc/issue, /etc/os-release 等替换上游文件 -->
  <ItemGroup>
    <IncludeFile Include="deploy/issue" Target="/etc/issue" />
    <IncludeFile Include="deploy/os-release" Target="/etc/os-release" />
    <IncludeFile Include="deploy/lsb-release" Target="/etc/lsb-release" />
  </ItemGroup>
</Project>
```

`$(Suite)` 变量会在构建时解析为 `resolute`、`questing` 等，实现同一 `.aosproj` 从不同上游 suite 下载对应版本。

`$(UpstreamVersion)` 变量仅在 `<PackageVersion>` 中可用。构建时，Apkg 从下载的上游 `.deb` 控制文件中读取 `Version` 字段并替换该占位符。例如 `<PackageVersion>$(UpstreamVersion)-anduinos</PackageVersion>` 对 noble suite（上游版本为 `13ubuntu10`）会生成 `13ubuntu10-anduinos`，对 questing suite（上游版本为 `14ubuntu3`）会生成 `14ubuntu3-anduinos`。

当输出 suite 名与上游 suite 名不同时，可通过 `<UpstreamSuiteMapping>` 建立映射。典型场景：AnduinOS 的 addon 仓库使用 `questing-addon` 等独立 suite（避免与 Official 仓库的 `questing` 冲突），但上游包仍需从 Ubuntu 的 `questing` 下载：

```xml
<PropertyGroup>
  <TargetSuites>noble-addon questing-addon resolute-addon</TargetSuites>
  <UpstreamSuite>$(Suite)</UpstreamSuite>
  <UpstreamSuiteMapping>noble-addon=noble, questing-addon=questing, resolute-addon=resolute</UpstreamSuiteMapping>
</PropertyGroup>
```

构建时，`$(Suite)` 先展开为 `noble-addon`，然后通过映射表查找到上游 suite `noble`，最终从 Ubuntu 的 `noble` 下载原包。不设映射时行为不变——解析后的 suite 直接用于上游下载。

`apkg lint` 可单独执行，也由 `apkg build` 在构建前自动调用。Error 级别问题会中止构建，Warning 仅打印提示。以下是它检查的全部规则：

| 规则 | 级别 |
|------|------|
| `PackageName`、`PackageVersion`、`PackageDescription`、`TargetSuites` 必须设置 | **Error** |
| `PackageName` 必须匹配 `^[a-z0-9][a-z0-9\-+.]*$` | **Error** |
| 所有条目的 `Target=` 不能为空 | **Error** |
| 所有条目的 `Include=`（`Source`）不能为空 | **Error** |
| `Condition` 表达式必须能被解析 | **Error** |
| `Maintainer` 与 `PackageAuthors` 至少填一个 | Warning |
| `UpstreamPackage` 设置时必须同时设置 `UpstreamUrl`、`UpstreamDistro`、`UpstreamSuite` | **Error** |
| `UpstreamPackage` 设置时 `UpstreamComponent` 未填（默认 `main`） | Warning |
| `UpstreamPackage` 设置时 `UpstreamArch` 未填（默认 `all`） | Warning |
| `UpstreamSuiteMapping` 中输出 suite 未在 `TargetSuites` 中声明 | Warning |
| `UpstreamSuiteMapping` 中上游 suite 为空 | **Error** |
| `TargetSuites` 中某 suite 未出现在 `UpstreamSuiteMapping` 中 | Warning |
| `TargetDistro` 未设（构建全部 target 时必须，默认回退 `ubuntu`） | Warning |
| `TargetArchitectures` 未设（构建全部 target 时必须） | Warning |
| 所有 `Include=` 指向的源文件/目录在磁盘上实际存在 | Warning |
| 至少声明一个文件条目（`IncludeFile`/`IncludeScript`/`IncludeFolder`/`ConfFile`），否则包为空 | Warning |

### ItemGroup 条目类型

所有条目都支持 MSBuild 风格的 `Condition` 属性，可用 `$(Distro)`、`$(Suite)`、`$(Arch)`（别名 `$(Architecture)`）、`$(Component)`、`$(UpstreamDistro)`、`$(UpstreamSuite)`、`$(UpstreamArch)`（别名 `$(UpstreamArchitecture)`）在构建矩阵中做条件分支。此外 `$(UpstreamVersion)` 可在 `PackageVersion` 中用作模板变量，构建时自动替换为上游版本号。

#### IncludeFile — 安装单个文件

```xml
<!--
  把一个文件原样安装到系统路径。
  适合图片、字体、数据文件、库文件等。
  Include = 相对于 .aosproj 的源路径
  Target  = 安装后的完整绝对路径，含文件名
-->
<IncludeFile Include="src/logo.svg" Target="/usr/share/pixmaps/anduinos-logo.svg" />

<!-- 带条件：只在 amd64 上包含特定库 -->
<IncludeFile
  Include="lib/amd64/libfoo.so"
  Target="/usr/lib/x86_64-linux-gnu/libfoo.so"
  Condition="'$(Arch)' == 'amd64'" />
```

#### IncludeScript — 安装可执行脚本或二进制

```xml
<!--
  与 IncludeFile 相同，但安装后自动将目标文件权限设为 0755（可执行）。
  适合放在 /usr/bin/、/usr/sbin/、/usr/lib/xxx/bin/ 下的脚本或二进制。
-->
<IncludeScript Include="bin/my-tool" Target="/usr/bin/my-tool" />
```

#### IncludeFolder — 安装目录树

```xml
<!--
  递归安装整个目录。目录下的所有文件会保持相对结构
  安装到 Target 指定的路径下。
  Include = 相对于 .aosproj 的源目录路径
  Target  = 安装后的目录路径
-->
<IncludeFolder Include="assets/" Target="/usr/share/anduinos/assets" />
```

#### ConfFile — 配置文件（受 dpkg 保护）

```xml
<!--
  安装一个配置文件，并将其路径写入 deb 的 conffiles 列表。
  效果：升级包时，若用户已修改该文件，dpkg 会提示是否用新版覆盖，
        而不是静默覆盖——这是 Debian 对用户配置的标准保护机制。
  适合放在 /etc/ 下的用户可编辑配置。
-->
<ConfFile Include="config/settings.conf" Target="/etc/anduinos/settings.conf" />
```

#### Dependency — 声明运行时依赖

```xml
<!--
  每个 Dependency 对应 deb control 文件 Depends 字段中的一项。
  所有条目在构建时合并，用 ", " 连接写入 Depends 字段。
  支持 Debian 版本约束语法：libfoo (>= 1.2)、libfoo (= 1.2.3) 等。
-->
<Dependency Include="libssl3" />
<Dependency Include="python3 (&gt;= 3.10)" />

<!--
  Condition 属性允许按 suite/arch 选择不同的依赖版本。
  常见场景：同一个库在不同 suite 里包名或 SONAME 不同。

  可用变量完整列表见上文 ItemGroup 条件语法说明（§ItemGroup 条目类型开头），
  常用：$(Suite)、$(Arch)、$(Distro)。字符串比较需加单引号。
-->
<Dependency Include="libssl3"    Condition="'$(Suite)' == 'resolute'" />
<Dependency Include="libssl3t64" Condition="'$(Suite)' == 'questing'" />
```

#### SystemdUnit — systemd 服务单元

```xml
<!--
  安装一个 systemd .service 文件，并自动生成完整的 maintainer scripts：
    postinst (configure, 新装)  → systemctl enable + systemctl start
    postinst (configure, 升级)  → systemctl try-restart
    prerm    (remove)           → systemctl stop
    postrm   (remove/purge)     → systemctl disable + daemon-reload
  无需手写任何 maintainer scripts。

  Include    = .service 文件相对于 .aosproj 的路径
  AutoEnable = true（默认）表示安装后立即 enable+start；
               false 表示只安装 unit 文件，不自动启动
-->
<SystemdUnit Include="service/my-daemon.service" AutoEnable="true" />
```

#### PrebuildCommand — 预构建命令

```xml
<!--
  在打 deb 之前执行的 shell 命令，适合编译步骤或资源生成。
  执行顺序：上游解包（如果配置了 UpstreamSource）→ PrebuildCommand → IncludeFile 等文件覆盖。
  因此可以在 PrebuildCommand 中对上游文件执行 sed、patch 等操作，再让本地文件覆盖最终版本。
  Run = 要执行的命令（在 .aosproj 所在目录下执行）
-->
<PrebuildCommand Run="make release" />

<!-- 带条件：只在 amd64 上执行特定编译步骤 -->
<PrebuildCommand Run="make amd64-extras" Condition="'$(Arch)' == 'amd64'" />

<!-- 上游派生模式下修改上游文件 -->
<PrebuildCommand Run="sed -i 's/Ubuntu/AnduinOS/g' obj/$(Suite)_$(Arch)/etc/issue" />
```

#### PostInstallScript — 安装后脚本

```xml
<!--
  安装后由 dpkg 执行的自定义 shell 脚本（DEBIAN/postinst）。
  适合 systemd 之外的初始化操作（如创建用户、设置权限、初始化数据库）。
  多个条目按声明顺序追加到同一个 postinst 脚本中。
-->
<PostInstallScript Include="scripts/postinst.sh" />
```

#### PreRemoveScript — 卸载前脚本

```xml
<!--
  卸载前由 dpkg 执行的自定义 shell 脚本（DEBIAN/prerm）。
  适合服务停止之外需要额外清理的操作。
-->
<PreRemoveScript Include="scripts/prerm.sh" />
```

### 完整示例（带条件依赖与构建矩阵）

```xml
<Project Sdk="Aiursoft.Apkg.Sdk">
  <PropertyGroup>
    <PackageName>anduinos-shell-ext</PackageName>
    <PackageVersion>2.1.0</PackageVersion>
    <PackageAuthors>AnduinOS Team &lt;dev@anduinos.com&gt;</PackageAuthors>
    <PackageDescription>AnduinOS GNOME Shell extensions</PackageDescription>
    <PackageHomepage>https://anduinos.com</PackageHomepage>
    <LicenseType>GPL-2.0</LicenseType>

    <!-- 目标发行版：anduinos（对应服务器上配置的仓库 Distro 名） -->
    <TargetDistro>anduinos</TargetDistro>

    <!--
      同时发布到两个 suite。
      apkg build --all 会产出 2 suites × 2 arches = 4 个 .deb 文件。
    -->
    <TargetSuites>resolute questing</TargetSuites>
    <TargetArchitectures>amd64 arm64</TargetArchitectures>

    <Component>main</Component>
  </PropertyGroup>

  <!-- 预构建步骤：生成资源文件 -->
  <ItemGroup>
    <PrebuildCommand Run="make assets" />
  </ItemGroup>

  <ItemGroup>
    <!--
      可执行工具，安装后可在终端直接运行 anduinos-shell-ext。
      IncludeScript 自动设置 0755 权限。
    -->
    <IncludeScript Include="bin/shell-ext" Target="/usr/bin/anduinos-shell-ext" />

    <!--
      GNOME Shell 扩展文件夹，整棵目录树都安装进去。
      安装后结构：/usr/share/gnome-shell/extensions/shell-ext@anduinos/
                    ├── extension.js
                    ├── metadata.json
                    └── ...
    -->
    <IncludeFolder Include="extension/" Target="/usr/share/gnome-shell/extensions/shell-ext@anduinos" />

    <!--
      用户可编辑的配置文件。写入 conffiles，升级时 dpkg 会保护用户改动。
    -->
    <ConfFile Include="config/shell-ext.conf" Target="/etc/anduinos/shell-ext.conf" />
  </ItemGroup>

  <ItemGroup>
    <!-- 所有 suite 共同依赖 -->
    <Dependency Include="gnome-shell (&gt;= 42)" />
    <Dependency Include="gir1.2-glib-2.0" />

    <!--
      libssl 在 resolute 和 questing 里包名不同（SONAME 变更），
      用 Condition 按 suite 分别声明。
    -->
    <Dependency Include="libssl3"    Condition="'$(Suite)' == 'resolute'" />
    <Dependency Include="libssl3t64" Condition="'$(Suite)' == 'questing'" />
  </ItemGroup>

  <ItemGroup>
    <!--
      后台辅助守护进程的 systemd unit。
      apkg build 自动生成 postinst/prerm/postrm，无需手写维护脚本。
    -->
    <SystemdUnit Include="service/shell-ext-helper.service" AutoEnable="true" />
  </ItemGroup>
</Project>
```

`apkg build --all` 对上述文件产出的构建矩阵：

```
resolute × amd64  →  bin/anduinos-shell-ext_2.1.0_resolute_amd64.deb
resolute × arm64  →  bin/anduinos-shell-ext_2.1.0_resolute_arm64.deb
questing × amd64  →  bin/anduinos-shell-ext_2.1.0_questing_amd64.deb
questing × arm64  →  bin/anduinos-shell-ext_2.1.0_questing_arm64.deb
```

resolute 的 amd64 包 `Depends` 字段将是：

```
gnome-shell (>= 42), gir1.2-glib-2.0, libssl3
```

questing 的 amd64 包 `Depends` 字段将是：

```
gnome-shell (>= 42), gir1.2-glib-2.0, libssl3t64
```

---

## 二、`manifest.xml` 格式（v2）

`manifest.xml` 是 `apkg publish` **自动生成**、嵌入 `.apkg` 归档的分发清单，**不需要手写**。Apkg 服务器读取它来决定把哪个 `.deb` 放进哪个 APT 仓库。

### 结构示例（对应上面的完整 `.aosproj`）

```xml
<?xml version="1.0" encoding="utf-8"?>
<!--
  FormatVersion="2" 是版本标识。
  服务器通过此属性区分新旧格式，拒绝识别不含该属性的旧格式文件。
-->
<ApkgPackage FormatVersion="2">

  <!-- 包的元信息，直接从 .aosproj 的 PropertyGroup 映射而来 -->
  <Name>anduinos-shell-ext</Name>
  <Version>2.1.0</Version>
  <Maintainer>AnduinOS Team &lt;dev@anduinos.com&gt;</Maintainer>
  <Description>AnduinOS GNOME Shell extensions</Description>
  <Homepage>https://anduinos.com</Homepage>
  <License>GPL-2.0</License>

  <!--
    Entries 是构建矩阵展开后的结果。
    每个 Entry 对应一个 suite × arch 组合，即一个 .deb 文件。
    服务器按 Distro + Suite + Architecture + Component 四元组
    定位目标仓库，找不到则跳过并记录警告。
  -->
  <Entries>

    <!--
      Entry 1：resolute suite，amd64 架构
      DebFile 是 .apkg 归档内的文件名，命名规则：
        <PackageName>_<Version>_<Suite>_<Architecture>.deb
    -->
    <Entry>
      <DebFile>anduinos-shell-ext_2.1.0_resolute_amd64.deb</DebFile>
      <Distro>anduinos</Distro>   <!-- 对应服务器仓库的 Distro 字段 -->
      <Suite>resolute</Suite>     <!-- 对应服务器仓库的 Suite 字段  -->
      <Component>main</Component> <!-- 对应服务器仓库的 Component   -->
      <Architecture>amd64</Architecture>
    </Entry>

    <!-- Entry 2：resolute suite，arm64 架构 -->
    <Entry>
      <DebFile>anduinos-shell-ext_2.1.0_resolute_arm64.deb</DebFile>
      <Distro>anduinos</Distro>
      <Suite>resolute</Suite>
      <Component>main</Component>
      <Architecture>arm64</Architecture>
    </Entry>

    <!-- Entry 3：questing suite，amd64 架构 -->
    <Entry>
      <DebFile>anduinos-shell-ext_2.1.0_questing_amd64.deb</DebFile>
      <Distro>anduinos</Distro>
      <Suite>questing</Suite>
      <Component>main</Component>
      <Architecture>amd64</Architecture>
    </Entry>

    <!-- Entry 4：questing suite，arm64 架构 -->
    <Entry>
      <DebFile>anduinos-shell-ext_2.1.0_questing_arm64.deb</DebFile>
      <Distro>anduinos</Distro>
      <Suite>questing</Suite>
      <Component>main</Component>
      <Architecture>arm64</Architecture>
    </Entry>

  </Entries>
</ApkgPackage>
```

### 顶层字段

| 字段 | 来源（对应 `.aosproj`） | 说明 |
|------|------------------------|------|
| `FormatVersion` | 固定为 `2` | 格式版本标识，服务器用于区分新旧格式 |
| `Name` | `PackageName` | 包名 |
| `Version` | `PackageVersion` | 版本号 |
| `Maintainer` | `Maintainer` 或 `PackageAuthors` | 维护者 |
| `Description` | `PackageDescription` | 简介 |
| `Homepage` | `PackageHomepage` | 主页 |
| `License` | `LicenseType` | 许可证 |

### `<Entry>` 字段

每个 `Entry` 对应构建矩阵中的一个 `suite × arch` 组合。

| 字段 | 说明 |
|------|------|
| `DebFile` | `.apkg` 归档内的 `.deb` 文件名，格式为 `pkgname_version_suite_arch.deb` |
| `Distro` | 目标发行版，与服务器仓库的 `Distro` 字段匹配 |
| `Suite` | APT suite 名（如 `resolute`、`questing`） |
| `Component` | APT 组件（如 `main`） |
| `Architecture` | CPU 架构（如 `amd64`、`arm64`、`all`） |

服务器根据 `Distro + Suite + Architecture + Component` 四元组定位目标仓库。若找不到匹配仓库，该 `Entry` 会被跳过并记录警告。

> ⚠️ **静默跳过陷阱**：如果 `TargetDistro`、`TargetSuites` 或 `Component` 与服务器上配置的仓库不完全匹配，`apkg push` 会成功返回，但那个 `.deb` 不会出现在任何 APT 仓库里。打包者不会收到任何错误——包是静默丢失的。
> 排查方法：检查服务器日志，或在目标机器上执行 `apt-cache show <pkgname>` 确认包是否可见。

---

## 三、CLI 工作流

### 主工作流（aosproj 模式）

```
apkg new       → 创建 .aosproj 骨架
apkg add       → 往 .aosproj 追加文件条目
apkg lint      → 验证 .aosproj 语法和文件存在性（见上方规则表）
apkg build     → 默认构建全部 TargetSuites × TargetArchitectures；--suite/--arch 限定单个目标
apkg publish   → 自动 lint + build（默认全部 target）+ 打包为 bin/<name>.<ver>.apkg；--no-build 跳过构建
apkg push      → 上传 .apkg 到 Apkg 服务器
```

### 旧命令（legacy，未来可能废弃）

```
apkg install    → 从本地 .apkg 解出匹配当前系统的 .deb 并执行 dpkg -i
apkg add-source → 在当前机器添加 Apkg APT 源到 /etc/apt/sources.list.d/
apkg unpack     → 解包 .apkg 归档（自动选择匹配当前系统的 .deb）
```

### `apkg build` 构建矩阵

`TargetSuites` × `TargetArchitectures` 的笛卡尔积产出所有 `.deb`：

```
TargetSuites:        resolute questing
TargetArchitectures: amd64 arm64

产出：
  bin/pkg_1.0.0_resolute_amd64.deb
  bin/pkg_1.0.0_resolute_arm64.deb
  bin/pkg_1.0.0_questing_amd64.deb
  bin/pkg_1.0.0_questing_arm64.deb
```

构建时会在 `obj/<suite>_<arch>/` 下生成临时暂存目录（含 `DEBIAN/` 控制文件和所有已复制的载荷文件），然后调用 `dpkg-deb --build`。构建失败时可在此目录检查生成内容，正常完成后可安全删除。

不带参数默认构建完整矩阵（等同于 `--all`）；使用 `--suite` / `--arch` 限定单个目标。

### `apkg push` 参数

```bash
apkg push bin/anduinos-shell-ext.2.1.0.apkg \
  --source https://apkg-dev.aiursoft.com \
  --api-key <你的 API Key>
```

