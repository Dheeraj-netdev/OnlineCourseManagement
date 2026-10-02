#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
smoke=false
build=true
for arg in "$@"; do
  case "$arg" in
    --smoke) smoke=true ;;
    --no-build) build=false ;;
    *) echo "Usage: bash scripts/run-local.sh [--smoke] [--no-build]" >&2; exit 2 ;;
  esac
done
command -v dotnet >/dev/null || { echo "Install the .NET 8 SDK, then rerun this script." >&2; exit 1; }
if "$build"; then dotnet build OnlineCourseManagement.sln --configuration Release --nologo; fi
mkdir -p .run
export ASPNETCORE_ENVIRONMENT=Development
export USER_SERVICE_URL=http://localhost:5101
export COURSE_SERVICE_URL=http://localhost:5102
dotnet tools/SmokeTest/bin/Release/net8.0/SmokeTest.dll --check-ports
user_pid=""
course_pid=""
cleanup() {
  for pid in "$user_pid" "$course_pid"; do
    if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; fi
  done
  wait 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
dotnet src/UserService/bin/Release/net8.0/UserService.dll --contentRoot "$PWD/src/UserService" --urls "$USER_SERVICE_URL" > .run/user-service.log 2>&1 &
user_pid=$!
dotnet src/CourseService/bin/Release/net8.0/CourseService.dll --contentRoot "$PWD/src/CourseService" --urls "$COURSE_SERVICE_URL" > .run/course-service.log 2>&1 &
course_pid=$!
if ! dotnet tools/SmokeTest/bin/Release/net8.0/SmokeTest.dll --wait; then
  cat .run/user-service.log .run/course-service.log
  exit 1
fi
kill -0 "$user_pid" "$course_pid"
if "$smoke"; then
  dotnet tools/SmokeTest/bin/Release/net8.0/SmokeTest.dll
  exit 0
fi
echo "User Swagger:   $USER_SERVICE_URL/swagger"
echo "Course Swagger: $COURSE_SERVICE_URL/swagger"
echo "Logs: .run/   Stop both services with Ctrl+C."
while kill -0 "$user_pid" 2>/dev/null && kill -0 "$course_pid" 2>/dev/null; do sleep 1; done
echo "A service stopped unexpectedly. See .run/ logs." >&2
exit 1
