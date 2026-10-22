@echo off
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ClassroomStudent /f
echo Startup disabled. Use Exit from the application's tray icon to stop it.
pause
