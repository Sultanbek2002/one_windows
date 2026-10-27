$report = Join-Path $PSScriptRoot 'startup-diagnostic.txt'
"ScreenLink startup diagnostics $(Get-Date -Format o)" | Set-Content -LiteralPath $report -Encoding UTF8
function Record($value) { $value | Out-File -LiteralPath $report -Append -Encoding UTF8; Write-Host $value }
Record "Windows user: $env:USERDOMAIN\$env:USERNAME"
Record "Computer: $env:COMPUTERNAME"
$path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command = (Get-ItemProperty -LiteralPath $path -Name ClassroomStudent -ErrorAction SilentlyContinue).ClassroomStudent
Record "Startup command: $command"
if (!$command) { Record 'NOT REGISTERED: run install-student.cmd or launch the new Student.exe once.' }
elseif ($command -match '^"([^"]+)"$') { Record "Startup executable exists: $(Test-Path -LiteralPath $Matches[1])" }
Get-Process Student -ErrorAction SilentlyContinue | ForEach-Object { Record "Student process: $($_.Id) $($_.Path)" }
$approved = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' -Name ClassroomStudent -ErrorAction SilentlyContinue).ClassroomStudent
if ($approved) { Record "StartupApproved bytes: $([BitConverter]::ToString($approved))" }
$log = Join-Path $env:LOCALAPPDATA 'Classroom/Student.log'
if (Test-Path -LiteralPath $log) { Record 'Recent Student log:'; Get-Content -LiteralPath $log -Tail 30 -Encoding UTF8 | ForEach-Object { Record $_ } }
else { Record 'No Student log for this Windows account.' }
Record 'Autostart is per Windows account and runs after sign-in, not before it.'
