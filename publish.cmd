@echo off
rem MultiPaneExplorer publish script: Release single-file (win-x64, framework-dependent).
rem Requires .NET 8 Desktop Runtime installed. For self-contained publish,
rem change --self-contained false to true (larger output, no runtime needed).
setlocal
cd /d "%~dp0"

dotnet publish src\MultiPaneExplorer.App\MultiPaneExplorer.App.csproj ^
  -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist

if errorlevel 1 (
  echo Publish FAILED.
  exit /b 1
)
echo Publish OK: dist\MultiPaneExplorer.App.exe
endlocal
