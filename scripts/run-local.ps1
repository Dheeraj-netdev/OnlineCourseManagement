param([switch]$Smoke, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 SDK, then rerun this script.' }
if (-not $NoBuild) {
    & dotnet build OnlineCourseManagement.sln --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
New-Item -ItemType Directory -Force '.run' | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:USER_SERVICE_URL = 'http://localhost:5101'
$env:COURSE_SERVICE_URL = 'http://localhost:5102'
& dotnet tools/SmokeTest/bin/Release/net8.0/SmokeTest.dll --check-ports
if ($LASTEXITCODE -ne 0) { throw 'Ports 5101 and 5102 must be free.' }
$userProcess = $null
$courseProcess = $null
try {
    $userArgs = @('src/UserService/bin/Release/net8.0/UserService.dll', '--contentRoot', ('"{0}"' -f (Join-Path $PWD 'src/UserService')), '--urls', $env:USER_SERVICE_URL)
    $courseArgs = @('src/CourseService/bin/Release/net8.0/CourseService.dll', '--contentRoot', ('"{0}"' -f (Join-Path $PWD 'src/CourseService')), '--urls', $env:COURSE_SERVICE_URL)
    $userProcess = Start-Process dotnet -ArgumentList $userArgs -PassThru -NoNewWindow -RedirectStandardOutput '.run/user-service.log' -RedirectStandardError '.run/user-service.err.log'
    $courseProcess = Start-Process dotnet -ArgumentList $courseArgs -PassThru -NoNewWindow -RedirectStandardOutput '.run/course-service.log' -RedirectStandardError '.run/course-service.err.log'
    & dotnet tools/SmokeTest/bin/Release/net8.0/SmokeTest.dll --wait
    if ($LASTEXITCODE -ne 0 -or $userProcess.HasExited -or $courseProcess.HasExited) { throw 'Startup failed. Check .run logs.' }
    if ($Smoke) {
        & dotnet tools/SmokeTest/bin/Release/net8.0/SmokeTest.dll
        if ($LASTEXITCODE -ne 0) { throw 'Smoke test failed.' }
    } else {
        Write-Host 'User Swagger: http://localhost:5101/swagger'
        Write-Host 'Course Swagger: http://localhost:5102/swagger'
        Write-Host 'Logs: .run/   Stop both services with Ctrl+C.'
        while (-not $userProcess.HasExited -and -not $courseProcess.HasExited) { Start-Sleep -Seconds 1 }
        throw 'A service stopped unexpectedly. Check .run logs.'
    }
} finally {
    foreach ($process in @($userProcess, $courseProcess)) {
        if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
    }
}
