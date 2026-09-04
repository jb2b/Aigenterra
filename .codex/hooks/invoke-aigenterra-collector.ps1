try {
    $repositoryRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null).Trim()
    if ([string]::IsNullOrWhiteSpace($repositoryRoot)) {
        throw 'Repository root could not be resolved.'
    }

    $collectorPath = Join-Path $repositoryRoot 'src\Aigenterra.Collector\bin\Debug\net10.0\Aigenterra.Collector.exe'
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
