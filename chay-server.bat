@echo off
rem Chay server CookMate.Api tai http://0.0.0.0:5255 (Development)
cd /d "%~dp0CookMate.Api"
dotnet run --launch-profile http
