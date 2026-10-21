$ErrorActionPreference = 'Stop'

$dockerfile = Join-Path $PSScriptRoot 'src/container/Dockerfile'
$config = if ($env:WORLDALERTS_ENV_FILE) { $env:WORLDALERTS_ENV_FILE } else { Join-Path $PSScriptRoot '.env' }
$instance = if ($env:WORLDALERTS_INSTANCE) { $env:WORLDALERTS_INSTANCE } else { 'worldalerts-local' }
$port = if ($env:WORLDALERTS_PORT) { $env:WORLDALERTS_PORT } else { '8080' }

if ($instance -cnotmatch '^[a-z0-9][a-z0-9_.-]*$' -or $port -notmatch '^[0-9]+$' -or
    $port.Length -gt 5 -or [int]$port -lt 1 -or [int]$port -gt 65535) {
    [Console]::Error.WriteLine('Invalid WORLDALERTS_INSTANCE or WORLDALERTS_PORT')
    exit 1
}

if (-not (Test-Path -LiteralPath $config -PathType Leaf)) {
    [Console]::Error.WriteLine("Private configuration missing: $config (see docs/startup.md)")
    exit 1
}

$config = (Resolve-Path -LiteralPath $config).Path
$volume = "$instance-data"
$image = "${instance}:verified"
$label = "org.worldalerts.instance=$instance"

# Same Dockerfile build/test/run path as run.sh. Explicitly check native exit
# codes: Windows PowerShell does not turn failed native commands into throws.
docker build --no-cache -f $dockerfile -t $image $PSScriptRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$owner = docker volume inspect --format '{{ index .Labels "org.worldalerts.instance" }}' $volume 2>$null
if ($LASTEXITCODE -eq 0) {
    if ($owner -cne $instance) {
        [Console]::Error.WriteLine("Refusing to use a volume not owned by this instance: $volume")
        exit 1
    }
} else {
    docker volume create --label $label $volume
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$owner = docker container inspect --format '{{ index .Config.Labels "org.worldalerts.instance" }}' $instance 2>$null
if ($LASTEXITCODE -eq 0) {
    if ($owner -cne $instance) {
        [Console]::Error.WriteLine("Refusing to replace a container not owned by this instance: $instance")
        exit 1
    }
    docker container stop $instance
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    docker container rm $instance
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

docker run -d --name $instance --label $label --env-file $config `
    -e 'ASPNETCORE_URLS=http://+:8080' -e 'WorldAlerts__DatabasePath=/app/data/worldalerts.sqlite' `
    -p "127.0.0.1:${port}:8080" -v "${volume}:/app/data" --restart unless-stopped $image
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
