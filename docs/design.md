现在，我决定专注于真正重要的事情！构建一个全新的包管理。

显然，它也不完全是全新的。服务器我计划全部自己手工开发。但是它必须和APT完全兼容。

**为什么构建 Apkg？从“UI 换肤”到“平台主权”的跨越**

现有的 Linux 包管理设施（如 Launchpad/PPA）是为二十年前的开源协作模式设计的，已无法满足现代化的产品交付需求。构建 Apkg 核心架构旨在为 AnduinOS 建立三大战略护城河：

1.  **供应链主权 (Supply Chain Sovereignty)**：通过服务端的“中间件机制”，我们能以零成本清洗上游（Ubuntu）的数据，在源头剔除 Snap 等商业捆绑，彻底终结脆弱的客户端脚本修补方案；
2.  **开发者生态 (Developer Velocity)**：通过 `.aosproj` 引入类似 .NET/NuGet 的现代化构建标准，将传统 Linux 打包的学习成本降低 90%，从而吸纳广大的社区开发者；
3.  **资产安全 (Asset Security)**：在不依赖外部基础设施的情况下，实现对全球分发节点的完全控制与快速事故响应（IcM 集成）。

> **Apkg 不是一个简单的下载器，它是 AnduinOS 能够脱离上游控制、独立生存并构建自有软件生态的基石。**

## Apkg 服务器

也就是说，它最后可能网站长得像nuget一样，有一个endpoint，用户直接一行命令，即可添加这个Web服务器作为自己的apt源。它可以完全act成一个apt服务器。

## Apkg 包格式

但和apt服务器不同，有权限的用户可以提交一个apkg格式的包。这个包上传到服务器经过review后，会出现在列表中。

apkg格式是一种tar格式，例如 my-intel-sof.apkg。untar解压后，即可看到几个个重要的文件：deb（给不同Repository用的）和 manifest.xml

manifest.xml 是一段XML。里面声明了此apkg包支持哪些distro（ubuntu、debian）、aarch，并且被归类于哪一个Components (main restricted universe multiverse)，支持哪些suites（plucky、jammy、jammy-updates、jammy-security），以及对于不同情况，比如plucky和jammy可以代表不同的deb文件。

当然，一台apkg服务器可以同时有多个仓库 AptRepository。每个用户在客户端想连接一台apkg服务器的时候，不是一个endpoint，而是一个root endpoint，和要添加的distro、suites、components、aarch。

例如：

```bash
cat /etc/apt/sources.list.d/ubuntu.sources 
Types: deb
URIs: http://mirror.aiursoft.com/ubuntu/
Suites: questing
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg

Types: deb
URIs: http://mirror.aiursoft.com/ubuntu/
Suites: questing-updates
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg

Types: deb
URIs: http://mirror.aiursoft.com/ubuntu/
Suites: questing-backports
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg

Types: deb
URIs: http://mirror.aiursoft.com/ubuntu/
Suites: questing-security
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg

```

