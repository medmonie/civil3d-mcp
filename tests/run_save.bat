@echo off
rem Save-failure hunt: replays the 0.15-0.19 live suites in ONE new unsaved drawing (Drawing1 after MCPALLOW) and runs the
rem read-only diagnose_save after every edit. Extra options are passed on (e.g. --suites model --copy-each-edit).
rem Output: tests\results\save.txt and tests\results\save_check.json
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0save_live.py" %* > "%~dp0results\save.txt" 2>&1
