param(
    [int]$Port = 50051,
    [int]$Seed = 42,
    [int]$RoundSeconds = 15
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$server = $null
$names = @('ARENA_SEEKER_TOKEN', 'ARENA_HIDER_TOKEN', 'ARENA_REFEREE_TOKEN')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $repo
try {
    $logFolder = Join-Path $repo 'artifacts'
    New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
    $buildLog = Join-Path $logFolder 'match-build.log'
    dotnet build Arena.slnx --configuration Release --verbosity quiet *> $buildLog
    if ($LASTEXITCODE -ne 0) { Get-Content $buildLog; throw 'Build failed.' }
    Write-Host "Build complete. Full build output and dependency audit warnings: $buildLog"
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, [Guid]::NewGuid().ToString('N'), 'Process') }
    $serverDll = Join-Path $repo 'src/Arena.Demo/bin/Release/net10.0/Arena.Demo.dll'
    $serverLog = Join-Path $logFolder 'match-server.log'
    $errorLog = Join-Path $logFolder 'match-server.err.log'
    $server = Start-Process -FilePath 'dotnet' -ArgumentList @(
        ('"' + $serverDll + '"'), '--headless', '--manual', '--no-bots', '--port', $Port,
        '--round-seconds', $RoundSeconds, '--exit-after-frames', ($RoundSeconds * 20)
    ) -PassThru -RedirectStandardOutput $serverLog -RedirectStandardError $errorLog
    $null = $server.Handle # Keep a process handle so Windows PowerShell retains ExitCode after exit.

    # Wait for this process's readiness marker, never attach to a different server on the port.
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ($true) {
        if ($server.HasExited) { throw "Server exited. See $serverLog and $errorLog" }
        if ((Test-Path $serverLog) -and (Select-String -Path $serverLog -Pattern 'Arena ready' -Quiet)) { break }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Server startup timed out.' }
        Start-Sleep -Milliseconds 100
    }
    dotnet run --no-build --configuration Release --project samples/Arena.Bot -- --match --url "http://127.0.0.1:$Port" --seed $Seed
    if ($LASTEXITCODE -ne 0) { throw 'The gRPC match did not finish successfully.' }
    if (-not $server.WaitForExit(15000)) { throw 'Server did not shut down after the round.' }
    if ($server.ExitCode -ne 0) { throw "Server exited with code $($server.ExitCode)." }
    Get-Content -Path $serverLog | Select-String -Pattern 'Backend:|GPU observations:'
    Write-Host 'PASS: separate server and agent processes completed a round over gRPC.'
}
finally {
    if ($null -ne $server -and -not $server.HasExited) { Stop-Process -Id $server.Id }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    Pop-Location
}
