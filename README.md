# Multisim 14.x 安装与主数据库报错一键修复工具 (Multisim-Fix-Tool)

针对 NI Multisim 14.x（`14.0` / `14.1` / `14.2` / `14.3`）在 Windows 10 / Windows 11 系统下最常见的三大顽疾，提供**零第三方依赖、全自动提权、跨盘符通用**的一键修复脚本。

---

## 解决哪些问题？

### 1. 安装时报错 `NIPMCommError` / `Packages.gz 无效`
* **常见报错提示**：
  > `更新所有源失败。详细信息请访问ni.com/info，输入信息代码NIPMCommError进行查询。`
  > `源URI '...\setup\bin\./ni-package-manager-packages/Packages.gz'无效。`
  > `无法找到包含兼容版本和架构的程序包'ni-package-manager'。`
* **原因**：部分解压软件在解压安装包时开启了“自动解压嵌套压缩包”，将安装源目录中的 `Packages.gz` 解压成了未压缩的 `Packages` 文件并删除了原 `.gz` 归档，导致 NI Package Manager 无法读取组件源。
* **对应脚本**：双击运行 **`1_Fix_Installer_PackagesGz.bat`**（支持放在安装包目录下自动识别，或按提示将 `setup` 文件夹拖入窗口），1 秒重新生成所有缺失的 `Packages.gz` 并自动启动安装程序。

---

### 2. 打开软件时报错 `访问数据库时发生错误。主数据库 无法访问`
* **常见报错提示**：
  > `访问数据库时发生错误。`
  > `主数据库 无法访问。使用 主数据库 的功能将不可用。`
  > *(The Master Database cannot be accessed. Features using the Master Database will not be available.)*
* **为什么卸载重装也没用？**
  卸载 Multisim 时，Windows **不会清除** `%AppData%\National Instruments\Circuit Design Suite` 下已损坏的用户配置文件（`config`）和 `C:\ProgramData\National Instruments` 下的数据库文件锁（`.ldb`）及只读权限。重装后依然会读取旧的损坏配置。
* **对应脚本**：双击运行 **`2_Fix_Master_Database.bat`**（自动弹出管理员权限确认）：
  1. 自动备份并重置 `%AppData%` 下残留损坏的 `config` 文件夹；
  2. 自动扫描全盘（`C:` / `D:` / `E:` 等所有本地硬盘），清除数据库目录下的 `.ldb` 死锁文件与“只读”属性；
  3. 自动为 `ProgramData` 与安装目录中的 `database` 赋予 `Users` 完全控制权限；
  4. 自动排查并隔离冲突的第三方 DLL（`libeay32.dll` / `libiconv-2.dll` / `libpq.dll`），并将 `multisim.exe` 配置为默认以管理员身份启动。
* **补充说明（Windows 11 用户）**：如果执行完上述修复后仍偶发该提示，通常是 Windows 11 近期累积更新（如 `KB5065426`）与 `14.3.0` 原版的兼容性问题，打开电脑上的 **NI Package Manager** 将 `Circuit Design Suite` 更新至官方修复版 **`14.3.3`** 即可彻底根治。

---

### 3. 一键自动寻路安装简体中文语言包
* **对应脚本**：双击运行 **`3_Install_Chinese_Pack.bat`**，自动查找 `Chinese-simplified` 语言包并复制到电脑上已安装的 `Circuit Design Suite 14.x\stringfiles` 目录中，无需手动翻找 `Program Files (x86)`。

---

## 目录结构

```text
Multisim-Fix-Tool/
├── 1_Fix_Installer_PackagesGz.bat   # 一键修复安装报 Packages.gz 无效
├── 2_Fix_Master_Database.bat        # 一键修复“主数据库无法访问”（即使重装无效也能修）
├── 3_Install_Chinese_Pack.bat       # 一键自动寻路安装简体中文包
├── src/
│   ├── FixInstaller.cs              # Packages.gz 重建核心源码 (C#)
│   ├── FixDatabase.cs               # 数据库配置重置与权限修复核心源码 (C#)
│   └── InstallChinese.cs            # 汉化包自动部署核心源码 (C#)
├── LICENSE
└── README.md
```

## 技术特点
* **零外部依赖**：直接调用 Windows 10 / 11 系统自带的 `.NET Framework 4.0` 编译器（`csc.exe`）即时编译运行，无需安装 Python、7-Zip 等任何第三方环境。
* **规避 `cmd` 中文乱码坑**：所有 `.bat` 入口均为纯 ASCII 编码，杜绝 Windows 命令行 `chcp 65001` 引发的换行截断与闪退。
* **自动 UAC 提权**：直接双击即可自动请求管理员权限，不会因为忘记右键“以管理员身份运行”而修复失败。
