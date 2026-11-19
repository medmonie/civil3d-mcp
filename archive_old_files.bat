@echo off
rem One-off after updating the sources to v0.8: moves the files that v0.8 replaced into _archive_v0.7.
rem Nothing is deleted. Run it BEFORE build.bat - the plugin does not build while old and new files are both present.
cd /d "%~dp0"
for %%D in (plugin\Edit plugin\Read tests) do if not exist "_archive_v0.7\%%D" mkdir "_archive_v0.7\%%D"
(
for %%F in (AcadOps DraftOps AcadMoreOps CivilOps CivilMoreOps CivilAdvancedOps) do if exist "plugin\Edit\%%F.cs" move /y "plugin\Edit\%%F.cs" "_archive_v0.7\plugin\Edit\"
if exist "plugin\Read\CivilRead.cs" move /y "plugin\Read\CivilRead.cs" "_archive_v0.7\plugin\Read\"
for %%F in (run_live_edit_acad run_live_edit_acad2 run_live_edit_civil3) do if exist "tests\%%F.bat" move /y "tests\%%F.bat" "_archive_v0.7\tests\"
echo --- plugin\Edit
dir /b plugin\Edit
echo --- plugin\Read
dir /b plugin\Read
echo --- tests
dir /b tests
) > archive_result.txt 2>&1
