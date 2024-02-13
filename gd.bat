@echo off
REM This script is for customization by the user. For example, you might edit
REM this file to launch your favorite diff tool instead of windiff.exe, to
REM add -uno by default, etc.
REM
REM Run "GitDiffWrapper.exe -h" for details on supported parameters. Important
REM parameters include:
REM
REM -uno       Disables scanning for untracked files by default.
REM --tool=... Sets the GUI diff tool to be invoked. Default is "windiff.exe".
REM --args=... Sets the parameters to pass to the GUI diff tool.
REM            Default is "-i $F", which works well for windiff.
REM            For other tools, you will typically use "$1 $2".
REM -v or -q   Increases (-v) or decreases (-q) the detail of output.
REM
REM GitDiffWrapper works really well with windiff because it supports a "list"
REM file containing a the pairs of files to compare (most diff tools just take
REM a pair of folders to compare). This allows the diff to show things like
REM moves and renames (where the left and right files have different names).
REM It also allows the diff to compare directly against files in the working
REM directory (instead of comparing against a temporary copy of the working
REM directory files), so you can edit the working directory file from within
REM the diff tool.
REM
REM GitDiffWrapper also works with directory names. It will populate two
REM temporary folders ($1="left", $2="right") with the files to be compared,
REM and can pass the names of those folders to the diff tool.
REM
REM Basic usage (to launch windiff.exe) would be:
REM
REM 	GitDiffWrapper.exe %*
REM
REM To use a tool other than windiff.exe (e.g. odd.exe, BCompare.exe), you
REM would need to add both --tool and --args parameters:
REM
REM		GitDiffWrapper.exe --tool="odd.exe" --args="$1 $2" %*
REM
REM Customize this line as needed (default behavior launches windiff.exe):
GitDiffWrapper.exe %*
