# Task 02 — app foundation evidence

All commands were run on this host after the human installed the ASP.NET Core 10.0.13 runtime (`dotnet --list-runtimes` confirmed `Microsoft.AspNetCore.App 10.0.13`).

| Check | Command | Result | Log |
| --- | --- | --- | --- |
| Backend build | `dotnet build src/WorldAlerts.sln` | exit 0, 0 warnings, 0 errors | `docs/evidence/task-02-dotnet-build.log` |
| Backend tests | `dotnet test src/tests/WorldAlerts.Api.Tests/WorldAlerts.Api.Tests.csproj --filter "FullyQualifiedName~FoundationTests"` | exit 0, **3 passed**, 0 failed | `docs/evidence/task-02-backend-tests.log` |
| Angular tests | `npm test` in `src/web` | **4 passed** | `docs/evidence/task-02-angular-tests.log` |
| Angular build | `npm run build` in `src/web` | bundle generated (123.06 kB initial) | `docs/evidence/task-02-angular-build.log` |
| API start | `dotnet run --project src/api --no-launch-profile --urls http://127.0.0.1:5000` | listening on port 5000 | `docs/evidence/task-02-api-start.log` |
| Health endpoint | `curl http://127.0.0.1:5000/api/health` | `200 {"status":"ok"}`, real file `src/api/data/worldalerts.sqlite` created (8192 bytes) | `docs/evidence/task-02-api-start.log` |
| Web dev server | `npm start -- --host 127.0.0.1 --port 4200` | listening on 4200 | `docs/evidence/task-02-web-start.log` |
| Angular → API via proxy | `curl http://127.0.0.1:4200/api/health` | `200 {"status":"ok"}` | command output in this session |
| Local start screenshot | headless Firefox against the dev server | shell shows `Backend: Available` | `docs/evidence/task-02-local-start.png` |

## What was built

- `src/WorldAlerts.sln` with `src/api` (ASP.NET Core, net10.0) and `src/tests/WorldAlerts.Api.Tests` (xunit).
- `src/api/Program.cs`: configuration-driven SQLite path (`WorldAlerts:DatabasePath`, default `<content root>/data/worldalerts.sqlite`), lazy-safe initialization creating a `SchemaVersions` table, and `GET /api/health` returning `200 {"status":"ok"}` only when a real read/write connection succeeds, otherwise `503 {"status":"unavailable"}`. Initialization failures are caught, so an unusable database never takes the API down and never leaks the configured path in the response.
- Delivery configuration stays out of the repository: `appsettings*.json` contain only logging/host defaults, and `.gitignore` now excludes `.env`, `.env.*` and `*.local.json`. No SMTP or Slack credential exists in the tree.
- Angular shell (`src/web`) with `GET /api/health` status and a dev proxy to `http://localhost:5000`; API launch profiles moved from 5071 to 5000 to match the proxy.

## Verification honesty notes

- The backend tests were written by DeepSeek before the health endpoint existed, but because the machine lacked the ASP.NET runtime they could not be executed at that time. The implementation was then written and the suite first ran green afterwards, so **no red run was observed for these tests**; the tests were not adjusted to fit the implementation.
- Manual start found a real defect the tests did not cover: on the default path the `data/` directory did not exist and SQLite could not create the file, so health reported unavailable. Directory creation was added and the endpoint then returned 200. The xunit fixture creates its own temporary directory, so this default-path case remains uncovered by tests.
- The screenshot was taken through a temporary local wrapper page (python HTTP server on port 4300) that holds the browser's load event until the health request has resolved. The wrapper is a screenshot harness only and is not part of the application or the repository.
- Known non-blocking warning accepted by the human: `Microsoft.Data.Sqlite` was upgraded from 10.0.3 to 10.0.13 to clear `NU1903` (high-severity advisory in `SQLitePCLRaw.lib.e_sqlite3` 2.1.11). Restore and builds are now warning-free. The local .NET install still needs `AllowMissingPrunePackageData` because its ASP.NET targeting pack was missing; the package is now resolved from the local NuGet cache.
- Process cleanup: the API, Angular dev server and screenshot wrapper were stopped after verification (`curl` to ports 5000 and 4200 returned no connection).
