@echo off
rem One-time setup of the MCP server's Python environment on THIS PC, then the offline smoke test.
rem Log: setup_result.txt. A .venv copied from another PC does not work (it points to that PC's Python),
rem so an existing one that fails to start is moved aside to server\.venv_old.
cd /d "%~dp0"
set LOG=%~dp0setup_result.txt
echo setup started %DATE% %TIME% > "%LOG%"
if exist server\.venv\Scripts\python.exe (
  server\.venv\Scripts\python.exe -c "import sys" >nul 2>&1 || (
    echo existing .venv is broken, moving it to server\.venv_old >> "%LOG%"
    if exist server\.venv_old rmdir /s /q server\.venv_old
    move server\.venv server\.venv_old >> "%LOG%" 2>&1
  )
)
if not exist server\.venv\Scripts\python.exe (
  where py >nul 2>&1 && (py -3 -m venv server\.venv >> "%LOG%" 2>&1) || (python -m venv server\.venv >> "%LOG%" 2>&1)
)
server\.venv\Scripts\python.exe --version >> "%LOG%" 2>&1 || (echo RESULT=NO_PYTHON >> "%LOG%" & exit /b 1)
server\.venv\Scripts\python.exe -m pip install -r server\requirements.txt >> "%LOG%" 2>&1 || (echo RESULT=PIP_FAILED >> "%LOG%" & exit /b 1)
echo ---- smoke test >> "%LOG%"
if not exist tests\results mkdir tests\results
server\.venv\Scripts\python.exe tests\smoke_test.py >> "%LOG%" 2>&1
echo SMOKE_EXIT=%ERRORLEVEL% >> "%LOG%"
echo RESULT=DONE >> "%LOG%"
