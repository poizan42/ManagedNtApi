@echo off
set flattener="..\..\tools\StructFlattener\bin\Debug\StructFlattener.exe"

call:gen UnsafeStructs
call:gen UnsafeStructs.win32 UnsafeStructs.in.cs

goto:eof

:gen
set bn=%1
set params=
shift
:getparams
	if "%1" == "" goto gotparams
	set params=%params% %1
	shift
	goto getparams
:gotparams
rem echo %flattener% %bn%.in.cs -32 -out %bn%.x86.cs %params%
%flattener% %bn%.in.cs -32 -out %bn%.x86.cs %params%
%flattener% %bn%.in.cs -64 -out %bn%.x64.cs %params%
goto:eof