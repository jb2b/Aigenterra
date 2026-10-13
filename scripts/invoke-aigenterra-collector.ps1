try {
    $collectorPath = Join-Path $env:LOCALAPPDATA 'Aigenterra\collector\Aigenterra.Collector.exe'
    if (-not (Test-Path -LiteralPath $collectorPath -PathType Leaf)) {
        Write-Output '{}'
        exit 0
    }

    & $collectorPath
    exit 0
}
catch {
    Write-Output '{}'
    exit 0
}
