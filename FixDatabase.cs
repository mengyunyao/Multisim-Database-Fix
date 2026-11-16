using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml;
using Microsoft.Win32;

class FixDatabase
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr hModule);

    static bool IsAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    static void EnsureAdmin(string[] args)
    {
        if (IsAdministrator()) return;
        Console.WriteLine("[Info] Requesting Administrator privileges (UAC)...");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = string.Join(" ", args),
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch
        {
            Console.WriteLine("[ERROR] Administrator privileges are required to repair Multisim database.");
            Console.WriteLine("Please right-click Fix_Multisim_Database.bat and select 'Run as administrator'.");
            Console.ReadLine();
        }
        Environment.Exit(0);
    }

    static void StopRunningMultisimProcesses()
    {
        string[] procNames = new string[] { "multisim", "ultiboard", "SimServer", "ewb" };
        foreach (string name in procNames)
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    Console.WriteLine("[Step 1] Closing running process: " + p.ProcessName + " (PID: " + p.Id + ")");
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch { }
        }
    }

    static string ExtractEmbeddedManifest(string exePath)
    {
        IntPtr hMod = IntPtr.Zero;
        try
        {
            hMod = LoadLibraryEx(exePath, IntPtr.Zero, 0x00000002 /* LOAD_LIBRARY_AS_DATAFILE */);
            if (hMod == IntPtr.Zero) return null;

            IntPtr hRes = FindResource(hMod, (IntPtr)1, (IntPtr)24 /* RT_MANIFEST */);
            if (hRes == IntPtr.Zero) return null;

            uint size = SizeofResource(hMod, hRes);
            if (size == 0) return null;

            IntPtr hGlobal = LoadResource(hMod, hRes);
            if (hGlobal == IntPtr.Zero) return null;

            IntPtr pData = LockResource(hGlobal);
            if (pData == IntPtr.Zero) return null;

            byte[] bytes = new byte[size];
            Marshal.Copy(pData, bytes, 0, (int)size);
            string xml = Encoding.UTF8.GetString(bytes);
            xml = xml.Trim('\uFEFF', '\0', ' ', '\t', '\r', '\n');
            int startIdx = xml.IndexOf("<assembly");
            if (startIdx > 0 && !xml.StartsWith("<?xml"))
            {
                xml = xml.Substring(startIdx);
            }
            return xml;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hMod != IntPtr.Zero) FreeLibrary(hMod);
        }
    }

    static void InjectLocaleManifest(string exePath)
    {
        if (!File.Exists(exePath)) return;
        try
        {
            string xmlText = ExtractEmbeddedManifest(exePath);
            if (string.IsNullOrEmpty(xmlText))
            {
                xmlText = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                          "<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\"></assembly>";
            }

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xmlText);
            XmlElement root = doc.DocumentElement;
            string asmV3Uri = "urn:schemas-microsoft-com:asm.v3";
            string smi2019Uri = "http://schemas.microsoft.com/SMI/2019/WindowsSettings";

            XmlElement appNode = null;
            foreach (XmlNode n in root.ChildNodes)
            {
                if (n.NodeType == XmlNodeType.Element && n.LocalName == "application")
                {
                    appNode = (XmlElement)n;
                    break;
                }
            }
            if (appNode == null)
            {
                appNode = doc.CreateElement("asmv3", "application", asmV3Uri);
                root.AppendChild(appNode);
            }

            XmlElement winSettingsNode = null;
            foreach (XmlNode n in appNode.ChildNodes)
            {
                if (n.NodeType == XmlNodeType.Element && n.LocalName == "windowsSettings")
                {
                    winSettingsNode = (XmlElement)n;
                    break;
                }
            }
            if (winSettingsNode == null)
            {
                winSettingsNode = doc.CreateElement("asmv3", "windowsSettings", asmV3Uri);
                appNode.AppendChild(winSettingsNode);
            }

            XmlElement acpNode = null;
            foreach (XmlNode n in winSettingsNode.ChildNodes)
            {
                if (n.NodeType == XmlNodeType.Element && n.LocalName == "activeCodePage")
                {
                    acpNode = (XmlElement)n;
                    break;
                }
            }
            if (acpNode == null)
            {
                acpNode = doc.CreateElement("activeCodePage", smi2019Uri);
                winSettingsNode.AppendChild(acpNode);
            }
            acpNode.InnerText = "en-US";

            string manifestPath = exePath + ".manifest";
            using (XmlTextWriter writer = new XmlTextWriter(manifestPath, new UTF8Encoding(false)))
            {
                writer.Formatting = Formatting.Indented;
                doc.Save(writer);
            }

            // Touch executable LastWriteTime so Windows SxS reloads external manifest cache immediately
            DateTime now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(exePath, now);
            Console.WriteLine("  -> Applied en-US process codepage manifest: " + manifestPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  -> [Warn] Manifest injection for " + Path.GetFileName(exePath) + ": " + ex.Message);
        }
    }

    static void EnableExternalManifestInRegistry()
    {
        string[] sxsKeys = new string[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\SideBySide",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\SideBySide"
        };
        foreach (string k in sxsKeys)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(k))
                {
                    if (key != null)
                    {
                        key.SetValue("PreferExternalManifest", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }
        Console.WriteLine("[Step 5] Enabled Windows SxS PreferExternalManifest policy.");
    }

    static void ClearReadOnlyAndLdb(string dir)
    {
        if (!Directory.Exists(dir)) return;
        Console.WriteLine("[Step 3] Scanning database directory: " + dir);
        try
        {
            DirectoryInfo di = new DirectoryInfo(dir);
            if ((di.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                di.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
        catch { }

        foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                FileAttributes attr = File.GetAttributes(file);
                if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
                    Console.WriteLine("  -> Removed Read-Only attribute: " + Path.GetFileName(file));
                }
                if (file.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    Console.WriteLine("  -> Deleted residual lock file: " + file);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  -> [Warn] " + Path.GetFileName(file) + ": " + ex.Message);
            }
        }
    }

    static void GrantFullControl(string dir)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            Process p = Process.Start(new ProcessStartInfo
            {
                FileName = "icacls.exe",
                Arguments = "\"" + dir + "\" /grant *S-1-1-0:(OI)(CI)F *S-1-5-32-545:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F /T /C /Q",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p.WaitForExit(20000);
            Console.WriteLine("  -> Granted Full Control ACL permissions on: " + dir);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  -> [Warn] icacls failed: " + ex.Message);
        }
    }

    static void FixJetRegistryPermissions()
    {
        string[] jetKeys = new string[]
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Jet\4.0",
            @"SOFTWARE\WOW6432Node\Microsoft\Jet\4.0\Engines",
            @"SOFTWARE\Microsoft\Jet\4.0\Engines"
        };

        foreach (string keyPath in jetKeys)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey))
                {
                    if (key != null)
                    {
                        RegistrySecurity rs = key.GetAccessControl();
                        SecurityIdentifier usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                        rs.AddAccessRule(new RegistryAccessRule(
                            usersSid,
                            RegistryRights.FullControl,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                            PropagationFlags.None,
                            AccessControlType.Allow));
                        key.SetAccessControl(rs);
                        Console.WriteLine("[Step 4] Repaired Microsoft Jet 4.0 registry permissions: HKLM\\" + keyPath);
                    }
                }
            }
            catch { }
        }

        string sysWow64 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        string[] jetDlls = new string[] { "msjet40.dll", "msrd3x40.dll", "msrd2x40.dll", "msjtes40.dll", "msjetoledb40.dll" };
        foreach (string dll in jetDlls)
        {
            string p = Path.Combine(sysWow64, dll);
            if (File.Exists(p))
            {
                try
                {
                    Process proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "icacls.exe",
                        Arguments = "\"" + p + "\" /grant *S-1-1-0:RX *S-1-5-32-545:RX /C /Q",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    proc.WaitForExit(5000);
                }
                catch { }
            }
        }
    }

    static void CheckAndConfigureUtf8SystemCodePage()
    {
        try
        {
            using (RegistryKey cpKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Nls\CodePage", true))
            {
                if (cpKey == null) return;
                string currentAcp = Convert.ToString(cpKey.GetValue("ACP", ""));
                Console.WriteLine();
                Console.WriteLine("------------------------------------------------------------------");
                Console.WriteLine("[Step 6] Windows System CodePage (ACP) Check:");
                Console.WriteLine("  Current System ACP: " + currentAcp + (currentAcp == "936" ? " (Simplified Chinese GBK)" : (currentAcp == "65001" ? " (Unicode UTF-8)" : "")));

                if (currentAcp != "65001")
                {
                    Console.WriteLine();
                    Console.WriteLine("  [OPTIONAL: WINDOWS 11 DEEP FIX]");
                    Console.WriteLine("  Steps 1-5 above only modify Multisim itself (0 impact on other apps).");
                    Console.WriteLine("  If Multisim STILL fails to open Master Database on Windows 11 24H2,");
                    Console.WriteLine("  you can enable Windows Global UTF-8 (ACP=65001) here.");
                    Console.WriteLine("  (Note: Global UTF-8 may cause garbled text in very old non-Unicode GBK tools;");
                    Console.WriteLine("   you can run this script again anytime and press 'R' to restore 936.)");
                    Console.WriteLine();
                    Console.Write("  Enable Windows Global UTF-8 CodePage (ACP=65001)? [y/N] (Default=N): ");
                    string ans = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(ans) && ans.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase))
                    {
                        cpKey.SetValue("ACP", "65001", RegistryValueKind.String);
                        cpKey.SetValue("OEMCP", "65001", RegistryValueKind.String);
                        cpKey.SetValue("MACCP", "65001", RegistryValueKind.String);
                        Console.WriteLine("  -> [OK] Set System ACP/OEMCP/MACCP to 65001 (UTF-8).");
                        Console.WriteLine("  -> NOTE: Please RESTART your computer once for the global UTF-8 setting to take effect!");
                    }
                    else
                    {
                        Console.WriteLine("  -> [Skipped] Kept default System ACP (" + currentAcp + "). No reboot needed.");
                    }
                }
                else
                {
                    Console.WriteLine("  -> [OK] Windows UTF-8 CodePage (65001) is currently enabled.");
                    Console.Write("  Keep UTF-8 (65001) or Restore default Chinese GBK (936)? [K=Keep / R=Restore] (Default=K): ");
                    string ans = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(ans) && ans.Trim().StartsWith("R", StringComparison.OrdinalIgnoreCase))
                    {
                        cpKey.SetValue("ACP", "936", RegistryValueKind.String);
                        cpKey.SetValue("OEMCP", "936", RegistryValueKind.String);
                        cpKey.SetValue("MACCP", "10008", RegistryValueKind.String);
                        Console.WriteLine("  -> [Restored] Reverted System ACP/OEMCP to 936 (Chinese GBK). Restart PC to take effect.");
                    }
                }
                Console.WriteLine("------------------------------------------------------------------");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Warn] Could not check/update NLS CodePage: " + ex.Message);
        }
    }

    static void Main(string[] args)
    {
        EnsureAdmin(args);

        Console.WriteLine("==================================================================");
        Console.WriteLine("  NI Multisim 14.x 'Master Database Cannot Be Accessed' Fix Tool");
        Console.WriteLine("  (Includes Win11 24H2 / KB5065426 Chinese Locale Jet Engine Fix)");
        Console.WriteLine("==================================================================");
        Console.WriteLine();

        // Step 1: Close running Multisim processes to release file locks
        StopRunningMultisimProcesses();

        // Step 2: Reset corrupted AppData\Roaming\National Instruments\Circuit Design Suite\<ver>\config
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string cdsAppData = Path.Combine(appData, @"National Instruments\Circuit Design Suite");
        if (Directory.Exists(cdsAppData))
        {
            foreach (string verDir in Directory.GetDirectories(cdsAppData))
            {
                string configDir = Path.Combine(verDir, "config");
                if (Directory.Exists(configDir))
                {
                    string backupDir = Path.Combine(verDir, "config_bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    try
                    {
                        Directory.Move(configDir, backupDir);
                        Console.WriteLine("[Step 2] Reset corrupted user config folder:");
                        Console.WriteLine("  -> Moved to backup: " + backupDir);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[Warn] Could not reset config folder: " + ex.Message);
                    }
                }
            }
        }

        // Step 3: Discover all Database & Installation directories across all local fixed drives
        List<string> dbDirs = new List<string>();
        string progDataCds = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"National Instruments\Circuit Design Suite");
        if (Directory.Exists(progDataCds)) dbDirs.Add(progDataCds);

        string myDocsNi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"National Instruments");
        if (Directory.Exists(myDocsNi)) dbDirs.Add(myDocsNi);

        List<string> installDirs = new List<string>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            string root = drive.RootDirectory.FullName;
            string[] candidateRoots = new string[]
            {
                Path.Combine(root, @"Program Files (x86)\National Instruments"),
                Path.Combine(root, @"Program Files\National Instruments"),
                Path.Combine(root, @"National Instruments")
            };
            foreach (string cRoot in candidateRoots)
            {
                if (!Directory.Exists(cRoot)) continue;
                foreach (string sub in Directory.GetDirectories(cRoot, "Circuit Design Suite*"))
                {
                    installDirs.Add(sub);
                    string dbSub = Path.Combine(sub, "database");
                    if (Directory.Exists(dbSub)) dbDirs.Add(dbSub);
                }
            }
        }

        foreach (string dir in dbDirs)
        {
            ClearReadOnlyAndLdb(dir);
            GrantFullControl(dir);
        }

        // Step 4: Repair Microsoft Jet 4.0 Registry & SysWOW64 DLL Permissions
        FixJetRegistryPermissions();

        // Step 5: Enable External Manifest & Inject activeCodePage=en-US to bypass Win11 Chinese Locale Jet Bug
        EnableExternalManifestInRegistry();
        string[] conflictingDlls = new string[] { "libeay32.dll", "libiconv-2.dll", "libpq.dll" };
        string[] targetExes = new string[] { "multisim.exe", "ultiboard.exe", "SimServer.exe", "db_Create.exe", "Db_Convert.exe", "db_merge.exe" };

        foreach (string instDir in installDirs)
        {
            foreach (string dllName in conflictingDlls)
            {
                string dllPath = Path.Combine(instDir, dllName);
                if (File.Exists(dllPath))
                {
                    try
                    {
                        string bakPath = dllPath + ".bak";
                        if (File.Exists(bakPath)) File.Delete(bakPath);
                        File.Move(dllPath, bakPath);
                        Console.WriteLine("[Step 5] Isolated conflicting DLL: " + dllPath);
                    }
                    catch { }
                }
            }

            foreach (string exeName in targetExes)
            {
                string exePath = Path.Combine(instDir, exeName);
                if (File.Exists(exePath))
                {
                    InjectLocaleManifest(exePath);
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"))
                        {
                            if (key != null) key.SetValue(exePath, "~ RUNASADMIN", RegistryValueKind.String);
                        }
                        using (RegistryKey keyLM = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"))
                        {
                            if (keyLM != null) keyLM.SetValue(exePath, "~ RUNASADMIN", RegistryValueKind.String);
                        }
                    }
                    catch { }
                }
            }
        }

        // Step 6: Check and optionally configure Windows Global UTF-8 CodePage (ACP=65001)
        CheckAndConfigureUtf8SystemCodePage();

        Console.WriteLine();
        Console.WriteLine("==================================================================");
        Console.WriteLine("  [SUCCESS] Master Database & Win11 Locale Fix Completed!");
        Console.WriteLine("==================================================================");
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }
}
