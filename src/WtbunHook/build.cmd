@echo off
rem Builds wtbunhook.dll (x64) with the Visual Studio C++ tools. Exit code 1 if the tools are missing.
setlocal
set "here=%~dp0"
set "vswhere=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%vswhere%" (
  echo wtbunhook: Visual Studio not found
  exit /b 1
)
set "vs="
for /f "usebackq delims=" %%i in (`"%vswhere%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "vs=%%i"
if not defined vs (
  echo wtbunhook: C++ tools not found
  exit /b 1
)
call "%vs%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist "%here%obj" mkdir "%here%obj"
if not exist "%here%bin" mkdir "%here%bin"
cl /nologo /utf-8 /O2 /MT /W4 /WX /EHs-c- /LD "%here%wtbunhook.cpp" /Fo"%here%obj\\" /Fe"%here%bin\wtbunhook.dll" /link /NOLOGO /IMPLIB:"%here%obj\wtbunhook.lib"
