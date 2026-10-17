@echo off
setlocal
"%~dp0flashrouter.exe" %*
exit /b %ERRORLEVEL%
