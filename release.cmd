@echo off
rem Builds the release package from Profile.ini ([Title] ShortText and [Version] Text):
rem   release\<ShortText>_<Version>\      the release binaries, README.md, LICENSE and NOTICE
rem   release\<ShortText>_<Version>.zip   that folder, zipped
rem Exit code 1 on failure.
setlocal
set "root=%~dp0"
set "proj=%root%src\Wtbun\Wtbun.csproj"
set "out=%root%release"

rem Read the values through MSBuild, so Profile.ini is parsed the same way as in the build (Directory.Build.props)
set "name="
set "version="
for /f "usebackq delims=" %%i in (`dotnet msbuild "%proj%" -nologo -getProperty:ProfileShortTitle`) do set "name=%%i"
for /f "usebackq delims=" %%i in (`dotnet msbuild "%proj%" -nologo -getProperty:Version`) do set "version=%%i"
if not defined name goto noprofile
if not defined version goto noprofile

set "pkg=%name%_%version%"
set "dir=%out%\%pkg%"
set "zip=%out%\%pkg%.zip"
echo Package: %pkg%

if exist "%dir%" rmdir /s /q "%dir%" || goto fail
if exist "%zip%" del /q "%zip%" || goto fail
if not exist "%out%" mkdir "%out%" || goto fail

rem Release build without debug symbols (.pdb)
dotnet publish "%proj%" -c Release -o "%dir%" -p:DebugType=none -nologo || goto fail

rem wtbunhook.dll is optional for the build (needs the C++ tools), but required for the package
if not exist "%dir%\wtbunhook.dll" (
  echo release: wtbunhook.dll was not built. Install the Visual Studio C++ tools.
  goto fail
)

for %%f in (README.md LICENSE NOTICE) do (
  copy /y "%root%%%f" "%dir%\" >nul || goto fail
)

rem Zip the folder itself, so the archive extracts into <ShortText>_<Version>\
rem Use the tar of Windows (bsdtar, which can write zip), not another tar on PATH (e.g. GNU tar from Git)
"%SystemRoot%\System32\tar.exe" -a -c -f "%zip%" -C "%out%" "%pkg%" || goto fail

echo Created: %zip%
exit /b 0

:noprofile
echo release: could not read [Title] ShortText or [Version] Text from Profile.ini.
:fail
echo release: failed.
exit /b 1
