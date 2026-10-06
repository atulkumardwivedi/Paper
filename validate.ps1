$ErrorActionPreference='Stop'
dotnet build "$PSScriptRoot/api/PaperTrader.Api.csproj"
if ($LASTEXITCODE -ne 0) { throw 'API build failed' }
dotnet run --project "$PSScriptRoot/tests/PaperTrader.Checks.csproj"
if ($LASTEXITCODE -ne 0) { throw 'Engine checks failed' }
Push-Location "$PSScriptRoot/ui"
try { npm install; if($LASTEXITCODE -ne 0){throw 'npm install failed'}; npm run build; if($LASTEXITCODE -ne 0){throw 'Angular build failed'} } finally {Pop-Location}
