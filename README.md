# Multisim “主数据库无法访问”一键修复工具 (Multisim-Database-Fix)

专门解决 NI Multisim 14.x（`14.0` / `14.1` / `14.2` / `14.3`）在 Windows 10 / Windows 11 系统下启动时弹出：

> **访问数据库时发生错误。**
> **主数据库 无法访问。使用 主数据库 的功能将不可用。**
> *(There was an error accessing the Database. The Master Database cannot be accessed. Features using the Master Database will not be available.)*

导致元件库一片空白、无法放置元器件的问题。

---

## 为什么卸载重装 Multisim 后还是报这个错？

很多同学遇到这个报错后尝试把 Multisim 卸载重装，甚至重装好几次后发现**依然报同一个错**。这是因为 Windows 卸载程序只会删除安装目录（`Program Files (x86)`），而**绝对不会删除**以下隐藏目录中的残留数据：
1. `%AppData%\National Instruments\Circuit Design Suite\<版本>\config` 中已经损坏的用户配置文件与错误路径缓存；
2. `C:\ProgramData\National Instruments\Circuit Design Suite\<版本>\database` 下未释放的 `.ldb` 数据库死锁文件、只读属性（Read-Only）以及受限的文件夹安全权限（ACL）；
3. 64 位系统下 `Microsoft Jet 4.0` 数据库引擎注册表项权限异常或第三方冲突 DLL。

因此，只要不清理这些隐藏残留并修复权限，重装多少次都会继续报错。

---

## 使用方法（傻瓜式一键修复）

1. 下载本仓库（点击右上角 `Code` -> `Download ZIP` 并解压到任意位置）。
2. 双击运行 **[`Fix_Multisim_Database.bat`](./Fix_Multisim_Database.bat)**。
3. 在弹出的管理员权限窗口（UAC）中点击 **“是”**。
4. 看到 `[SUCCESS] Master Database repair completed!` 提示后按回车关闭窗口，重新打开 Multisim 即可恢复正常！

---

## 本工具自动完成了哪些操作？

| 步骤 | 自动修复内容 |
| :--- | :--- |
| **Step 1** | 自动检测并关闭后台未退出的 `multisim.exe` / `ultiboard.exe` / `SimServer.exe` 进程，释放文件占用锁。 |
| **Step 2** | 自动定位 `%AppData%\National Instruments\Circuit Design Suite` 下所有版本的残留 `config` 文件夹，将其备份重命名为 `config_bak_<时间戳>`，强制 Multisim 在下次启动时生成全新的默认配置。 |
| **Step 3** | 自动扫描 `C:\ProgramData`、用户文档以及所有本地硬盘（`C:` ~ `Z:`）下的 Multisim 数据库目录，清除所有 `.ldb` 死锁文件与只读（Read-Only）属性，并通过 `icacls` 赋予完全控制权限。 |
| **Step 4** | 自动修复 Windows 注册表中 `HKLM\SOFTWARE\WOW6432Node\Microsoft\Jet\4.0\Engines` 的访问控制权限。 |
| **Step 5** | 自动排查并隔离安装目录下可能引起数据库引擎崩溃的冲突动态库（`libeay32.dll` / `libiconv-2.dll` / `libpq.dll`），并将 `multisim.exe` 配置为默认以管理员身份启动（`RUNASADMIN`）。 |

---

## 补充说明（针对 Windows 11 最新系统更新用户）

如果运行本工具后，在部分中文版 Windows 11 系统上仍然报错，通常是遇到了微软近期推送的 Windows 11 累积更新（如 `KB5065426`）与 Multisim `14.3.0` 早期版本的底层区域兼容性问题。可通过以下任一方式解决：
* **方式 A（推荐）**：打开电脑开始菜单中的 **NI Package Manager** -> 点击 **更新 (Updates)**，将 `NI Circuit Design Suite` 升级至官方修复版 **`14.3.3`**。
* **方式 B（应急）**：弹出“主数据库无法访问”小窗时**不要点确定关闭它**，直接回桌面再次双击 Multisim 图标多开第 2~3 个窗口，后续窗口即可绕过锁定正常加载元件库。

---

## 文件列表

```text
Multisim-Database-Fix/
├── Fix_Multisim_Database.bat   # 双击运行的一键修复入口（纯 ASCII 批处理，无乱码）
├── FixDatabase.cs              # 核心修复源码（使用 Windows 内置 .NET Framework 即时编译）
├── LICENSE                     # MIT 开源协议
└── README.md                   # 说明文档
```
