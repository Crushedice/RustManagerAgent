dotnet publish ./agent/RustOpsAgent/RustOpsAgent.csproj -c Release -r linux-x64 -o ./build/agent/RustOpsAgent/
dotnet publish ./SteamBot/OpsSteamBot/OpsSteamBot.csproj -c Release -r linux-x64 -o ./build/SteamBot/OpsSteamBot/
dotnet publish ./api/rustmgrapi.csproj -c Release -r linux-x64 -o ./build/api/
