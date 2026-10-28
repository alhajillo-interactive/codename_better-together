# Compila el proyecto en batchmode y reporta errores C#.
# No corre si el editor tiene el proyecto abierto (Temp/UnityLockfile).
# Uso: pwsh Tools/compile-check.ps1 [-UnityPath "C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe"]
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe"
)

$project = Resolve-Path (Join-Path $PSScriptRoot "..")
$lock = Join-Path $project "Temp\UnityLockfile"
if (Test-Path $lock) {
    Write-Host "El editor tiene el proyecto abierto (Temp/UnityLockfile). Compila desde el editor o ciérralo." -ForegroundColor Yellow
    exit 2
}
if (-not (Test-Path $UnityPath)) {
    Write-Host "No se encontró Unity en $UnityPath" -ForegroundColor Red
    exit 2
}

$log = Join-Path $env:TEMP "gnomore-compile.log"
& $UnityPath -batchmode -nographics -quit -projectPath "$project" -logFile "$log" | Out-Null
$exit = $LASTEXITCODE

$errors = Select-String -Path $log -Pattern "error CS[0-9]+" | Select-Object -First 50
$warnings = Select-String -Path $log -Pattern "warning CS[0-9]+" | Measure-Object

if ($errors) {
    Write-Host "ERRORES DE COMPILACIÓN:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host $_.Line }
    exit 1
}

Write-Host "Compila OK (exit $exit). Warnings C#: $($warnings.Count). Log: $log" -ForegroundColor Green
exit 0
