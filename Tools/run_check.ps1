# Auto check script (P0: AI self-check loop).
# Runs up to three checks in one shot, degrading gracefully when Unity is absent:
#   0. Static check   - plain-text audit of .cs/.unity/.prefab (NO Unity needed)
#   1. Compile check  - headless compile, grep "error CS" in the Unity log
#   2. Scene audit    - load scenes and verify runtime-critical references
#
# If Unity.exe cannot be found, checks 1 & 2 are skipped and the script still
# reports a meaningful result for check 0 (the "no Unity installed" case).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File Tools\run_check.ps1
#   powershell -ExecutionPolicy Bypass -File Tools\run_check.ps1 -UnityPath "D:\unity\2022.3.57f1c2\Editor\Unity.exe"
#
# IMPORTANT: batchmode on a project that the Unity EDITOR has open is NOT
# supported by Unity (project lock + concurrent Library access) and may crash
# the editor. The script aborts (exit code 3) if it detects such a case.
# Re-run with -Force only if you accept the risk.
#
# Results:
#   %TEMP%\unity_static_audit.log    - plain-text audit result (always written)
#   %TEMP%\unity_compile_check.log   - Unity full log (look for "error CS")
#   %TEMP%\unity_scene_audit.log     - per-check [PASS]/[FAIL], ends with SCENE_AUDIT_RESULT
#
# NOTE: keep this file ASCII-only. PowerShell 5.1 decodes .ps1 with the ANSI
# codepage; non-ASCII bytes in a BOM-less UTF-8 file get garbled.

