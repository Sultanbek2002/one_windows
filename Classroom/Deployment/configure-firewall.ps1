$ErrorActionPreference = 'Stop'
$student = Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Student.exe')
$role = if ($student) { 'Student' } else { 'Teacher' }
$exe = Join-Path $PSScriptRoot "$role.exe"
if (!(Test-Path -LiteralPath $exe)) { throw 'Place this script next to Teacher.exe or Student.exe.' }
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    Start-Process powershell.exe -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait
    exit
}
$port = if ($student) { 47833 } else { 47832 }
$name = "Classroom-$role-Discovery"
if (Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue) { Set-NetFirewallRule -Name $name -Program $exe -Protocol UDP -LocalPort $port -Profile Private -RemoteAddress LocalSubnet -Enabled True -Action Allow }
else { New-NetFirewallRule -Name $name -DisplayName "$role classroom discovery" -Direction Inbound -Program $exe -Protocol UDP -LocalPort $port -Profile Private -RemoteAddress LocalSubnet -Action Allow | Out-Null }
if (!$student) {
    $name = 'Classroom-Teacher-TLS'
    if (Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue) { Set-NetFirewallRule -Name $name -Program $exe -Protocol TCP -LocalPort 47831 -Profile Private -RemoteAddress LocalSubnet -Enabled True -Action Allow }
    else { New-NetFirewallRule -Name $name -DisplayName 'Teacher classroom TLS' -Direction Inbound -Program $exe -Protocol TCP -LocalPort 47831 -Profile Private -RemoteAddress LocalSubnet -Action Allow | Out-Null }
}
