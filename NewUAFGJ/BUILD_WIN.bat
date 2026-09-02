@echo off
setlocal
chcp 65001 >nul
set DOTNET_CLI_UI_LANGUAGE=en
set PYTHONUTF8=1

echo [BUILD] Restoring UAFGJ with AssetsTools.NET 3.0.5...
dotnet restore UAFGJ.csproj
if errorlevel 1 exit /b %errorlevel%

echo [BUILD] Building Release x64...
dotnet build UAFGJ.csproj -c Release -r win-x64 --self-contained false
if errorlevel 1 exit /b %errorlevel%

echo [BUILD] Done.
endlocal
