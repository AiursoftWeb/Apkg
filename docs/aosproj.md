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
| `RepositoryUrl` | — | 源码仓库 URL（如 GitHub 链接）。传递至 manifest 和 ApkgPackage，供 Web UI 展示。不进 DEBIAN/control |
| `LicenseType` | — | SPDX 标识符，如 `MIT`、`GPL-2.0` |
| `LicenseFile` | — | 许可证文件相对路径 |
| `PackageTags` | — | 逗号分隔的包标签。目前保留为未来使用，暂无后端路由支持 |
| `Maintainer` | — | 覆盖 `PackageAuthors` 作为 deb 的 `Maintainer` 字段 |
| `Provides` | — | deb `Provides` 字段，声明此包提供哪些虚包 |
| `Conflicts` | — | deb `Conflicts` 字段，声明与哪些包冲突 |
| `Replaces` | — | deb `Replaces` 字段，声明此包替换哪些旧包 |
| `Breaks` | — | deb `Breaks` 字段，声明此包会破坏哪些其他包的特定版本。本地优先，未填时回退到上游值。仅在需要阻断旧版本兼容时才需填写 |
| `Recommends` | — | deb `Recommends` 字段，声明强烈推荐但非必须的软件包（`apt install` 默认安装，`apt remove` 时不会破坏依赖） |
| `Suggests` | — | deb `Suggests` 字段，声明可选的锦上添花软件包（`apt` 不自动安装，仅作提示） |
| `Section` | — | deb `Section` 字段（如 `utils`, `admin`, `editors`）。三级回退：本地 → 上游 → Debian 标准默认 `"utils"`。影响 APT 分类搜索 |
| `Priority` | — | deb `Priority` 字段（如 `optional`, `required`, `important`）。三级回退：本地 → 上游 → Debian 标准默认 `"optional"`。通常保持默认即可 |
| `DependencyCheckUrl` | — | lint 阶段用于验证 Depends/Recommends 依赖是否存在的 apt 服务器 base URL（如 `https://mirror.aiursoft.com/ubuntu`）。**留空则跳过依赖检查**。不填写时默认跳过，建议在每个项目中显式配置 |
| `DependencyCheckSuiteMap` | — | 将目标 suite 名映射到 `DependencyCheckUrl` 上的 suite 名。格式与 `UpstreamSuiteMapping` 相同：空格/逗号分隔的 `target=check` 对（如 `noble-addon=noble questing-addon=questing`）。若留空则直接用目标 suite 名查询 |
| `UpstreamUrl` | ⚠️ | 上游 APT 仓库的 base URL（如 `http://archive.ubuntu.com/ubuntu`）。设置 `UpstreamPackage` 时必填 |
| `UpstreamDistro` | ⚠️ | 上游仓库的发行版标识（如 `ubuntu`）。设置 `UpstreamPackage` 时必填 |
| `UpstreamPackage` | — | 上游 .deb 的包名（如 `base-files`）。一旦设置，触发上游派生模式 |
| `UpstreamSuite` | ⚠️ | 上游 suite（如 `$(Suite)` 表示与构建 suite 同名）。设置 `UpstreamPackage` 时必填 |
| `UpstreamSuiteMapping` | — | 输出 suite → 上游 suite 的映射表。格式：`out1=up1, out2=up2`。当 `UpstreamSuite` 解析后的值命中了映射的 key，则替换为对应的上游 suite |
| `UpstreamComponent` | — | 上游 APT 组件，默认为 `main`。支持 `$(Suite)` 等变量 |
| `UpstreamArch` | — | 上游包架构，默认为 `all` |
| `SuppressUpstreamScripts` | — | 设为 `true` 时，不继承上游包的 maintainer scripts（preinst/postinst/prerm/postrm），仅继承其数据载荷。适用于仅需上游文件但需完全自控安装脚本的场景。默认为 `false` |
| `SuppressUpstreamDependencies` | — | 空格/逗号分隔的上游包名列表，在合并本地依赖前从上游继承的 `Depends` 中移除。例如 `"ubuntu-pro-client ubuntu-advantage-desktop-daemon"`。移除时仅匹配基础包名（忽略版本约束），大小写不敏感 |

### 上游派生（UpstreamSource）

当 `.aosproj` 设置了 `<UpstreamPackage>` 时，`apkg build` 进入**上游派生模式**——不从零构建，而是从一个已存在的 `.deb` 派生：

