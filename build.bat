@echo off
setlocal EnableExtensions

rem Build Seed Apocalypse from the matching MelonLoader / game assemblies.
rem Usage: build.bat [optional path to the game's root folder]

set "ROOT=%~dp0"
set "LIBDIR=%ROOT%libs\"
set "PROJECT=%ROOT%SeedApocalypse.csproj"
set "OUTPUT=%ROOT%bin\Release\net6.0\SeedApocalypse.dll"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install the .NET 6 SDK or newer first.
    exit /b 1
)

if not exist "%LIBDIR%net6\MelonLoader.dll" (
    echo ERROR: Missing libs\net6\MelonLoader.dll
    echo Put the MelonLoader 0.7.x net6 reference files in libs\net6\.
    exit /b 1
)
if not exist "%LIBDIR%net6\0Harmony.dll" (
    echo ERROR: Missing libs\net6\0Harmony.dll
    exit /b 1
)
if not exist "%LIBDIR%net6\Il2CppInterop.Runtime.dll" (
    echo ERROR: Missing libs\net6\Il2CppInterop.Runtime.dll
    exit /b 1
)
if not exist "%LIBDIR%net6\Il2CppInterop.Common.dll" (
    echo ERROR: Missing libs\net6\Il2CppInterop.Common.dll
    exit /b 1
)
dir /b "%LIBDIR%Il2CppAssemblies\*.dll" >nul 2>nul
if errorlevel 1 (
    echo ERROR: No game reference DLLs found in libs\Il2CppAssemblies\.
    echo Extract the Il2CppAssemblies for your installed PvZ Fusion version there.
    exit /b 1
)

echo Building Seed Apocalypse...
dotnet build "%PROJECT%" -c Release "-p:LibDir=%LIBDIR%"
if errorlevel 1 (
    echo.
    echo BUILD FAILED. Check that the references match PvZ Fusion 4.0.5.2 and MelonLoader 0.7.x.
    exit /b 1
)

if not exist "%OUTPUT%" (
    echo ERROR: Build reported success but the DLL was not found at:
    echo %OUTPUT%
    exit /b 1
)

echo.
echo SUCCESS: %OUTPUT%
if not "%~1"=="" (
    if not exist "%~1\Mods" mkdir "%~1\Mods"
    copy /y "%OUTPUT%" "%~1\Mods\SeedApocalypse.dll" >nul
    if errorlevel 1 (
        echo WARNING: Could not copy the DLL to "%~1\Mods".
        exit /b 1
    )
    echo Installed to: %~1\Mods\SeedApocalypse.dll
) else (
    echo Copy this DLL into your game's Mods folder to install the mod.
)

endlocal
