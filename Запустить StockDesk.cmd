@echo off
cd /d "%~dp0"
dotnet build "StockDesk.Desktop\StockDesk.Desktop.csproj" -m:1
if errorlevel 1 (pause & exit /b 1)
start "" "StockDesk.Desktop\bin\Debug\net8.0-windows\StockDesk.Desktop.exe"
