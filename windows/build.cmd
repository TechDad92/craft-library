@echo off
rem Builds Craft Library and its installer on Windows.
rem Needs the .NET 10 SDK (https://dotnet.microsoft.com/download) and NSIS (https://nsis.sourceforge.io).
rem The finished installer appears in windows\dist\.
cd /d "%~dp0"
for /f "tokens=3 delims=<>" %%v in ('findstr "<Version>" CraftLibrary.App\CraftLibrary.App.csproj') do set VERSION=%%v
echo Building Craft Library %VERSION%
dotnet test CraftLibrary.Tests -nologo -v q || exit /b 1
if exist publish rmdir /s /q publish
if not exist dist mkdir dist
dotnet publish CraftLibrary.App -nologo -v q -c Release -o publish || exit /b 1
"%ProgramFiles(x86)%\NSIS\makensis.exe" -V2 -DVERSION=%VERSION% installer\CraftLibrary.nsi || exit /b 1
echo Done: dist\Craft-Library-Setup-%VERSION%.exe
