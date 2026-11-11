@echo off
rem Compile GeminiDiagDirect.exe avec le compilateur C# fourni avec Windows (.NET Framework 4).
rem Fonctionne sur l eeePC (Windows 10 32 bits) comme sur GitHub.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (echo Compilateur introuvable : %CSC% & exit /b 1)
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /codepage:65001 /win32icon:app.ico /resource:logo.png,logo.png /out:GeminiDiagDirect.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll GeminiDiagDirect.cs
if errorlevel 1 (echo ECHEC de la compilation & exit /b 1)
echo Compilation OK : GeminiDiagDirect.exe
