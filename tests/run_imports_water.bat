@echo off
rem Import retry test (water), docs\AUDIT_TEST_PLAN.md B-lines. See tests\imports_live.py. Output: tests\results\imports_water.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0imports_live.py" water > "%~dp0results\imports_water.txt" 2>&1
