# Multisim “主数据库无法访问”一键修复工具 (Multisim-Database-Fix)

专门解决 NI Multisim 14.x（`14.0` / `14.1` / `14.2` / `14.3`）在 Windows 10 / Windows 11（包括 **Win11 24H2 / KB5065426 更新后**）启动时弹出：

> **访问数据库时发生错误。**
> **主数据库 无法访问。使用 主数据库 的功能将不可用。**
> *(There was an error accessing the Database. The Master Database cannot be accessed. Features using the Master Database will not be available.)*

导致元件库一片空白、无法放置元器件，以及**“卸载重装无效”、“关机重启后又复发”**的问题。

---

## 为什么卸载重装无效？为什么关机重启后又会报错？

导致该报错共有两层原因：

1. **配置与权限残留（导致卸载重装无效）**：
   Windows 卸载程序只会清理 `Program Files (x86)`，**不会删除** `%AppData%\National Instruments\Circuit Design Suite\<版本>\config` 中损坏的配置文件，也不会清除 `C:\ProgramData\National Instruments` 下的 `.ldb` 死锁文件和只读权限限制。
2. **Windows 11 中文系统双字节编码（GBK `936`）与 `msjet40.dll` 底层冲突（导致关机重启后复发）**：
   Multisim 主数据库依赖 Windows 系统自带的 `msjet40.dll`（Microsoft Jet 4.0 数据库引擎）读取。在 Windows 11 近期更新（如 `KB5065426` / `24H2`）后，`msjet40.dll` 在简体中文系统默认代码页（`ACP = 936`）下校验数据库密码时会出现双字节解析错误，从而拒绝加载主数据库。

---

## 使用方法（安全无副作用，支持一键还原）

1. 下载本仓库（点击右上角绿色的 **`<> Code`** -> **`Download ZIP`** 并解压到任意文件夹）。
2. 双击运行 **[`Fix_Multisim_Database.bat`](./Fix_Multisim_Database.bat)**，在弹出的管理员权限窗口（UAC）中点击 **“是”**。
3. **两种修复模式（按需选择）**：
   * **标准模式（默认，对其他软件 100% 零影响）**：
     脚本自动完成 **Step 1 ~ Step 5**（清理残留配置、解锁权限、为 `multisim.exe` 注入单进程专属 `en-US` 清单）。在 **Step 6** 询问 `[y/N]` 时直接**按回车（默认 `N`）**，无需重启电脑，直接打开 Multisim 测试！
   * **深度根治模式（针对部分顽固 Win11 24H2 系统）**：
     如果标准模式后仍报错，再次运行脚本并在 **Step 6** 输入 **`Y`** 开启系统级 UTF-8 代码页（`ACP = 65001`），随后重启一次电脑即可永久根治。
   * **随时一键还原**：
     开启全局 UTF-8 对微信、Office、浏览器、Steam 等 99% 现代软件无任何影响；若发现极少数十年前的老旧非 Unicode 中文小工具（如旧版单片机串口助手）出现乱码，只需再次运行本脚本并输入 **`R`（Restore）**，即可 1 秒恢复系统默认的 `936` 中文编码！

---

## 本工具自动执行的 6 步修复流程

| 步骤 | 自动修复内容 | 对系统其他软件的影响 |
| :--- | :--- | :--- |
| **Step 1** | 自动检测并关闭后台未退出的 `multisim.exe` / `ultiboard.exe` / `SimServer.exe` 进程，释放文件占用锁。 | 无影响（0 副作用） |
| **Step 2** | 自动定位 `%AppData%\National Instruments\Circuit Design Suite` 下所有版本的残留 `config` 文件夹，将其备份为 `config_bak_<时间戳>`，强制重建默认配置。 | 无影响（0 副作用） |
| **Step 3** | 自动扫描 `C:\ProgramData`、用户文档以及所有本地硬盘（`C:` ~ `Z:`）下的 Multisim 数据库目录，清除所有 `.ldb` 死锁文件与只读属性，并赋予完全控制权限。 | 无影响（0 副作用） |
| **Step 4** | 自动修复注册表 `HKLM\SOFTWARE\WOW6432Node\Microsoft\Jet\4.0\Engines` 及 `SysWOW64\msjet40.dll` 等驱动文件的访问控制权限。 | 无影响（0 副作用） |
| **Step 5** | 自动提取 `multisim.exe` 原始清单并注入单进程 `<activeCodePage>en-US</activeCodePage>` 外部清单（`multisim.exe.manifest`），隔离冲突 DLL，并将主程序设为默认管理员启动。 | 无影响（仅作用于 Multisim 自身） |
| **Step 6** | **（可选，默认跳过 `N`）** 支持输入 `Y` 一键开启系统级 UTF-8 代码页（`65001`），或输入 `R` 一键还原系统默认中文 GBK 代码页（`936`）。 | 可选且支持随时一键还原 |

---

## 文件列表

```text
Multisim-Database-Fix/
├── Fix_Multisim_Database.bat   # 双击运行的一键修复入口（纯 ASCII 批处理，无乱码）
├── FixDatabase.cs              # 核心修复源码（调用 Windows 内置 .NET Framework 即时编译）
├── LICENSE                     # MIT 开源协议
└── README.md                   # 说明文档
```
