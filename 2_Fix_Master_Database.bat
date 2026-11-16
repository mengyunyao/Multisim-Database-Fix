@echo off
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /r:System.dll /r:System.Xml.dll /out:"%TEMP%\Multisim_FixDatabase.exe" "%~dp0..\Multisim-Database-Fix\FixDatabase.cs"
"%TEMP%\Multisim_FixDatabase.exe"
