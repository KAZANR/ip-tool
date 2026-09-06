@echo off
rem 双击运行 IP 切换工具（脚本内部会自动请求管理员权限）
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ip_tool.ps1"
