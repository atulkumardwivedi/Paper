$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET 10 SDK first.' }
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'Install Node.js 20 LTS first.' }
Push-Location "$root/ui"
try { npm install; if ($LASTEXITCODE -ne 0) { throw 'npm install failed' } } finally { Pop-Location }
dotnet build "$root/api/PaperTrader.Api.csproj"
if ($LASTEXITCODE -ne 0) { throw 'API build failed' }
Start-Process powershell -ArgumentList '-NoExit','-Command',"Set-Location '$root/api'; dotnet run --project PaperTrader.Api.csproj"
Start-Process powershell -ArgumentList '-NoExit','-Command',"Set-Location '$root/ui'; npm start"
Write-Host 'Open http://localhost:4202. API: http://localhost:5092'
