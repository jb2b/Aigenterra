#requires -Version 7.0
#requires -RunAsAdministrator
param()
$ErrorActionPreference = 'Stop'
$repositoryPath = Split-Path $PSScriptRoot -Parent
$setupPath = Join-Path $env:LOCALAPPDATA 'Aigenterra\setup'
$installerPath = Join-Path $setupPath 'postgresql-17.11-3-windows-x64.exe'
$expectedHash = '2fd19749560be03020026f2d842b69af47f0ea2c7946bda17eed26a4a9235695'
$optionPath = Join-Path $setupPath 'installer-options.txt'
$adminCredentialPath = Join-Path $setupPath 'postgres-credential.xml'
$appCredentialPath = Join-Path $setupPath 'aigenterra-credential.xml'
$serviceName = 'postgresql-aigenterra-17'
$dataPath = 'C:\ProgramData\Aigenterra\PostgreSQL\17\data'
$installPath = 'C:\Program Files\PostgreSQL\17'

if (-not $IsWindows) { throw 'This service setup is for Windows.' }
New-Item -ItemType Directory -Path $setupPath -Force | Out-Null
$ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& icacls.exe $setupPath /inheritance:r /grant:r "*${ownerSid}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not protect the setup directory.' }
if (-not (Test-Path -LiteralPath $installerPath)) {
    Invoke-WebRequest 'https://get.enterprisedb.com/postgresql/postgresql-17.11-3-windows-x64.exe' -OutFile $installerPath
}
if ((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'PostgreSQL installer checksum mismatch.' }
foreach ($credentialPath in @($adminCredentialPath, $appCredentialPath)) {
    if (-not (Test-Path -LiteralPath $credentialPath)) {
        $username = if ($credentialPath -eq $adminCredentialPath) { 'postgres' } else { 'aigenterra' }
        $password = ConvertTo-SecureString ([Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))) -AsPlainText -Force
        [pscredential]::new($username, $password) | Export-Clixml -LiteralPath $credentialPath
    }
}
$adminCredential = Import-Clixml -LiteralPath $adminCredentialPath
$appCredential = Import-Clixml -LiteralPath $appCredentialPath

if (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
    if (Test-Path -LiteralPath $dataPath) { throw 'Existing data directory found; refusing to initialize or overwrite it.' }
    if (Test-Path -LiteralPath (Join-Path $installPath 'bin\postgres.exe')) { throw 'Another PostgreSQL 17 installation exists; configure it manually instead of overwriting it.' }
    if (Get-NetTCPConnection -LocalPort 5432 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 5432 is already in use.' }
    @(
        'mode=unattended', 'unattendedmodeui=none', 'enable-components=server,commandlinetools',
        'disable-components=pgAdmin,stackbuilder', "prefix=$installPath", "datadir=$dataPath", 'serverport=5432',
        "servicename=$serviceName", 'serviceaccount=NT AUTHORITY\NetworkService', 'superaccount=postgres',
        ('superpassword=' + $adminCredential.GetNetworkCredential().Password)
    ) | Set-Content -LiteralPath $optionPath
    try {
        $installer = Start-Process -FilePath $installerPath -ArgumentList @('--optionfile', ('"' + $optionPath + '"')) -WindowStyle Hidden -PassThru
        $installer.WaitForExit()
        if ($installer.ExitCode -ne 0) { throw 'PostgreSQL installer failed.' }
    } finally { Remove-Item -LiteralPath $optionPath -Force -ErrorAction SilentlyContinue }
}
Set-Service -Name $serviceName -StartupType Automatic
Start-Service -Name $serviceName

function Invoke-SetupSql([string] $sql) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $installPath 'bin\psql.exe'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($arg in @('-X', '-h', '127.0.0.1', '-U', 'postgres', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-tA')) { $start.ArgumentList.Add($arg) }
    $start.Environment['PGPASSWORD'] = $adminCredential.GetNetworkCredential().Password
    $sqlProcess = [Diagnostics.Process]::Start($start)
    try {
        $stderr = $sqlProcess.StandardError.ReadToEndAsync()
        $stdout = $sqlProcess.StandardOutput.ReadToEndAsync()
        $sqlProcess.StandardInput.WriteLine($sql)
        $sqlProcess.StandardInput.Close()
        $sqlProcess.WaitForExit()
        $result = $stdout.GetAwaiter().GetResult()
        $null = $stderr.GetAwaiter().GetResult()
        if ($sqlProcess.ExitCode -ne 0) { throw 'PostgreSQL setup SQL failed; credentials were not logged.' }
        return $result.Trim()
    } finally { $sqlProcess.Dispose() }
}
if ((Invoke-SetupSql "SELECT count(*) FROM pg_roles WHERE rolname='aigenterra';") -eq '0') {
    $rolePassword = $appCredential.GetNetworkCredential().Password
    $null = Invoke-SetupSql "CREATE ROLE aigenterra LOGIN PASSWORD '$rolePassword';"
}
if ((Invoke-SetupSql "SELECT count(*) FROM pg_database WHERE datname='aigenterra';") -eq '0') {
    $null = Invoke-SetupSql 'CREATE DATABASE aigenterra OWNER aigenterra;'
}
$null = Invoke-SetupSql "ALTER SYSTEM SET listen_addresses = 'localhost';"
Restart-Service -Name $serviceName

# Run this script elevated as the same Windows user who runs AppHost.
$connection = 'Host=127.0.0.1;Port=5432;Database=aigenterra;Username=aigenterra;Password=' + $appCredential.GetNetworkCredential().Password
@{ 'ConnectionStrings:aigenterra' = $connection; 'Database:Mode' = 'External' } | ConvertTo-Json -Compress |
    & dotnet user-secrets set --project (Join-Path $repositoryPath 'src\Aigenterra.AppHost') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Database ready, but writing AppHost user secrets failed.' }
Write-Output "PostgreSQL service ready: $serviceName. AppHost user secrets configured. Data: $dataPath"