1. **下载**：通过隔离的 `apt-get` 从 `UpstreamUrl` 的 `UpstreamSuite` 下载 `UpstreamPackage`
2. **解包**：用 `dpkg-deb -x` 提取上游数据到暂存区，用 `dpkg-deb -e` 提取控制文件
3. **PrebuildCommand**：执行预构建命令（此时可对上游文件执行 `sed` 等操作）
4. **合并**：本地条目（`IncludeFile`、`IncludeScript`、`IncludeFolder`）覆盖到暂存区
5. **合并 control 字段**：
   - **Version**：若 `PackageVersion` 包含 `$(UpstreamVersion)`，则替换为上游的实际版本号（如 `13.1ubuntu1`）。这使得派生包的版本自动跟随上游
   - `Depends`：上游依赖在前，本地 `Dependency` 附录在后。以基础包名去重（如上游有 `libc6 (>= 2.34)` 而本地声明 `libc6`，保留上游版本）。设置 `SuppressUpstreamDependencies` 可在合并前按包名移除上游依赖
   - `Provides`、`Conflicts`、`Replaces`、`Breaks`、`Recommends`、`Suggests`：本地优先，未填时回退到上游值
   - `Homepage`：本地优先，未填时回退到上游值
   - `Section`、`Priority`：三级回退 — 本地优先，未填时回退到上游值，上游也没有时使用 Debian 标准默认值（`"utils"` / `"optional"`）
6. **链式 maintainer scripts**：上游 `preinst`/`postinst`/`prerm`/`postrm`（去除 shebang）→ 本地脚本 → systemd 自动脚本，按序追加。设置 `<SuppressUpstreamScripts>true</SuppressUpstreamScripts>` 可跳过上游脚本，仅保留本地脚本和 systemd 自动脚本

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

所有条目都支持 MSBuild 风格的 `Condition` 属性，支持以下运算符和变量。

**变量**：`$(Distro)`、`$(Suite)`、`$(Arch)`（别名 `$(Architecture)`）、`$(Component)`、`$(UpstreamDistro)`、`$(UpstreamSuite)`、`$(UpstreamArch)`（别名 `$(UpstreamArchitecture)`）。此外 `$(UpstreamVersion)` 可在 `PackageVersion` 中用作模板变量，构建时自动替换为上游版本号。

**运算符**：

| 运算符 | 说明 | 示例 |
|--------|------|------|
| `==` | 等于 | `'$(Arch)' == 'amd64'` |
| `!=` | 不等于 | `'$(Suite)' != 'noble-addon'` |
| `and` | 逻辑与 | `'$(Suite)' == 'questing-addon' and '$(Arch)' == 'amd64'` |
| `or` | 逻辑或 | `'$(Suite)' == 'noble-addon' or '$(Suite)' == 'questing-addon'` |

`and` 优先级高于 `or`，字符串比较大小写不敏感。未指定 `Condition` 属性（或为空）表示该条目始终生效。

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

  符号链接行为（v10.0.4 起）：
  - 文件符号链接（如 libfoo.so -> libfoo.so.1）会被原样保留为符号链接，
    不会展开（dereference）为实体文件拷贝。
  - 目录符号链接（如 codecs/alias -> codecs/real）会被原样保留为目录
    符号链接，不会递归进入。这避免了同一文件以多条路径重复出现在
    最终 .deb 包中，也杜绝了循环符号链接导致的无限枚举。
  - 普通文件和目录的行为不变：正常递归复制。
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

#### Recommends 与 Suggests — 推荐/可选依赖

`Recommends` 和 `Suggests` 直接作为 `<PropertyGroup>` 里的字符串填写，格式与 `Depends` 相同（逗号分隔，支持版本约束）。

```xml
<!--
  Recommends：强烈推荐，但非必须。
    - apt install 默认一并安装（除非用 --no-install-recommends）
    - apt remove 单独卸载推荐包时不会破坏当前包的依赖
    - 典型用途：元包（meta-package）列出它所代表的所有组件
-->
<Recommends>gnome-shell-extension-blur-my-shell, gnome-shell-extension-arcmenu</Recommends>

<!--
  Suggests：可选的锦上添花。
    - apt 不自动安装，仅作文字提示
    - 典型用途：列出可以增强此包功能但完全独立的工具
-->
<Suggests>gnome-tweaks</Suggests>
```

> **元包模式**：创建一个没有任何 `IncludeFile`/`IncludeFolder` 的包，全部内容只有 `Recommends`，
> 即可实现类似 `ubuntu-desktop` 的元包语义——安装它会拉入一组软件，但可以单独卸载其中任意一个而不报依赖错误。

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