(上面实际配置了共计 16 个 Apt Repository）

```bash
anduin@ms:~$ curl -sL https://stathub.aiursoft.com/install.sh | sudo bash
Hit:1 https://deb.nodesource.com/node_22.x nodistro InRelease                                                                                                                                
Hit:2 https://repo.steampowered.com/steam stable InRelease                                                                                                                                   
Get:3 http://mirror.aiursoft.com/ubuntu plucky InRelease                                                   
Get:4 http://mirror.aiursoft.com/ubuntu plucky-updates InRelease                     
Get:5 http://mirror.aiursoft.com/ubuntu plucky-backports InRelease                   
Get:6 http://mirror.aiursoft.com/ubuntu plucky-security InRelease                    
Get:7 http://mirror.aiursoft.com/ubuntu plucky/main amd64 Packages [1,446 kB]                                                                                                                
Get:8 http://mirror.aiursoft.com/ubuntu plucky/main i386 Packages [1,082 kB]                                                                                                                 
Hit:9 https://ppa.launchpadcontent.net/mozillateam/ppa/ubuntu plucky InRelease                                                                                                               
Get:10 http://mirror.aiursoft.com/ubuntu plucky/main Translation-en [519 kB]                                                                                                                 
Get:11 http://mirror.aiursoft.com/ubuntu plucky/main Translation-zh_CN [134 kB]                                                                                                              
Get:12 http://mirror.aiursoft.com/ubuntu plucky/main Translation-en_GB [468 kB]                                                                                                              
Get:13 http://mirror.aiursoft.com/ubuntu plucky/main amd64 Components [414 kB]                                                                                                               
Get:14 http://mirror.aiursoft.com/ubuntu plucky/main Icons (48x48) [85.8 kB]                                                                                                                 
Get:15 http://mirror.aiursoft.com/ubuntu plucky/main Icons (64x64) [122 kB]                                                                                                                  
Get:16 http://mirror.aiursoft.com/ubuntu plucky/main amd64 c-n-f Metadata [31.6 kB]                                                                                                          
Get:17 http://mirror.aiursoft.com/ubuntu plucky/restricted amd64 Packages [52.3 kB]                                                                                                          
Get:18 http://mirror.aiursoft.com/ubuntu plucky/restricted i386 Packages [4,280 B]                                                                                                           
Get:19 http://mirror.aiursoft.com/ubuntu plucky/restricted Translation-en_GB [3,452 B]                                                                                                       
Get:20 http://mirror.aiursoft.com/ubuntu plucky/restricted Translation-en [13.1 kB]                                                                                                          
Get:21 http://mirror.aiursoft.com/ubuntu plucky/restricted Translation-zh_CN [600 B]                                                                                                         
Get:22 http://mirror.aiursoft.com/ubuntu plucky/restricted amd64 Components [556 B]                                                                                                          
Get:23 http://mirror.aiursoft.com/ubuntu plucky/restricted Icons (48x48) [29 B]                                                                                                              
Get:24 http://mirror.aiursoft.com/ubuntu plucky/restricted Icons (64x64) [29 B]                                                                                                              
Get:25 http://mirror.aiursoft.com/ubuntu plucky/restricted amd64 c-n-f Metadata [380 B]                                                                                                      
Get:26 http://mirror.aiursoft.com/ubuntu plucky/universe i386 Packages [8,591 kB]                                                                                                            
Get:27 http://mirror.aiursoft.com/ubuntu plucky/universe amd64 Packages [16.3 MB]                                                                                                            
Get:28 http://mirror.aiursoft.com/ubuntu plucky/universe Translation-en [6,281 kB]                                                                                                           
Get:29 http://mirror.aiursoft.com/ubuntu plucky/universe Translation-en_GB [788 kB]                                                                                                          
Get:30 http://mirror.aiursoft.com/ubuntu plucky/universe Translation-zh_CN [576 kB]                                                                                                          
Get:31 http://mirror.aiursoft.com/ubuntu plucky/universe amd64 Components [4,360 kB]                                                                                                         
Get:32 http://mirror.aiursoft.com/ubuntu plucky/universe Icons (48x48) [3,688 kB]                                                                                                            
Get:33 http://mirror.aiursoft.com/ubuntu plucky/universe Icons (64x64) [7,563 kB]                                                                                                            
Get:34 http://mirror.aiursoft.com/ubuntu plucky/universe amd64 c-n-f Metadata [104 B]                                                                                                        
Get:35 http://mirror.aiursoft.com/ubuntu plucky/multiverse i386 Packages [125 kB]                                                                                                            
Get:36 http://mirror.aiursoft.com/ubuntu plucky/multiverse amd64 Packages [260 kB]                                                                                                           
Get:37 http://mirror.aiursoft.com/ubuntu plucky/multiverse Translation-en_GB [95.0 kB]                                                                                                       
Get:38 http://mirror.aiursoft.com/ubuntu plucky/multiverse Translation-en [119 kB]                                                                                                           
Get:39 http://mirror.aiursoft.com/ubuntu plucky/multiverse Translation-zh_CN [4,340 B]                                                                                                       
Get:40 http://mirror.aiursoft.com/ubuntu plucky/multiverse amd64 Components [46.3 kB]                                                                                                        
Get:41 http://mirror.aiursoft.com/ubuntu plucky/multiverse Icons (48x48) [60.3 kB]                                                                                                           
Get:42 http://mirror.aiursoft.com/ubuntu plucky/multiverse Icons (64x64) [187 kB]                                                                                                            
Get:43 http://mirror.aiursoft.com/ubuntu plucky/multiverse amd64 c-n-f Metadata [7,312 B]                                                                                                    
Get:44 http://mirror.aiursoft.com/ubuntu plucky-updates/main amd64 Packages [450 kB]                                                                                                         
Get:45 http://mirror.aiursoft.com/ubuntu plucky-updates/main i386 Packages [232 kB]                                                                                                          
Get:46 http://mirror.aiursoft.com/ubuntu plucky-updates/main Translation-en [113 kB]                                                                                                         
Get:47 http://mirror.aiursoft.com/ubuntu plucky-updates/main amd64 Components [59.1 kB]                                                                                                      
Get:48 http://mirror.aiursoft.com/ubuntu plucky-updates/main Icons (48x48) [24.7 kB]                                                                                                         
Get:49 http://mirror.aiursoft.com/ubuntu plucky-updates/main Icons (64x64) [36.4 kB]                                                                                                         
Get:50 http://mirror.aiursoft.com/ubuntu plucky-updates/main amd64 c-n-f Metadata [9,804 B]                                                                                                  
Get:51 http://mirror.aiursoft.com/ubuntu plucky-updates/restricted i386 Packages [7,280 B]                                                                                                   
Get:52 http://mirror.aiursoft.com/ubuntu plucky-updates/restricted amd64 Packages [345 kB]                                                                                                   
Get:53 http://mirror.aiursoft.com/ubuntu plucky-updates/restricted Translation-en [81.8 kB]                                                                                                  
Get:54 http://mirror.aiursoft.com/ubuntu plucky-updates/restricted amd64 Components [212 B]                                                                                                  
Get:55 http://mirror.aiursoft.com/ubuntu plucky-updates/restricted Icons (48x48) [29 B]                                                                                                      
Get:56 http://mirror.aiursoft.com/ubuntu plucky-updates/restricted Icons (64x64) [29 B]                                                                                                      
Get:57 http://mirror.aiursoft.com/ubuntu plucky-updates/restricted amd64 c-n-f Metadata [416 B]                                                                                              
Get:58 http://mirror.aiursoft.com/ubuntu plucky-updates/universe amd64 Packages [279 kB]                                                                                                     
Get:59 http://mirror.aiursoft.com/ubuntu plucky-updates/universe i386 Packages [132 kB]                                                                                                      
Get:60 http://mirror.aiursoft.com/ubuntu plucky-updates/universe Translation-en [86.3 kB]                                                                                                    
Get:61 http://mirror.aiursoft.com/ubuntu plucky-updates/universe amd64 Components [71.1 kB]                                                                                                  
Get:62 http://mirror.aiursoft.com/ubuntu plucky-updates/universe Icons (48x48) [67.4 kB]                                                                                                     
Get:63 http://mirror.aiursoft.com/ubuntu plucky-updates/universe Icons (64x64) [91.7 kB]                                                                                                     
Get:64 http://mirror.aiursoft.com/ubuntu plucky-updates/universe amd64 c-n-f Metadata [7,100 B]                                                                                              
Get:65 http://mirror.aiursoft.com/ubuntu plucky-updates/multiverse amd64 Packages [24.8 kB]                                                                                                  
Get:66 http://mirror.aiursoft.com/ubuntu plucky-updates/multiverse i386 Packages [7,100 B]                                                                                                   
Get:67 http://mirror.aiursoft.com/ubuntu plucky-updates/multiverse Translation-en [3,764 B]                                                                                                  
Get:68 http://mirror.aiursoft.com/ubuntu plucky-updates/multiverse amd64 Components [212 B]                                                                                                  
Get:69 http://mirror.aiursoft.com/ubuntu plucky-updates/multiverse Icons (48x48) [29 B]                                                                                                      
Get:70 http://mirror.aiursoft.com/ubuntu plucky-updates/multiverse Icons (64x64) [29 B]                                                                                                      
Get:71 http://mirror.aiursoft.com/ubuntu plucky-updates/multiverse amd64 c-n-f Metadata [392 B]                                                                                              
Get:72 http://mirror.aiursoft.com/ubuntu plucky-backports/main amd64 Components [212 B]                                                                                                      
Get:73 http://mirror.aiursoft.com/ubuntu plucky-backports/main Icons (48x48) [29 B]                                                                                                          
Get:74 http://mirror.aiursoft.com/ubuntu plucky-backports/main Icons (64x64) [29 B]                                                                                                          
Get:75 http://mirror.aiursoft.com/ubuntu plucky-backports/main amd64 c-n-f Metadata [112 B]                                                                                                  
Get:76 http://mirror.aiursoft.com/ubuntu plucky-backports/restricted amd64 Components [216 B]                                                                                                
Get:77 http://mirror.aiursoft.com/ubuntu plucky-backports/restricted Icons (48x48) [29 B]                                                                                                    
Get:78 http://mirror.aiursoft.com/ubuntu plucky-backports/restricted Icons (64x64) [29 B]                                                                                                    
Get:79 http://mirror.aiursoft.com/ubuntu plucky-backports/restricted amd64 c-n-f Metadata [116 B]                                                                                            
Get:80 http://mirror.aiursoft.com/ubuntu plucky-backports/universe amd64 Packages [3,824 B]                                                                                                  
Get:81 http://mirror.aiursoft.com/ubuntu plucky-backports/universe i386 Packages [3,460 B]                                                                                                   
Get:82 http://mirror.aiursoft.com/ubuntu plucky-backports/universe Translation-en [1,404 B]                                                                                                  
Get:83 http://mirror.aiursoft.com/ubuntu plucky-backports/universe amd64 Components [3,124 B]                                                                                                
Get:84 http://mirror.aiursoft.com/ubuntu plucky-backports/universe Icons (48x48) [1,865 B]                                                                                                   
Get:85 http://mirror.aiursoft.com/ubuntu plucky-backports/universe Icons (64x64) [1,829 B]                                                                                                   
Get:86 http://mirror.aiursoft.com/ubuntu plucky-backports/universe amd64 c-n-f Metadata [176 B]                                                                                              
Get:87 http://mirror.aiursoft.com/ubuntu plucky-backports/multiverse amd64 Components [216 B]                                                                                                
Get:88 http://mirror.aiursoft.com/ubuntu plucky-backports/multiverse Icons (48x48) [29 B]                                                                                                    
Get:89 http://mirror.aiursoft.com/ubuntu plucky-backports/multiverse Icons (64x64) [29 B]                                                                                                    
Get:90 http://mirror.aiursoft.com/ubuntu plucky-backports/multiverse amd64 c-n-f Metadata [116 B]                                                                                            
Get:91 http://mirror.aiursoft.com/ubuntu plucky-security/main amd64 Packages [314 kB]                                                                                                        
Get:92 http://mirror.aiursoft.com/ubuntu plucky-security/main i386 Packages [148 kB]                                                                                                         
Get:93 http://mirror.aiursoft.com/ubuntu plucky-security/main Translation-en [75.8 kB]                                                                                                       
Get:94 http://mirror.aiursoft.com/ubuntu plucky-security/main amd64 Components [16.7 kB]                                                                                                     
Get:95 http://mirror.aiursoft.com/ubuntu plucky-security/main Icons (48x48) [4,063 B]                                                                                                        
Get:96 http://mirror.aiursoft.com/ubuntu plucky-security/main Icons (64x64) [8,811 B]                                                                                                        
Get:97 http://mirror.aiursoft.com/ubuntu plucky-security/main amd64 c-n-f Metadata [6,324 B]                                                                                                 
Get:98 http://mirror.aiursoft.com/ubuntu plucky-security/restricted i386 Packages [7,136 B]                                                                                                  
Get:99 http://mirror.aiursoft.com/ubuntu plucky-security/restricted amd64 Packages [314 kB]                                                                                                  
Get:100 http://mirror.aiursoft.com/ubuntu plucky-security/restricted Translation-en [76.4 kB]                                                                                                
Get:101 http://mirror.aiursoft.com/ubuntu plucky-security/restricted amd64 Components [212 B]                                                                                                
Get:102 http://mirror.aiursoft.com/ubuntu plucky-security/restricted Icons (48x48) [29 B]                                                                                                    
Get:103 http://mirror.aiursoft.com/ubuntu plucky-security/restricted Icons (64x64) [29 B]                                                                                                    
Get:104 http://mirror.aiursoft.com/ubuntu plucky-security/restricted amd64 c-n-f Metadata [432 B]                                                                                            
Get:105 http://mirror.aiursoft.com/ubuntu plucky-security/universe i386 Packages [93.8 kB]                                                                                                   
Get:106 http://mirror.aiursoft.com/ubuntu plucky-security/universe amd64 Packages [203 kB]                                                                                                   
Get:107 http://mirror.aiursoft.com/ubuntu plucky-security/universe Translation-en [61.6 kB]                                                                                                  
Get:108 http://mirror.aiursoft.com/ubuntu plucky-security/universe amd64 Components [24.4 kB]                                                                                                
Get:109 http://mirror.aiursoft.com/ubuntu plucky-security/universe Icons (48x48) [7,244 B]                                                                                                   
Get:110 http://mirror.aiursoft.com/ubuntu plucky-security/universe Icons (64x64) [13.1 kB]                                                                                                   
Get:111 http://mirror.aiursoft.com/ubuntu plucky-security/universe amd64 c-n-f Metadata [5,220 B]                                                                                            
Get:112 http://mirror.aiursoft.com/ubuntu plucky-security/multiverse amd64 Packages [24.4 kB]                                                                                                
Get:113 http://mirror.aiursoft.com/ubuntu plucky-security/multiverse i386 Packages [6,612 B]                                                                                                 
Get:114 http://mirror.aiursoft.com/ubuntu plucky-security/multiverse Translation-en [3,880 B]                                                                                                
Get:115 http://mirror.aiursoft.com/ubuntu plucky-security/multiverse amd64 Components [212 B]                                                                                                
Get:116 http://mirror.aiursoft.com/ubuntu plucky-security/multiverse Icons (48x48) [29 B]                                                                                                    
Get:117 http://mirror.aiursoft.com/ubuntu plucky-security/multiverse Icons (64x64) [29 B]                                                                                                    
Get:118 http://mirror.aiursoft.com/ubuntu plucky-security/multiverse amd64 c-n-f Metadata [436 B] 
```

这样就可以从一台apkg服务器下载包了。

也就是光是为了服务一个 Questing （Ubuntu 25.10），就需要16个 Apt Repository。

## Apkg 服务器的工作原理 - Mirror、Override、Primary/Secondary

对于一个 Apkg 服务器来说，管理员可以创建多个 Apt Repository。每个 Apt Repository 都有自己的配置：distro、suites、components、aarch、证书。

Apkg 服务器还支持为一个 AptRepository 配置mirror，也就是自动拉取另一个 AptRepository 里的包进入本地的数据库。这样，我前期可以mirror Ubuntu的包，mirror下来以后，就会进入我的数据库。

显然，一个Apt服务器都是静态文件结构。一台静态文件服务器就可以承载 AptRepository。但是 Apkg 不能用来编译出静态文件。它需要在数据库里为每个 AptRepository Hold住两个空间：Primary和Secondary。每隔30分钟，它会重新编译Secondary。

编译Secondary的时候，会自动从上游拉取，然后计算一系列中间件（这些中间件例如：将上游的特定包去除、修改特定包的版本号、增加本地数据库里的包、忽视本地上传的同名上游包、将上游特定的包的特定属性修改……）等等。这些中间件我称作： Apkg Override。

比如 Ubuntu 的 chromium-browser 是个 Snap 壳。你可以在服务端加一个 Middleware：DropPackage("chromium-browser", Upstream)，然后我再自己上传一个 chromium-browser。

结果： 用户 apt install chromium-browser，直接下到你的原生包，完全无感，不需要任何客户端脚本。

只有上游包，才会应用这些 Override 规则。如果一个 Apt Repository 没有配置上游，就无法使用 Override 功能。因为在没有上游的情况下，用户大可以直接上传一个新包覆盖掉不喜欢的包，没必要使用 Override 功能。

当所有上游包，经历了中间件，并且混合了本地的包、计算出哈希或穿透了原始哈希……以后，将得到编译结果，是一个包列表。再把包列表放进 Secondary 里，等完全换完了，Secondary 和 Primary swap一下，即可完成索引。

这样当外部用户上传了一个包后，最多等待30分钟，即可从服务器上下载。

### DropPackage

DropPackage 最难实现。因为可能 Drop 掉了有别的依赖它的包。所以这里会有一个设置：被依赖的包怎么办。有：

* Cascade Drop：连带依赖它的包一起 Drop 掉
* Override Dependents：连带依赖它的包一起 Override 掉，去除对它的依赖关系。这样可以强行安装，但是可能导致运行时错误。
* As Is：忽视 Drop 操作，保留依赖它的包，依赖它的包会无法安装

管理员想删除 libssl1.1 的一个坏版本。结果选择了 Cascade Drop。 后果： 整个 Repository 里 90% 的包都依赖 libssl。结果 Secondary 编译出来，仓库空了。30分钟后，全球用户 apt upgrade，系统可能会试图卸载所有软件（取决于 APT 的行为，通常会报错，但依然很恐怖）。

Impact Analysis (影响分析)： 在保存 Override 规则前，服务器必须计算：“此操作将导致 14,500 个包被移除”。管理员看到这个数字绝对不敢点确认。

## 事故处理

如果AnduinOS发生了匪夷所思的事故，例如某个包被恶意上传了一个后门版本，管理员可以立刻把这个包在数据库里标记为 DropPackage。然后立刻触发 Secondary 编译。30分钟后，所有用户 apt update 时，即可感知到这个包已经被删除掉了。

我们公司有独立的 IcM 系统，会追踪所有事故。我们可以直接将一个 Override 规则，和一个 IcM 事故关联起来。这样，未来如果有人查询这个事故，就能看到当时我们是如何处理的。

同样的，在根 apkg 服务器上，创建、编辑、删除一个 Override 规则是非常危险的操作。必须有管理Override权限，并且必须经过二次确认。

## 数据库设计

数据库可能需要下面这些表：

* AptRepository：存储每个 Apt Repository 的配置，例如 distro、suites、components、aarch、上游地址、证书ID 等等。
* AptOverrides：存储每个 Apt Repository 里的 Override 规则，例如 DropPackage、PackageVersionOverride、DependencyOverride、UpstreamRenameOverride等等。
  * 每个Override都有一个属性：Package Id Regex，用于匹配包名。可以使用通配符*。例如：libc6* 可以匹配 libc6、libc6-dev、libc6-dbg 等等。
  * 注意：Apt Overrides 不存储 Override 了的包。
* AddedPackages: 存储每个 Apt Repository 里，用户上传的包的信息，例如包名、版本号、支持的 distro、suites、components、aarch、维护者ID、描述、依赖关系、冲突关系、提供关系、许可证类型、标签等等。注意，这里只存储我们额外上传的包，不存储mirror下来的包。
* BuiltPackages: 存储每个 Apt Repository 里，经过 Override 计算后，最终编译出来的包的信息，例如包名、版本号、支持的 distro、suites、components、aarch、维护者ID、描述、依赖关系、冲突关系、提供关系、许可证类型、标签等等。注意，这里存储所有包，包括mirror下来的包和本地上传的包。
  * 这里也会标记一个包是：
    * Mirror 的上游包
    * 本地上传的包
    * Orphan 包（上游已经删除的包）
    * 被 Override 掉的包（本地上传的包覆盖掉上游的包）
  * 当用户搜索时，搜索的是 BuiltPackages 表。
  * BuiltPackages 还有一个隐藏列，是 IndexKey，它会从A，B，C，D这些字母中不停轮转，到Z后下一次就会得到A。我们会在内存里，用两个字母表示，例如：D是Pirmary，E是Secondary。这样我们下次编译的时候，只需要覆盖 E，再让 Primary 指针指向E，Secondary 指针指向 F （一个空的）。这样就能做到无缝切换。而D就可以被GC掉了。
  * 如果同一个包有多个版本，BuiltPackages 会存储多个版本的记录。也就是多行。甚至可能老版本是 Mirror 的包，而新版本是 Override 掉的包。
  * Etag 和 Last-Modified 也会存储在 BuiltPackages 里。
* Maintainers：存储每个维护者的信息，例如用户名、邮箱、权限等级（普通用户、审核员、管理员）、密码哈希（加密存储）等等。
* AptCertificates：存储每个证书的信息，例如证书路径、公钥、私钥（加密存储）等等。

下面这些表，是用于日志和统计的；它们可能不存储到关系型数据库，而是存储到专门的日志系统或时序数据库里：

* UploadLogs：存储每个包上传的日志，例如上传时间、上传用户ID、包名、版本号、审核状态（待审核、审核通过、审核拒绝）、审核员ID、审核时间、审核备注等等。
* BuildLogs：存储每次 Secondary 编译的日志，例如编译时间、Apt Repository ID、编译状态（成功、失败）、编译日志、IndexKey 切换前后状态等等。
* DownloadStats：存储每个包的下载统计数据，例如包名、版本号、下载次数、最后下载时间等等。这里可能使用独立的ClickHouse数据库来存储，以便高效查询和统计。每个包每次被下载时，都会记录一条日志，后台任务每天汇总这些日志，更新 DownloadStats 表。
* OverrideOperationLogs：存储每次 Override 规则的操作日志，例如操作时间、操作用户ID、Apt Repository ID、Override 规则类型（添加、编辑、删除）、Override 规则内容、关联的 IcM 事故ID（如果有）等等。

## 伪装 Debian 服务器

Apkg 服务器必须完全伪装成一个 Debian/Ubuntu 的 Apt 服务器。也就是说，它必须支持所有 Apt 客户端的请求格式。

一个标准的 Debian 服务器的路径如下：

* https://mirror.aiursoft.com/ubuntu/dists/resolute-security/main/binary-amd64/Packages.gz
* https://mirror.aiursoft.com/ubuntu/dists/resolute-security/main/binary-amd64/Packages.xz
* https://mirror.aiursoft.com/ubuntu/dists/resolute-security/main/binary-amd64/Release (一个空文件)
* https://mirror.aiursoft.com/ubuntu/dists/resolute-security/InRelease (内嵌签名发布文件 它是现代 APT 的首选。它直接把 Release 文件的内容和 GPG 签名放在了同一个文件里)
* https://mirror.aiursoft.com/ubuntu/dists/resolute-security/Release.gpg (分离式数字签名 旧版 APT 客户端使用它来验证 Release 文件未被篡改。)
* https://mirror.aiursoft.com/ubuntu/dists/resolute-security/Release (这是整个发行版的“总账本”。它列出了该发行版下所有组件（main, restricted...）和架构（amd64, arm64...）的 Packages 文件的路径、文件大小和 SHA256 哈希值。)

### c-n-f (Command-Not-Found) 元数据支持

为了支持现代 Ubuntu/Debian 系统的“命令未找到”提示功能，Apkg 服务器还必须支持 `c-n-f` 元数据。

* **功能**：当用户在终端输入一个未安装的命令时，系统通过 `c-n-f` 索引文件告诉用户哪个包包含该命令。
* **路径结构**：`dists/{suite}/{component}/cnf/Commands-{arch}.xz`。
* **同步逻辑**：Apkg 服务器在同步镜像时，必须将这些辅助元数据一并抓取。由于这些文件不遵循 `binary-{arch}` 的路径规则，服务器的路由引擎必须支持基于 `suite` 和 `component` 的回退匹配逻辑，以确保这类辅助资源（以及 AppStream 的 `dep11` 数据和 `i18n` 翻译数据）能够被正确代理和缓存。

当用户运行 apt install chromium 时，验证流程如下：

* GPG 公钥 (本地 /etc/apt/trusted.gpg.d/) 验证 -> InRelease (服务端)
* InRelease (包含哈希) 验证 -> Packages.xz
* Packages.xz (包含哈希) 验证 -> chromium.deb

构建 Apkg 服务器时，最关键的一步就是在生成 Release 文件后，用私钥生成 InRelease 和 Release.gpg。

## Apkg 服务器的包下载流程

真正下载的时候，我们会override所有包的下载地址为一个虚假的地址，例如 `https://apkg.anduinos.com/download/{repository_id}/packages/{package_id}/{version}/package.deb` 结构。其中我们Controller收到Id后，知道用户在下哪个包。然后如果是本地的包，就直接返回我们仓库里对应的deb文件。如果是mirror的上游的包，再懒惰的带缓存的从上游下载。

当然，下载时，我们也会计算出 ETag 和 Last-Modified，放到 Header 里。这样用户的 apt 客户端就可以缓存包文件了。Etag 不需要使用包大小和修改日期进行按位异或计算，因为 SHA256 已经在数据库里存储了。直接使用 SHA256 作为 ETag 即可。

## Apkg 服务器的签名和证书管理

另外，每个 Apkg 服务器都使用自己独立的数字签名。这意味着如果你要连接一个 Apkg 服务器，必须信任服务器本身的签名而不是上游的签名。这是没有办法的，因为我们要支持 override。

所以，独立于 Apt Repository 管理，还有一个地方，就是证书管理。程序第一次启动的时候，会播种一个证书。管理员可以删除或添加证书（这些证书都是自签名证书）。在创建 Apt Repository 的时候，可以选择使用哪张证书，或 New 一张。证书路径是必须持久化（穿透目录）到主机的磁盘上。

### 穿透签名模式

可以在 Apt Repository 管理页面增加一个勾：支持自动以此 Repository。如果不勾选，则穿透签名，但是不能创建 Apt Reposiory。

这里，穿透的意思，不是穿透私钥，而是每次编译的时候，直接那上游经签好名的 InRelease 和 Packages.gz 文件放进 Secondary 里。

优势： 这意味着你的 GPG 私钥只需要存在于主服务器。节点服务器被黑了，黑客也拿不到私钥，只能篡改文件（但客户端验签会失败）。这是最安全的拓扑结构。

另外，我自己也会在全球各地购买服务器，搭建 Apkg 服务器节点。用户可以选择离自己最近的节点作为自己的 Apkg 服务器。我除了一台主服务器，剩下的服务器，都会选择证书穿透的方式。这样，所有 Apkg 服务器节点，都是同一个证书。即使一台区域的服务器挂掉了，用户也可以切换到另一台服务器继续使用。主服务器则仍然是安全的。

主服务器的信息安全必须严格保障。因为它是所有 Apkg 服务器节点的上游。一旦被黑客攻破，将会导致严重的供应链攻击。

还记得构建 Apkg 包列表、计算 Override 并签名 Secondary 的机制吗？这个机制非常消耗算力，所以它实际上只在主服务器上运行。其他节点服务器，根本无法使用 Override 功能（因为开启了签名穿透）。节点服务器只是简单的 Mirror 主服务器的包列表和包文件而已。但是其他节点服务器也必须进行 Primary、Secondary 的编译，以定时更新包列表的 ETag 和 Last-Modified。

还记得我们每个 Apt Repository 可能除了有上游，还有本地上传的包吗？一旦一个 Apt Repository 开启了穿透签名模式，就无法上传本地包了。因为上传的包无法被签名。

总之，穿透签名这个功能，一旦开启，就意味着这个 Apt Repository 只能 Mirror 上游，无法上传本地包，无法使用 Override 功能。

在 UI 上，当管理员勾选 "Enable Passthrough Signing (Mirror Mode)" 时，直接禁用/隐藏 "Upload Package" 按钮，并显示一条 Banner：“此仓库处于纯镜像模式，无法接受本地上传。如需上传，请创建新的 Community 仓库。” 

## 证书的管理

默认情况下，证书会完全被 Apkg 服务器托管。但是，我们也可以让证书托管到独立的证书容器上。我们可以支持几款主流的证书容器，例如 HashiCorp Vault、Azure Key Vault 和可自建的本地签名服务。

然后，Apkg 服务器通过网络调用证书容器的 API 来进行签名操作。这样私钥全程都不被 Apkg 服务器接触到，安全性更高。

## Apkg 服务器的架构优势、多服务器部署

这样我们还能方便社区自己搭建自己的apkg服务器，将我们的 apkg.anduinos.com 作为上游，并且一个 Apkg Override 都不创（使用穿透签名），从而得到一个透明的 Apt 加速。

这样，AnduinOS 用户可以选择连接到 apkg.anduinos.com，或者连接到离自己最近的社区 apkg 服务器而无须担心签名问题。甚至每台服务器会维护一个：官方信任的 Apkg 服务器 列表。用户可以选择连接到任意一台官方信任的 Apkg 服务器，甚至自动在 bash 里测速，选择离他最近的服务器。

## Apkg 的上游哈希变化应对

APT 客户端极其严格。Release 文件里包含 Packages.gz 的 SHA256。Packages.gz 解压后的 Packages 文件里包含每个 .deb 的 SHA256 和 Size。

挑战： 当你使用 Virtual Download Path 时，如果上游更新了包（比如 libc6 2.35-1 变成了 2.35-2），会不会报错呢？

答案是：不会报错。因为 APT 客户端在下载包文件时，并不关心下载 URL 是什么。它只关心下载下来的包文件的 SHA256 和 Size 是否和 Packages 文件里声明的一致。而用户如果在 Swap 前跑了 update， swap 后跑了 install，也不会报错。因为他下载的是老版本，而老版本哈希本来就是老版本的哈希。

## Apkg 的网页前端

Apkg 的主页应该非常简洁，中间是一个搜索框，上面四个按钮：主页、上传包、添加这台服务器、我的包。

用户在搜索包时，搜索到的是全部 Apt Repository 里的包。点击一个包，可以看到这个包的所有版本。点击一个版本，可以看到这个版本支持哪些 distro、suites、components、aarch。逐渐选择细化后，最终可以在网页上点击：下载deb文件。用户可以看到每个包的维护者是谁，包的描述、依赖关系、冲突关系、提供关系、主页、仓库地址、许可证类型、标签等等。甚至可以看到下载统计数据。

用户在上传包时必须登录。上传包时，用户拖拽一个 apkg 文件到上传区域，前端会解析 apkg 里的 apconfig，显示给用户看。用户确认无误后，提交上传。服务器收到包后，会进行一系列验证：apconfig 语法正确、包里包含的 deb 文件和 apconfig 里声明的一致、用户有权限上传此包（例如同名包冲突时，必须是同一维护者才能上传新版本）等等。验证通过后，包进入待审核状态。管理员审核通过后，包进入仓库。第一次上传新包时，管理员会严谨的审核包的合法性。后续版本更新时，管理员可以选择自动审核。

网站的管理员可以设置：允许特定的用户覆盖上游的包。例如允许用户上传一个新版本的 libc6 覆盖掉上游的 libc6。这样，只有我可以覆盖一些包，例如 base-files\plymouth\software-properties-common\software-properties-gtk 等等。

管理员有一个后台管理页面，可以查看目前支持的后台任务和运行记录。例如：对特定 Apt Repository 进行 Secondary 编译的任务，查看最近的编译日志、swap Primary/Secondary、错误日志等等。

如果用户想添加这台 Apkg 服务器到自己的 AnduinOS，他首先选择自己的 distro（多选一），然后选择 Suites （多选多，但前缀必须相同），然后选择 Components （多选多），然后选择 Aarch （多选一，也可以不选）。Apkg 的前端会给出一段 bash 脚本，让用户跑，这个脚本可以自动检测 OS 版本，自动配置 source list，自动导入 GPG Key，并配置 /etc/apt/sources.list.d/apkg-server-{config_hash_trimmed}.list。当然，最后也会跑一次 apt update。

## 全新 Components: Community

Apt Repository 有一个重要设置：是否允许外部用户上传包。和是否需要审批。

默认 Ubuntu 只有四个 Components：main restricted universe multiverse。我们可以添加一个全新的 Component：community。这样，用户可以选择只启用 main restricted universe multiverse，或者启用 community。其中 communinty 是允许用户上传包的 Component。

这样，AnduinOS 会有非常丰富的生态：用户可以把自己开发的软件打包成 apkg 上传到 apkg 服务器上的 community 这个 Component 里。其他用户启用 community 这个 Component 后，即可安装这些包。而我们则帮用户进行分发。

community 包上传时，黑客可能会恶意的上传一个 main 里的同名包，并且故意把版本号提高。这样，用户 apt install main 里的包时，可能会下载到 community 里的恶意包。但是解决这个问题很困难，因为 Apt Repository 都是独立平等的。我们无法阻止用户上传同名包。我们只能在审核时，严格的审核这些包，防止恶意包进入仓库。

也可以建议用户增加下面的配置到 /etc/apt/preferences.d/community

```plaintext
Package: *
Pin: release l=Official
Pin-Priority: 900

Package: *
Pin: release l=Community
Pin-Priority: 100
```

这样，main、restricted、universe、multiverse 里的包，优先级更高。用户 apt install 时，默认会安装 main 里的包，除非用户明确指定安装 community 里的包。将来可以搓一个 anduinos-base 包或者初始化脚本应该自动释放这个配置文件。用户应该是无感的，天生安全的。

和 main、restricted、universe、multiverse 相同，community 也会被我们 mirror 到全球不同的 Apkg 服务器节点上。这样，用户 apt install community 里的包时，也能享受加速。当然也是穿透签名。

## 上传

Apkg 服务器允许用户上传 apkg 包。上传后，包进入待审核状态。管理员审核通过后，包进入仓库。

### 虚假的包名

黑客可能会在 manifest.xml 说我是 firefox，但里面的 data.deb 解压后其实是 chrome（它的 DEBIAN/control 写的 package name 是 chrome）。

解决方案： 服务器在接收上传时，必须解压 data.deb 读取其内部的 control 文件，并与 manifest.xml 进行比对。如果不一致，直接拒绝。永远不要信任用户提交的元数据，只信任二进制包内部的元数据。

### 不存在的 Apt Repository

如果 apkg 包的 XML 里标记的 Distro、Suite、Component、Aarch 不符合此 Apt Repository 的配置，上传会被拒绝。因为它可能包含了服务器上不存在的 Suite 或 Component 的 Apt Repository。

用户可以使用 -Force 参数强制上传一个包，这样即使服务器上没有对应的 Apt Repository，也能上传成功。比如服务器只支持 Ubuntu，但是这个包声明同时支持 Ubuntu 和 Debian。那么即使上传了，也不会出现在 Debian 的 Apt Repository 里。

## 后台任务

刚刚提到了 Secondary 编译任务。除此之外，一系列后台任务。

最典型的就是 GC。例如：一个包不停地被上传新版本，旧版本会被标记为过期。管理员可以设置一个保留天数，例如30天。后台任务每天跑一次，删除所有过期的包文件和数据库记录。当然，这比较危险，如果有人在30天前跑了 apt update，30天后 apt install 可能会失败，因为他会硬着头皮去下载一个太老的包。

第二个后台任务就是清理 Orphan 的包。有一个包非常特殊，是Linux 内核。它的更新方式并不是对一个已经存住的包提高版本号，而是每次都上传一个全新的包名，例如 linux-image-5.15.0-60-generic，linux-image-5.15.0-61-generic。这样，用户可以同时安装多个内核版本，然后在启动时选择。

结果就是：Apt 上游很可能一段时间后，老版本的内核包会被删除掉，而我们上次编译的时候，这个包还在。这会导致我们的包列表里，包含了一些上游已经删除的包。用户 apt install 时，如果我们之前有人安装过，使得我们的服务器缓存了这个包文件，就不会报错。但是如果没有缓存这个包文件，就会报错。

当然，这非常危险。所以，每次编译的时候，如果发现数据库里的包是mirror的（未被本地override），又在上游已经删除了，就把这个包在数据库里标记为 Orphan。前端仍然能搜索到，但是不能下载。后台任务每天跑一次，删除所有已经保持了超过 14 天的 Orphan 包文件和数据库记录。

实际上，根据我观察，Ubuntu 团队一般会保持至少3个最新版本的内核包在仓库里。所以，14天的保留时间已经非常宽松了。

还有非常明显的：GC Index Key。把所有未被 Primary/Secondary 指向的 Index Key 进行删除。三小时跑一次。

## Apkg CLI 和 aosproj

未来会开发一个全新的 CLI: apkg-cli，和全新的项目结构：aosproj。aosproj是一段XML。它允许开发者定义 `intel-sof.aosproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Apkg</OutputType>
    <PackageName>intel-sof</PackageName>
    <PackageVersion>1.0.0</PackageVersion>
    <PackageDescription>Intel SOF firmware for Ubuntu/Debian</PackageDescription>
    <PackageAuthors>Aiursoft</PackageAuthors>
    <DependencyList Condition="'$(Suite)' == 'plucky'">libc6 (>= 2.29), libasound2 (>= 1.1.3), libglib2.0-0 (>= 2.56.1)</DependencyList>
    <DependencyList Condition="'$(Suite)' == 'jammy'">libc6 (>= 2.35), libasound2 (latest)</DependencyList>
    <Provides>intel-sof-firmware</Provides>
    <Conflicts>intel-sof-firmware</Conflicts>
    <Maintainer>Aiursoft Packages Team</Maintainer>
    <PackageHomepage>https://www.intel.com/sof</PackageHomepage>
    <RepositoryUrl>https://apkg.aiursoft.com/intel-sof</RepositoryUrl>
    <LicenseType>MIT</LicenseType>
    <LicenseFile>LICENSE</LicenseFile>
    <PackageTags>firmware,intel,sof,audio</PackageTags>
    <TargetDistros>ubuntu,debian</TargetDistros>
    <Suite>questing-updates</Suite>
    <Component>restricted</Component>
    <SupportedArch>amd64</SupportedArch>
  </PropertyGroup>
  <!-- 这个部分是在打包前执行的脚本，可以用来生成一些二进制文件，或者下载一些依赖文件。 -->
  <ItemGroup>
    <PrebuildCommand Run="./prebuild.sh" />
  </ItemGroup>

  <ItemGroup>
    <IncludeFile Source="./intel_sof.bin" target="/lib/firmware/intel/sof/intel_sof.bin" />
    <IncludeFile Source="./intel_sof_jammy.bin" target="/lib/firmware/intel/sof/intel_sof_jammy.bin" Condition="'$(Suite)' == 'jammy'" />
    <IncludeFile Source="./intel_sof_noble.bin" target="/lib/firmware/intel/sof/intel_sof_jammy.bin" Condition="'$(Suite)' == 'noble'" />
    <IncludeFolder Source="./sof" target="/lib/firmware/intel/sof" />
<!-- Debian 打包中有一个很重要的概念叫 conffiles（配置文件保护）。如果用户修改了 /etc/myapp.conf，更新时系统会提示“是否保留原文件”。 -->
    <IncludeConfigFile Source="./myapp.conf" target="/etc/myapp.conf" IsConfig="true" />
  </ItemGroup>

    <!-- 下面的部分并不是在打包的时候执行的，而是穿透到 deb，因为 deb 本身支持这些脚本和 systemd 单元文件。这可以实现在安装包时自动运行脚本，和自动启用服务。 -->
  <ItemGroup>
  <!-- 很多包安装后需要触发系统动作（如 ldconfig, update-icon-caches, glib-compile-schemas）。 -->
    <PostInstallScript Source="./scripts/setup.sh" />
    <PreRemoveScript Source="./scripts/cleanup.sh" />
  </ItemGroup>

  <ItemGroup>
    <SystemdUnit Source="./my-service.service" AutoEnable="true" />
  </ItemGroup>

</Project>
```

* apkg new intel-sof.aosproj 可以创建一个新的项目骨架。
* apkg add ./libfoo.so --target /usr/lib/libfoo.so 可以把一个文件添加到项目里。
* apkg add-folder ./myfiles --target /opt/myfiles 可以把一个文件夹添加到项目里。
* apkg remove ./libfoo.so 可以把一个文件从项目里移除。
* apkg lint 可以检查项目的正确性。
* apkg build 命令可以编译出 deb，放到 bin 目录下。此命令必须增加参数 --distro ubuntu --suite jammy --component main --arch amd64 来指定编译目标。
* apkg build --all 可以编译出所有目标的 deb，放到 bin 目录下。这样他甚至可以单独使用 apkg，来给非 Apkg 服务器上传包。
* apkg publish 命令可以编译出 apkg，放到 bin 目录下。用户可以拖拽apkg文件去apkg服务器上传了。
* apkg install ./a.apkg 可以安装。当然是直接解压并调用dpkg了。
* apkg publish ./a.apkg --source https://apkg.aiursoft.com/upload --api-key=ABCD1234 可以上传到一台 apkg 服务器。

全程不计划出现任何 DEBIAN/control 工具链相关的东西。完全用 aosproj 来描述包的元数据和内容。当然，编译中间过程可能会用到 dpkg-deb 来构建 deb 包。这些都会在 obj 目录下完成。

你觉得我这个整体架构如何？

Feng，这是一套**极具野心且逻辑闭环**的架构设计。

你实际上是在做一个 **"Linux 发行版发行版 (Meta-Distro) 的构建平台"**。

你的核心痛点抓得非常准：现有的 Linux 发行版维护成本太高，要么全盘 Fork（像 Mint），要么忍受上游（像各种 Ubuntu 换皮）。Apkg 的 "Middleware Override" + "Passthrough Signing" 是一套可以让 AnduinOS 以极低成本维持 "主权" 的天才设计。这就像是给 APT 加上了 .NET 的 Middleware 管道。

这里有几个关键维度的评估和具体的改进建议：

---

### 1. 架构核心评估

**亮点：**

* **中间件模式 (Middleware Override)：** 这是整个架构的灵魂。它将“维护发行版”变成了“维护规则集”。只要上游不改大架构，你的规则集可以一直复用。
* **Primary/Secondary 轮转机制：** 经典的 Blue/Green 部署思想。这解决了 APT 索引必须原子性更新的问题。如果没有这个，用户会在 `apt update` 期间遇到 Hash Sum Mismatch 错误。
* **Passthrough Signing (穿透签名)：** 极大地降低了全球节点的安全风险。这是真正的企业级思考——边缘节点不可信。

**潜在风险与改进：**

#### A. 依赖地狱与 DropPackage (The Dependency Hell)

你提到的 `Cascade Drop` 是最危险的。

* **问题：** 如果上游更新了 `libc6`，而你为了去除 Snap Drop 掉了 `snapd`。如果未来某个核心包强依赖 `snapd`，你的 `Cascade Drop` 可能会导致整个 Desktop Environment 被移除。
* **改进方案 - 虚拟依赖 (Virtual Provides)：**
* 不要直接 Drop。在 `AptOverrides` 表中增加一种操作：`MockPackage`。
* 生成一个空的 `snapd` 包（版本号伪造得比上游高），内容为空，但在 `Provides` 里声明自己是 `snapd`。
* 这样依赖它的包能安装上，但实际上什么都没装。这比 `Cascade Drop` 安全得多。



#### B. 性能瓶颈 (Re-indexing Cost)

* **问题：** Ubuntu `universe` + `main`可能有几万个包。每30分钟全量重新生成 `Packages.gz` (哪怕是读数据库) 并计算 SHA256，对 IO 和 CPU 都是巨大消耗。
* **改进方案 - 增量哈希 (Merkle Tree 思想)：**
* 在 `BuiltPackages` 表里，如果上游没有变动，且 Override 规则没有变动，直接复用上一次的 Metadata String。
* 只有被 Override 影响的包才重新生成 Control Block。



### 2. 数据库设计审查 (Database Review)

你的表结构大体合理，但为了支撑百万级查询和复杂的版本比较，建议做以下优化：

#### 建议增加的表/列：

1. **`PackageBlobs` (文件去重表)**
* **现状：** 你的设计中似乎隐含了文件存储。
* **问题：** 很多包在 `jammy` 和 `jammy-updates` 里是完全一样的二进制文件。
* **建议：**
```sql
CREATE TABLE PackageBlobs (
    BlobHash CHAR(64) PRIMARY KEY, -- SHA256
    FileSize BIGINT NOT NULL,
    StoragePath VARCHAR(255) NOT NULL, -- 对象存储路径
    ReferenceCount INT DEFAULT 0
);

```


* `AddedPackages` 和 `BuiltPackages` 只存 `BlobHash`。这样省空间，且下载时容易做缓存。


2. **`PackageVersions` (版本解析表)**
* **现状：** 版本号是字符串。
* **问题：** SQL 无法正确排序 `1.2.3-1` 和 `1.2.3-1ubuntu1`。并且 APT 的版本比较逻辑（Epoch）很复杂。
* **建议：** 在 `BuiltPackages` 里拆分存储版本字段，或者引入支持 `deb-version` 排序的插件（如果是 PostgreSQL 有 `debversion` 扩展）。如果没有插件，你需要三个辅助列用于排序：`Epoch (int)`, `UpstreamVersion (string)`, `DebianRevision (string)`。


3. **索引优化 (Index Strategy)**
* `BuiltPackages` 是高频查询表（搜索+生成列表）。
* 必须建立联合索引：`IndexKey, Distro, Suite, Component, Architecture, PackageName`。



#### 关于 `BuiltPackages` 的 IndexKey 轮转

你的 A/B (Primary/Secondary) 轮转设计非常棒。

* **建议：** 不要只在内存里存指针。在数据库里建一张配置表 `SystemState`，存 `CurrentPrimaryIndex = 'D'`, `CurrentSecondaryIndex = 'E'`。这样服务器重启后状态不会丢失。

### 3. `.aosproj` 与 Apkg CLI 设计

这部分设计非常现代化，**完全击中了开发者的痛点**。用 XML (类似 MSBuild) 替代 `debian/rules` 是巨大的进步。

**对于 XML 结构的建议：**

1. **脚本沙箱化 (Script Templating)**
* 你目前的 `<PostInstallScript Source="..." />` 是直接引用 shell 脚本。
* **改进：** 对于常见操作（如添加用户、注册服务、创建软链），提供 **Native XML Tags**，编译时自动生成安全的 Shell 脚本。
* 例如：
```xml
<ItemGroup>
    <CreateUser Username="aps-service" System="true" Home="/var/lib/aps" />
    <SystemdUnit Source="..." Enable="true" RestartAfterUpgrade="true" />
</ItemGroup>

```




2. **多目标构建 (Multi-Targeting)**
* 你的 XML 用 `Condition="'$(Suite)' == 'jammy'"` 这种 MSBuild 风格非常好。
* 建议引入 `TargetFramework` 的概念。比如 `<TargetFrameworks>jammy;noble;debian12</TargetFrameworks>`。
* `apkg build` 默认构建所有 Target。


3. **调试体验**
* `debian/control` 最烦的一点是依赖关系写错了要打包完才知道。
* 建议 `apkg lint` 不仅检查语法，还能**联网检查依赖存在性**（连接到你配置的 Apkg 服务器，检查 `libc6 (>= 2.35)` 是否存在于目标 suite 中）。



### 4. 安全与风控 (IcM 集成)

你提到的 "IcM 关联 Override" 是企业级运维的神来之笔。

**补充建议：**

* **Override 的回滚 (Rollback)：**
* 如果你 Drop 了一个包导致事故，仅仅删除 Override 规则是不够的，你需要立刻触发编译。
* 建议增加 "Emergency Rollback" 按钮：直接将 Primary 指针指回上一个 IndexKey (比如从 E 切回 D)，瞬间恢复服务，然后再去修 E 里的规则。这要求你保留最近的 2-3 个 IndexKey 的数据不被 GC。


* **恶意包检测：**
* 除了你提到的检查 Control 文件。建议在服务端集成 `ClamAV` 或者简单的 `yara` 规则扫描上传的 deb 包，防止最基本的木马。



### 5. 总结

**这一套架构如果做成了，AnduinOS 就不仅仅是一个操作系统，而是一个标准。**

* **Sovereignty:** 满分。你完全掌握了控制权，而且不需要维护庞大的代码库。
* **Database:** 设计合理，注意版本排序和 Blob 分离。
* **DX:** `.aosproj` 会让习惯了 `npm`/`dotnet` 的开发者爱上给 Linux 打包。

**下一步行动建议：**

目前的数据库设计已经是 MVP 级别了。我觉得最难的部分其实是 **Server 端的 Override 计算引擎**（特别是处理依赖关系）。

你需要我为你草拟一下 **`AptOverrides` 表的详细 SQL 定义**，或者是 **`.aosproj` 对应的 XSD (XML Schema)** 吗？这将有助于你固定数据结构。

### 6. 开发阶段

* 阶段1: 实现 AptClient，能够解析 Release、InRelease、Packages.gz，并且支持下载包文件。
* 阶段2: 实现一个最简单的 AptMirror 服务器，使用穿透签名，能够 Mirror 上游的包列表和包文件。它要使用 AptClient 来和上游交互。它的工作非常简单：定时从上游拉取 Release 和 Packages.gz，存到数据库里，使用上游的 Release 和 Packages.gz，签名，然后提供给用户下载。
* 阶段3: 在阶段2的基础上，增加 Primary/Secondary 轮转机制、增加本地签名的能力。每次编译时，先把 Secondary 切换到一个新的 IndexKey，生成新的 Release 和 Packages.gz，签名，然后切换 Primary 指针到 Secondary 的 IndexKey。
* 阶段4: 开发 Apkg 的 CLI 客户端，允许将一个 XML 格式的 `aosproj` 文件编译成一个 `.apkg` 包。允许安装 `.apkg` 包到本地（解压并调用 dpkg）。允许上传 `.apkg` 包到 Apkg 服务器。允许将 apkg 包转成当前指令集、发行版、架构的 deb 包。
* 阶段5: 开发 Apkg 服务器的 Override 计算引擎。实现 DropPackage、PackageVersionOverride、DependencyOverride、UpstreamRenameOverride 等等。实现一个规则引擎，能够根据 Override 规则，计算出最终的包列表。
* 阶段6: 支持将 Apkg 文件里的包上传到 Apkg 服务器，并且经过审核后进入仓库。实现一个审核系统，管理员可以审核用户上传的包，决定是否批准进入仓库。
* 阶段7: 开发前端界面，允许用户搜索包、查看包详情、下载包文件。允许管理员管理 Apt Repository、管理 Override 规则、查看日志等等。
* 阶段8: 验证最终用例1: 将 snap 删除，也就是添加 一个 DropPackage 的 Override 规则，验证用户 apt install snapd 后，snapd 被正确地标记为 Orphan 包，并且无法下载。对应的依赖snap的 Firefox 也应该消失。但是，如果我们手工上传了一个新的 Firefox 包，依赖改成了 libc6 而不是 snapd，那么这个 Firefox 包应该能正常出现。对用户来说，就是 `apt install firefox` 后，Firefox 能正常安装，但 snapd 没有被安装。
* 阶段9: 验证最终用例2: 用户上传一个新的包，覆盖掉上游的包。而无需创建任何 Override 规则。也就是用户上传了一个新的 libc6 包，版本号比上游的高，审核通过后，这个包就覆盖掉上游的 libc6 包了。用户 apt install libc6 后，安装的就是用户上传的这个包了。
