<#
.SYNOPSIS
  The Agyo green ratchet — the single merge gate for this repo.

.DESCRIPTION
  Mirrors Koan's local-gate model (CI is deliberately light; the gate is a local script):

    A.  Build       dotnet build Agyo.sln   — every library + sample.
    A'. Test        dotnet test  Agyo.sln    — container-gated integration specs skip
                    cleanly when infra is absent. Skip with -SkipTests.
    B.  Surfaces    scripts/lint-surfaces.sh docs/SURFACES.md — the rotation-contract tripwire.

  Exit code is 0 (GREEN) only when every run leg passes; otherwise 1 (RED).
#>
param(
    [string]$Configuration = "Debug",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path "$PSScriptRoot/..").ProviderPath
Push-Location $root

$results = [ordered]@{}
function Invoke-Leg {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "`n=== [ratchet] $Name ===" -ForegroundColor Cyan
    & $Action
    $ok = ($LASTEXITCODE -eq 0)
    $results[$Name] = $ok
    if ($ok) { Write-Host "[ratchet] $Name : PASS" -ForegroundColor Green }
    else { Write-Host "[ratchet] $Name : FAIL (exit $LASTEXITCODE)" -ForegroundColor Red }
}

try {
    Write-Host "[ratchet] config=$Configuration  skipTests=$SkipTests"

    Invoke-Leg 'A. build' { & dotnet build "$root/Agyo.sln" -c $Configuration --nologo }

    if (-not $SkipTests) {
        Invoke-Leg "A'. test" { & dotnet test "$root/Agyo.sln" -c $Configuration --no-build --nologo }
    }

    Invoke-Leg 'B. surfaces' { & bash "$root/scripts/lint-surfaces.sh" "$root/docs/SURFACES.md" }

    Write-Host "`n=== [ratchet] summary ===" -ForegroundColor Cyan
    $failed = @()
    foreach ($k in $results.Keys) {
        $pass = $results[$k]
        Write-Host ("  {0,-22} {1}" -f $k, $(if ($pass) { 'PASS' } else { 'FAIL' })) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
        if (-not $pass) { $failed += $k }
    }

    if ($failed.Count -gt 0) {
        Write-Host "`n[ratchet] RED — $($failed.Count) leg(s) failed: $($failed -join ', ')" -ForegroundColor Red
        exit 1
    }
    Write-Host "`n[ratchet] GREEN — all legs passed." -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
