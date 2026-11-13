using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

class FixDatabase
{
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
    }

    static void Main(string[] args)
    {
        EnsureAdmin(args);

        Console.WriteLine("==================================================================");
        Console.WriteLine("  NI Multisim 14.x 'Master Database Cannot Be Accessed' Fix Tool");
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
        else
        {
            Console.WriteLine("[Step 2] No residual AppData config found (clean).");
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

        // Step 4: Repair Microsoft Jet 4.0 Registry Permissions
        FixJetRegistryPermissions();

        // Step 5: Isolate conflicting third-party DLLs & set multisim.exe to Run as Administrator
        string[] conflictingDlls = new string[] { "libeay32.dll", "libiconv-2.dll", "libpq.dll" };
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

            string[] exes = new string[] { "multisim.exe", "ultiboard.exe" };
            foreach (string exeName in exes)
            {
                string exePath = Path.Combine(instDir, exeName);
                if (File.Exists(exePath))
                {
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
                        Console.WriteLine("[Step 5] Enabled Run-As-Administrator for: " + exePath);
                    }
                    catch { }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("==================================================================");
        Console.WriteLine("  [SUCCESS] Master Database repair completed!");
        Console.WriteLine("  You can now open Multisim normally.");
        Console.WriteLine("==================================================================");
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }
}
