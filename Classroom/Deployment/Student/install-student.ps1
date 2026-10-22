$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Classroom/StudentApp'
$source = [System.IO.Path]::GetFullPath($PSScriptRoot)
$target = [System.IO.Path]::GetFullPath($target)
try {
    if ($source.TrimEnd('\') -ne $target.TrimEnd('\')) {
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
    }
    $exe = Join-Path $target 'Student.exe'
    if (!(Test-Path -LiteralPath $exe)) { throw 'Student.exe is missing. Extract the entire ZIP first.' }
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (!(Test-Path -LiteralPath $key)) { New-Item -Path $key -Force | Out-Null }
    New-ItemProperty -Path $key -Name ClassroomStudent -Value ('"' + $exe + '"') -PropertyType String -Force | Out-Null
    Write-Host "Installed to: $target"
    Write-Host 'Automatic startup enabled for the current Windows user at sign-in.'
    Write-Host 'First connection requires confirmation of the classroom teacher once.'
    $firewall = Join-Path $target 'configure-firewall.ps1'
    if (Test-Path -LiteralPath $firewall) {
        try { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $firewall; if ($LASTEXITCODE -ne 0) { Write-Warning 'Firewall setup failed. Run configure-firewall.cmd from the installed folder.' } }
        catch { Write-Warning "Firewall setup: $($_.Exception.Message)" }
    }
    Start-Process -FilePath $exe -WorkingDirectory $target
} catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }
