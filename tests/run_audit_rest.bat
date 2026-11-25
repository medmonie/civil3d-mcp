@echo off
rem Re-run only the audit blocks that need the gravity network fixture. Output: tests\results\audit_rest.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0audit_live.py" c4_c5_d1 c10_to_c13_d8 a4_a7 > "%~dp0results\audit_rest.txt" 2>&1