#### PreInstallScript — 安装前脚本

```xml
<!--
  安装前由 dpkg 执行的自定义 shell 脚本（DEBIAN/preinst）。
  适合安装前需要的准备工作（如备份旧数据、检查先决条件）。
  多个条目按声明顺序追加到同一个 preinst 脚本中。
-->
<PreInstallScript Include="scripts/preinst.sh" />
```

#### PostRemoveScript — 卸载后脚本

```xml
<!--
  卸载后由 dpkg 执行的自定义 shell 脚本（DEBIAN/postrm）。
  适合卸载后的残留清理操作（如删除日志、清理缓存）。
  systemd unit 的 disable + daemon-reload 会在自定义脚本之后自动追加。
-->
<PostRemoveScript Include="scripts/postrm.sh" />
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

### 设计原则：六属性铁律

一个 `.apkg` 包包含**两类属性**：

| 类别 | 属性 | 位置 | 语义 |
|------|------|------|------|
| **死属性** | `Name`, `Distro`, `Component` | manifest 根 | 唯一确定一个 APKG 包。三要素一旦设定即不可变。改变任意一个即为全新包。 |
| **活属性** | `Version`, `Suite`, `Architecture` | manifest Entry | 每次上传可混合。一个 apkg 内可包含多个 `(Version, Suite, Arch)` 组合的 .deb 文件。 |

- **Version 不在 manifest XML 中**。服务端直接从 `.deb` 文件内部解析版本号 — deb 本身是版本号的唯一真实来源。
- 所有 Entry 共享同一个根层的 `Distro` 和 `Component`。一个 apkg 只面向一个发行版的一个组件。
- `(Name, Distro, Component)` 三元组在数据库中全局唯一。第一个上传该三元组的用户拥有其所有权，其他用户上传相同三元组会被拒绝（403）。

### 结构示例（对应上面的完整 `.aosproj`）

```xml
<?xml version="1.0" encoding="utf-8"?>
<ApkgPackage FormatVersion="2">

  <!-- ═══ 3 个死属性 — 包的唯一身份 ═══ -->
  <Name>anduinos-shell-ext</Name>
  <Distro>anduinos</Distro>
  <Component>main</Component>

  <!-- 包元信息（从 .aosproj PropertyGroup 映射） -->
  <Maintainer>AnduinOS Team &lt;dev@anduinos.com&gt;</Maintainer>
  <Description>AnduinOS GNOME Shell extensions</Description>
  <Homepage>https://anduinos.com</Homepage>
  <License>GPL-2.0</License>

  <!--
    Entries — 构建矩阵展开结果。
    每个 Entry = 一个 (Suite, Architecture) 组合 = 一个 .deb 文件。
    服务端按 (Distro, Suite, Architecture) 三元组定位目标仓库。
    Version 从 .deb 文件内部解析，不出现在 manifest 中。
  -->
  <Entries>
    <Entry>
      <DebFile>anduinos-shell-ext_2.1.0_resolute_amd64.deb</DebFile>
      <Suite>resolute</Suite>
      <Architecture>amd64</Architecture>
    </Entry>

    <Entry>
      <DebFile>anduinos-shell-ext_2.1.0_resolute_arm64.deb</DebFile>
      <Suite>resolute</Suite>
      <Architecture>arm64</Architecture>
    </Entry>

    <Entry>
      <DebFile>anduinos-shell-ext_2.1.0_questing_amd64.deb</DebFile>
      <Suite>questing</Suite>
      <Architecture>amd64</Architecture>
    </Entry>

    <Entry>
      <DebFile>anduinos-shell-ext_2.1.0_questing_arm64.deb</DebFile>
      <Suite>questing</Suite>
      <Architecture>arm64</Architecture>
    </Entry>
  </Entries>