param(
    [string]$Project = "",
    [string]$UnityPath = "",
    [int]$TimeoutSeconds = 180,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if (-not $Project) {
    $Project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
$Project = $Project.TrimEnd('\', '/')

function Get-UnityExe {
    if ($UnityPath -and (Test-Path $UnityPath)) { return $UnityPath }
    if ($env:UNITY_PATH -and (Test-Path $env:UNITY_PATH)) { return $env:UNITY_PATH }

    $version = ''
    $pv = Join-Path $Project 'ProjectSettings\ProjectVersion.txt'
    if (Test-Path $pv) {
        $line = Get-Content $pv -TotalCount 1
        if ($line -match 'm_EditorVersion:\s*(\S+)') { $version = $Matches[1] }
    }

    if ($version) {
        $candidates = @(
            (Join-Path 'D:\unity' "$version\Editor\Unity.exe"),
            (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"),
            (Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor\$version\Editor\Unity.exe"),
            (Join-Path $env:ProgramFiles 'Unity\Editor\Unity.exe')
        )
        foreach ($c in $candidates) {
            if (Test-Path $c) { return $c }
        }
    }
    throw "Unity.exe not found. Pass -UnityPath or set env var UNITY_PATH."
}

function Invoke-UnityBatch {
    param([string]$LogPath, [string]$ExecuteMethod)
    $args = @('-batchmode', '-nographics', '-quit',
              '-projectPath', ('"{0}"' -f $Project),
              '-logFile', ('"{0}"' -f $LogPath))
    if ($ExecuteMethod) {
        $args += @('-executeMethod', $ExecuteMethod)
    }
    $p = Start-Process -FilePath $Unity -ArgumentList $args -PassThru -WindowStyle Hidden
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        throw "Unity batch mode timed out after $TimeoutSeconds s. Check $LogPath"
    }
    return $p.ExitCode
}

# Retry once if Unity crashed during startup (seen on some machines: first
# batchmode launch crashes at ~16KB into the log, second launch succeeds).
function Invoke-UnityBatchWithRetry {
    param([string]$LogPath, [string]$ExecuteMethod)
    $code = -1
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $code = Invoke-UnityBatch -LogPath $LogPath -ExecuteMethod $ExecuteMethod
        $crashed = [bool](Select-String -Path $LogPath -Pattern 'crash has been intercepted' -Quiet -ErrorAction SilentlyContinue)
        if (-not $crashed) { return $code }
        Write-Host "[AutoCheck] Unity crashed during batch run (attempt $attempt/2). Retrying after 5s..."
        Start-Sleep -Seconds 5
    }
    return $code
}

# Returns $true if ANY Unity process (e.g. an open editor) already has THIS
# project loaded. Checked by matching the project path in the process command
# line, so it ignores editors opened on other projects.
function Test-ProjectOpenedByEditor {
    $pattern = [regex]::Escape($Project) -replace '\\\\', '[\\/]'
    $hits = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match $pattern })
    return ($hits.Count -gt 0)
}

# ---------- Step 0: Static check (always runs, no Unity needed) ----------
Write-Host '[0/3] Static check (no Unity required) ...'
$staticScript = Join-Path $PSScriptRoot 'static_check.ps1'
$staticLog = Join-Path $env:TEMP 'unity_static_audit.log'
Remove-Item $staticLog -ErrorAction SilentlyContinue

& powershell -NoProfile -ExecutionPolicy Bypass -File $staticScript -Project $Project
$staticExit = $LASTEXITCODE

if (Test-Path $staticLog) {
    Get-Content $staticLog -Encoding UTF8 | ForEach-Object { Write-Host ("  " + $_) }
}
$staticPass = ($staticExit -eq 0)

# ---------- Locate Unity (optional; degrade gracefully if absent) ----------
$Unity = $null
try {
    $Unity = Get-UnityExe
} catch {
    Write-Host '[AutoCheck] Unity.exe not found -> compile check & scene audit skipped.'
    Write-Host '           Set UNITY_PATH (or pass -UnityPath) to enable them.'
}
if ($Unity) { Write-Host "[AutoCheck] Unity: $Unity" }

$compilePass = $null   # $null = skipped
$auditPass = $null     # $null = skipped

if ($Unity) {
    # Refuse to run while the editor has this project open: concurrent batchmode
    # on the same project is unsupported by Unity and may crash the editor.
    if ((Test-ProjectOpenedByEditor) -and -not $Force) {
        Write-Host '[AutoCheck] STOP: the Unity editor already has this project open.'
        Write-Host '       Running batchmode on the same project is NOT supported'
        Write-Host '       and may crash the editor / corrupt Library caches.'
        Write-Host '       Save your work, close the editor, then re-run.'
        Write-Host '       (Use -Force to override - NOT recommended.)'
        exit 3
    }

    # ---------- Step 1: Compile check ----------
    Write-Host '[1/3] Compile check (takes 1-2 min) ...'
    $compileLog = Join-Path $env:TEMP 'unity_compile_check.log'
    Remove-Item $compileLog -ErrorAction SilentlyContinue
    $compileExit = Invoke-UnityBatchWithRetry -LogPath $compileLog

    $errors = @(Select-String -Path $compileLog -Pattern 'error CS' -ErrorAction SilentlyContinue)
    $warnings = @(Select-String -Path $compileLog -Pattern 'warning CS' -ErrorAction SilentlyContinue)
    $exitedOk = [bool](Select-String -Path $compileLog -Pattern 'Exiting batchmode successfully now!' -Quiet -ErrorAction SilentlyContinue)

    if ($errors.Count -gt 0) {
        Write-Host "[FAIL] Compile errors: $($errors.Count)"
        $errors | Select-Object -First 10 | ForEach-Object { Write-Host ("  " + $_.Line.Trim()) }
        $compilePass = $false
    } elseif ($compileExit -ne 0 -and -not $exitedOk) {
        Write-Host "[FAIL] Unity batch exited abnormally (code=$compileExit), full log: $compileLog"
        $compilePass = $false
    } else {
        Write-Host "[PASS] Compile OK (warnings: $($warnings.Count)), full log: $compileLog"
        $compilePass = $true
    }

    # ---------- Step 2: Scene audit ----------
    Write-Host '[2/3] Scene audit ...'
    $auditLog = Join-Path $env:TEMP 'unity_scene_audit.log'
    $auditRunLog = Join-Path $env:TEMP 'unity_scene_audit_run.log'
    Remove-Item $auditLog, $auditRunLog -ErrorAction SilentlyContinue

    $null = Invoke-UnityBatchWithRetry -LogPath $auditRunLog -ExecuteMethod 'EditorTools.SceneAudit.RunFromCommandLine'

    if (Test-Path $auditLog) {
        Get-Content $auditLog | ForEach-Object { Write-Host ("  " + $_) }
        $auditPass = [bool](Select-String -Path $auditLog -Pattern 'SCENE_AUDIT_RESULT: PASS' -Quiet)
    } else {
        Write-Host "[FAIL] Scene audit log not generated: $auditLog"
        Write-Host "       Unity run log: $auditRunLog"
        $auditPass = $false
    }
}

# ---------- Summary ----------
Write-Host ''
Write-Host ("[AutoCheck] Static : " + $(if ($staticPass) { 'PASS' } else { 'FAIL' }))
Write-Host ("[AutoCheck] Compile: " + $(if ($null -eq $compilePass) { 'SKIPPED' } elseif ($compilePass) { 'PASS' } else { 'FAIL' }))
Write-Host ("[AutoCheck] Scene  : " + $(if ($null -eq $auditPass) { 'SKIPPED' } elseif ($auditPass) { 'PASS' } else { 'FAIL' }))

$overallPass = ($staticPass) -and
               ($null -eq $compilePass -or $compilePass) -and
               ($null -eq $auditPass -or $auditPass)

if ($overallPass) {
    Write-Host 'AUTO CHECK: ALL PASS'
    exit 0
} else {
    Write-Host 'AUTO CHECK: FAIL'
    exit 1
}
