@echo off
rem Imports "..\sewer network\Full output from sewergems.xlsx" into the ACTIVE (new) drawing and checks the result.
rem Output: tests\results\sewergems.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" -m pip install -q -r "%~dp0..\server\requirements.txt" > "%~dp0results\sewergems.txt" 2>&1
"%~dp0..\server\.venv\Scripts\python" "%~dp0sewergems_live.py" >> "%~dp0results\sewergems.txt" 2>&1
