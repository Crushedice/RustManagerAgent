@echo off
setlocal

cd /d "%~dp0"

rem Clear stale generated state that has been breaking Windows admin publishes.
if exist "SteamBot\OpsSteamBot\obj" rmdir /s /q "SteamBot\OpsSteamBot\obj"
del /q "api\rustmgrapi.deps.json" "api\rustmgrapi.runtimeconfig.json" "api\rustmgrapi.dll" "api\rustmgrapi.pdb" 2>nul

dotnet publish ".\agent\RustOpsAgent\RustOpsAgent.csproj" -c Release -r linux-x64 -o ".\build\agent\RustOpsAgent\" || exit /b 1
dotnet publish ".\SteamBot\OpsSteamBot\OpsSteamBot.csproj" -c Release -r linux-x64 -o ".\build\SteamBot\OpsSteamBot\" || exit /b 1
dotnet publish ".\api\rustmgrapi.csproj" -c Release -r linux-x64 -o ".\build\api\" || exit /b 1