</ApkgPackage>
```

### 根层字段（死属性 — 包身份）

| 字段 | 来源（`.aosproj`） | 说明 |
|------|-------------------|------|
| `FormatVersion` | 固定 `2` | 格式版本标识 |
| `Name` | `PackageName` | 包名。三元组之一，不可变。 |
| `Distro` | `TargetDistro` | 目标发行版。三元组之一，不可变。 |
| `Component` | `Component` | APT 组件。三元组之一，不可变。 |
| `Maintainer` | `Maintainer` 或 `PackageAuthors` | 维护者 |
| `Description` | `PackageDescription` | 简介 |
| `Homepage` | `PackageHomepage` | 主页 |
| `License` | `LicenseType` | 许可证 |

### `<Entry>` 字段（活属性 — 每次上传可变）

| 字段 | 说明 |
|------|------|
| `DebFile` | `.apkg` 归档内 `.deb` 文件名。命名规则：`{Name}_{Version}_{Suite}_{Arch}.deb` |
| `Suite` | APT suite 名（如 `resolute`、`questing`） |
| `Architecture` | CPU 架构（`amd64`、`arm64`、`all`）。`all` 匹配任意架构仓库。 |

服务端根据 `(Distro, Suite, Architecture)` 元组定位目标仓库（`Distro` 来自根节点，`Suite` 和 `Architecture` 来自 Entry）。找不到匹配仓库时跳过该 Entry 并记录警告。

> ⚠️ **静默跳过陷阱**：`apkg push` 返回成功不代表所有 `.deb` 都进了仓库。如果服务器上没有配置匹配的仓库，对应的 `.deb` 会被静默丢弃，打包者不会收到任何错误提示。

#### 为什么发生：两层身份体系

Apkg 的 push 流程涉及**两套互相独立的三元组**，它们各司其职但只有一个交集字段 `Distro`：

| 层面 | 三元组 | 作用 | 失败后果 |
|------|--------|------|---------|
| **ApkgPackage 身份** | `(Name, Distro, Component)` | 确定"这是谁的包"、归属哪个用户、Revision 往哪累积 | **显式错误**（403 所有权冲突、manifest 格式错误） |
| **APT 仓库路由** | `(Distro, Suite, Architecture)` | 决定 `.deb` 文件落入哪个 AptRepository | **静默跳过**（仅服务端日志留一行 Warning） |

注意两个三元组的字段是**不同的**：
- 身份三元组包含 **`Component`**，不含 `Architecture`
- 路由三元组包含 **`Architecture`**，不含 `Component`

唯一的交集是 `Distro`。所以**即使你的 `Distro` 写对了（ApkgPackage 身份正常创建），只要 `Suite` 或 `Architecture` 与服务器上的仓库配置不一致，对应的 `.deb` 就会静默消失。**

#### 具体场景

假设你写了这样的 `.aosproj`：

```xml
<PackageName>my-tool</PackageName>
<TargetDistro>anduinos</TargetDistro>
<Component>main</Component>
<TargetSuites>noble-addon questing-addon</TargetSuites>
<TargetArchitectures>amd64</TargetArchitectures>
```

`apkg push` 时，身份三元组 `(my-tool, anduinos, main)` 正常命中 ApkgPackage。然后两个 Entry 分别路由：

```
Entry 1: (anduinos, noble-addon,    amd64) → 服务器上有这个仓库 ✅ → .deb 入库
Entry 2: (anduinos, questing-addon, amd64) → 服务器上没有这个仓库   ❌ → 跳过，丢弃
```

push 返回 200。你以为 questing 用户也能 `apt install my-tool`，实际上那个 `.deb` 已经被丢弃了。下次管理员创建 questing-addon 仓库后，你**必须重新 push 一次**——已丢弃的 .deb 不会自动恢复。

> **与 NuGet 的类比**：NuGet 服务器原样存储整个 .nupkg，TFM 匹配发生在**客户端**。如果管理员没配 `net10.0` feed，你以后配了就行，不需要重新上传。Apkg 不同——Suite/Architecture 路由发生在**服务端 push 时**，没匹配上就直接丢弃。这不是 Bug，而是 APT 仓库架构决定的：每个 `(Distro, Suite, Architecture)` 组合对应一个独立的仓库端点，服务端必须在 push 时决定 deb 存到哪。

#### 给包发布者

1. **Push 前先确认服务器上存在哪些仓库**。联系你的服务器管理员，确认你的 `TargetDistro`、`TargetSuites`、`TargetArchitectures` 对应的仓库都已创建。
2. **Push 后验证**。在目标机器上执行 `apt-cache show <你的包名>` 确认包是否在各个 suite 下都可见。如果只在一个 suite 下能看到，说明其他的被丢弃了。
3. **问题不是你的 `.aosproj` 写错了**——是服务器侧配置不完整。责任在仓库管理员，但你需要主动推动他们对齐配置。

#### 给服务器管理员

1. **仓库创建应当与包发布者的声明对齐**。如果你知道有包声明了 `TargetSuites="noble-addon questing-addon resolute-addon"`，就应当创建三个对应的 `AptRepository`，分别覆盖这三个 suite。
2. **在 Web UI 或文档中公开你的仓库矩阵**。让包发布者能查到：
   - 支持的 Distro 列表
   - 每个 Distro 下的 Suite 列表
   - 每个 Suite 支持的 Architecture 列表
3. **监控服务端日志**。`"No repository found for (Distro=X, Suite=Y, Arch=Z)"` 这类 Warning 意味着有包发布者的 `.deb` 被丢弃了——这通常是你需要新建仓库的信号。
4. **Component 不参与路由**。一个 AptRepository 的 Component 列表（如 `main restricted universe`）决定了该仓库**包含哪些组件**，但不影响路由匹配。路由只看 `(Distro, Suite, Architecture)`。

> 💡 **最佳实践**：服务器管理员和包发布者应共享一份"支持的构建矩阵"文档。例如 AnduinOS 的官方矩阵是：
>
> | Distro | Suite | Architectures |
> |--------|-------|--------------|
> | anduinos | noble-addon | amd64, arm64 |
> | anduinos | questing-addon | amd64, arm64 |
> | anduinos | resolute-addon | amd64, arm64 |
>
> 包发布者只声明这张表里存在的 `(Distro, Suite, Arch)` 组合，管理员保证所有声明的组合都有对应的仓库。两边对齐就不会有静默丢失。

排查方法：检查服务器日志中的 `"No repository found"` 警告，或在目标机器上执行 `apt-cache show <pkgname>` 确认包是否可见。

### Component 冲突陷阱：改 Component 不会绕过 slot dedup

与 Suite/Arch 的路由问题不同，Component 面临的是**另一个维度的陷阱**。如果你改了 `.aosproj` 的 `<Component>`，想把同一个包推到同一个仓库的不同组件，**第二次 push 会被服务端 409 拒绝**。

#### 为什么：三层身份体系中 Component 的位置

| 层面 | 实体 | Component 是否在唯一键中 | 效果 |
|------|------|------------------------|------|
| ApkgPackage 身份 | `ApkgPackage` | **是** — `(Name, Distro, Component)` | 改 Component → 全新包家族 ✅ |
| LocalPackage 去重 | `ApkgDebPackage` | **否** — `(RepositoryId, Package, Version, Architecture)` | 改 Component → 仍然冲突 ❌ |
| APT 输出 | `AptPackage` | 是 — `(Package, Version, Architecture, Component)` | 到不了这一层 |

`ApkgPackage` 层会正确识别为新包，但 `DebUploadService` 的 slot conflict 检查用的是 `(RepositoryId, Package, Version, Architecture)` —— Component 不在其中。只要这四个字段相同，无论 Component 改成了什么，第二次 push 都会返回 **409 Conflict**。

#### 这不是 Bug

同一个仓库、同一个 `(Distro, Suite, Architecture)` 组合下，同一个包以相同版本出现在两个 Component 中在 APT 协议层面会造成混淆——APT 客户端看到两份完全相同的条目，Pin-Priority 无法区分。Apkg 的设计选择是"一个包只属于一个 Component"，这个限制是**有意为之**。

#### 给包发布者

- **改 Component 意味着换包的身份**。新的 Component 会创建新的 `ApkgPackage` 家族（可能由不同用户拥有），这是合法的。
- **但如果目标仓库已经有同 (Package, Version, Architecture) 的包**——不管它是哪个 Component 来的——push 会被拒绝。
- **如果你确实需要跨 Component 迁移**：先在服务器上 disable 旧 Component 下的 LocalPackage，再用新 Component 重新 push。

#### 给服务器管理员

- **409 Conflict 不是你的服务器配置问题**——是包发布者的 push 内容与已有数据冲突。
- 如果你在日志中看到 409，告诉包发布者先 disable 旧包再重试，而不是让你在服务器侧做什么。

---

## 三、CLI 工作流

### 主工作流（aosproj 模式）

```
apkg new       → 创建 .aosproj 骨架
apkg add       → 往 .aosproj 追加文件条目
apkg lint      → 验证 .aosproj 语法和文件存在性（见上方规则表）
apkg build     → 默认构建全部 TargetSuites × TargetArchitectures；--suite/--arch 限定单个目标
apkg publish   → 自动 lint + build（默认全部 target）+ 打包为 bin/<name>.apkg；--no-build 跳过构建
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
apkg push bin/anduinos-shell-ext.apkg \
  --source https://apkg-dev.aiursoft.com \
  --api-key <你的 API Key>
```

