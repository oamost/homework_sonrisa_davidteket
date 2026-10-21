# World Alerts Docker lifecycle on Windows PowerShell 5.1+ and PowerShell 7.
param([ValidateSet('up','build','start','stop','restart','status','logs','clean-dev','help')][string]$Action='up')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$config = if ($env:WORLDALERTS_ENV_FILE) { $env:WORLDALERTS_ENV_FILE } else { Join-Path $root '.env' }
$instance = if ($env:WORLDALERTS_INSTANCE) { $env:WORLDALERTS_INSTANCE } else { 'worldalerts-local' }
$port = if ($env:WORLDALERTS_PORT) { $env:WORLDALERTS_PORT } else { '8080' }
$image = "${instance}:verified"
$label = "org.worldalerts.instance=$instance"
$volume = "${instance}-data"
$keys = "${instance}-keys"
$dockerfile = Join-Path $root 'src/container/Dockerfile'
# LOCAL-ONLY demo credential. Never publish this port publicly.
$demoHash = '310000:o4KcI4pukTmLl9YNc07O5/qHipAB6FilN0vXRH7FS/Q=:8Tjil5XZEjiiOBBAeQPxxDlRau0Ve/0twEE17tPVuC0='

function Assert-NativeExit([string]$description) {
    if ($LASTEXITCODE -ne 0) { throw "$description failed (exit code $LASTEXITCODE)." }
}
function Stop-DevServer {
    # Only terminate listeners provably started by this repository's Angular CLI.
    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
        Write-Warning 'Get-NetTCPConnection unavailable; check old :4200 listener manually.'
        return
    }
    $connections = @(Get-NetTCPConnection -State Listen -LocalPort 4200 -ErrorAction SilentlyContinue)
    foreach ($pidValue in @($connections | Select-Object -ExpandProperty OwningProcess -Unique)) {
        if (-not $pidValue) { continue }
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$pidValue" -ErrorAction SilentlyContinue
        if (-not $proc) { continue }
        $line = [string]$proc.CommandLine
        $isRepo = $line.IndexOf($root, [StringComparison]::OrdinalIgnoreCase) -ge 0
        $isAngular = $line -match '@angular[/\\]cli|ng\.js|ng serve|vite'
        if ($isRepo -and $isAngular) {
            Write-Host "Stopping old Angular :4200 server for this repo, PID $pidValue."
            Stop-Process -Id $pidValue -ErrorAction Stop
        } else {
            Write-Warning "Leaving unrelated/unidentified :4200 listener PID $pidValue running."
        }
    }
}
function Ensure-Env {
    if (Test-Path -LiteralPath $config -PathType Leaf) { return }
    if ($env:WORLDALERTS_ENV_FILE) { throw "Configured env file missing: $config" }
    [System.IO.File]::WriteAllText($config, '')
    Write-Host 'Created empty .env; configure provider credentials via the application.'
}
if ($Action -eq 'help') {
    Write-Host 'Usage: .\run.ps1 [up|build|start|stop|restart|status|logs|clean-dev|help]'
    exit 0
}
if ($instance -cnotmatch '^[a-z0-9][a-z0-9_.-]*$' -or $port -notmatch '^[0-9]+$' -or
    $port.Length -gt 5 -or [int]$port -lt 1 -or [int]$port -gt 65535) {
    throw 'Invalid WORLDALERTS_INSTANCE or WORLDALERTS_PORT.'
}
if ($Action -eq 'clean-dev') { Stop-DevServer; exit 0 }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker CLI not found.' }

function Owned-Container {
    $owner = docker container inspect --format '{{ index .Config.Labels "org.worldalerts.instance" }}' $instance 2>$null
    if ($LASTEXITCODE -ne 0) { return $false }
    if ($owner -cne $instance) { throw "Refusing to modify an unowned container: $instance" }
    return $true
}
function Stop-Owned {
    Stop-DevServer
    if (Owned-Container) {
        $running = docker container inspect --format '{{ .State.Running }}' $instance
        Assert-NativeExit 'Container inspection'
        if ($running -eq 'true') {
            docker container stop $instance
            Assert-NativeExit 'Docker container stop'
        }
    } else { Write-Host "No container to stop: $instance" }
}
function Build-Image {
    docker build --no-cache -f $dockerfile -t $image $root
    Assert-NativeExit 'Docker build / tests'
}
function Check-Env {
    Ensure-Env
    $script:config = (Resolve-Path -LiteralPath $config).Path
}
function Create-Volumes {
    $missing = @()
    foreach ($name in @($volume, $keys)) {
        $owner = docker volume inspect --format '{{ index .Labels "org.worldalerts.instance" }}' $name 2>$null
        if ($LASTEXITCODE -eq 0) {
            if ($owner -cne $instance) { throw "Refusing an unowned Docker volume: $name" }
        } else { $missing += $name }
    }
    foreach ($name in $missing) {
        docker volume create --label $label $name
        Assert-NativeExit 'Docker volume creation'
    }
}
function Replace-Instance {
    Check-Env
    docker image inspect $image *> $null
    Assert-NativeExit 'Missing Docker image. Run build first'
    Stop-DevServer
    # Never delete a persistent volume; always stop and replace stale owned containers.
    if (Owned-Container) {
        Stop-Owned
        docker container rm $instance
        Assert-NativeExit 'Docker container removal'
    }
    Create-Volumes
    docker run -d --name $instance --label $label --env-file $config `
        -e 'ASPNETCORE_URLS=http://+:8080' `
        -e 'WorldAlerts__LocalHttp=true' `
        -e "WorldAlerts__Admin__PasswordHash=$demoHash" `
        -e 'WorldAlerts__DatabasePath=/app/data/worldalerts.sqlite' `
        -p "127.0.0.1:${port}:8080" -v "${volume}:/app/data" `
        -v "${keys}:/home/app/.aspnet/DataProtection-Keys" --restart unless-stopped $image
    Assert-NativeExit 'Docker start'
    Write-Host "Started: http://127.0.0.1:$port/"
}

switch ($Action) {
    'build' { Build-Image }
    'start' { Replace-Instance }
    'stop' { Stop-Owned }
    'restart' { Replace-Instance }
    'status' {
        if (Owned-Container) {
            docker container inspect --format '{{ .Name }}: running={{ .State.Running }} status={{ .State.Status }}' $instance
            Assert-NativeExit 'Docker container status'
        } else { Write-Host "Not created: $instance" }
    }
    'logs' {
        if (Owned-Container) { docker logs --tail 100 $instance; Assert-NativeExit 'Docker logs' }
        else { throw "No container named $instance" }
    }
    'up' { Check-Env; Build-Image; Replace-Instance }
}
