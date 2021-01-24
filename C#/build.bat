@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvarsall.bat" x86_amd64
msbuild -m /t:build /p:Configuration=Release;Platform=x64 ".\C#\SDK-CS.sln"
