# Smoke test end-to-end cho auth (JWT + device token + ownership).
# Yêu cầu: đã `dotnet build`, MongoDB chạy ở localhost:27017.
# Chạy:  powershell -ExecutionPolicy Bypass -File .\smoke-test-auth.ps1
$ErrorActionPreference = 'Stop'
$exe  = Join-Path $PSScriptRoot 'PlantTreeIoTServer\bin\Debug\net10.0\PlantTreeIoTServer.exe'
$log  = Join-Path $env:TEMP 'pt-smoke.out.log'
$elog = Join-Path $env:TEMP 'pt-smoke.err.log'
$base = 'http://localhost:8000'
$rand = Get-Random -Maximum 999999

if (-not (Test-Path $exe)) { Write-Host "EXE not found (chay 'dotnet build' truoc): $exe"; exit 1 }

$env:ASPNETCORE_ENVIRONMENT = 'Development'   # dev JWT fallback; Production phai dat JWT_SECRET
Remove-Item Env:MQTT_BROKER -ErrorAction SilentlyContinue   # tat MQTT cho test
Remove-Item Env:JWT_SECRET  -ErrorAction SilentlyContinue
if (Test-Path $log)  { Remove-Item $log  -Force }
if (Test-Path $elog) { Remove-Item $elog -Force }

# QUAN TRONG: -WorkingDirectory = thu muc exe de doc appsettings.json
$proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent) -RedirectStandardOutput $log -RedirectStandardError $elog -PassThru -WindowStyle Hidden

function Invoke-Api {
  param($Method, $Path, [hashtable]$Headers = @{}, $Body = $null)
  $ca = @('-s', '-X', $Method, "$base$Path", '-w', 'HTTPSTATUS:%{http_code}')
  foreach ($k in $Headers.Keys) { $ca += @('-H', "${k}: $($Headers[$k])") }
  $tmp = $null
  if ($null -ne $Body) {
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $Body, (New-Object System.Text.UTF8Encoding($false)))
    $ca += @('-H', 'Content-Type: application/json', '--data-binary', "@$tmp")
  }
  $raw = (& curl.exe @ca) -join "`n"
  if ($tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
  $status = 0; $bodyText = $raw
  if ($raw -match 'HTTPSTATUS:(\d+)\s*$') { $status = [int]$Matches[1]; $bodyText = ($raw -replace 'HTTPSTATUS:\d+\s*$','').Trim() }
  return [pscustomobject]@{ Status = $status; Body = $bodyText }
}

$results = @()
function Check($name, $cond, $detail) {
  $script:results += [pscustomobject]@{ Pass = [bool]$cond }
  Write-Host ("[{0}] {1} -- {2}" -f $(if ($cond) {'PASS'} else {'FAIL'}), $name, $detail)
}

try {
  $ok = $false
  for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 1000; if ($proc.HasExited) { break }; if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'Now listening' -Quiet)) { $ok = $true; break } }
  Check 'Server starts' $ok "exited=$($proc.HasExited)"
  if (-not $ok) { if (Test-Path $elog) { Get-Content $elog -Tail 30 }; return }

  $emailA = "smoke_a_$rand@test.local"; $emailB = "smoke_b_$rand@test.local"; $dev = "smoke-dev-$rand"

  $ra = Invoke-Api POST '/api/auth/register' @{} (@{ email=$emailA; password='pass1234'; displayName='A' } | ConvertTo-Json -Compress)
  $tokenA = try { ($ra.Body | ConvertFrom-Json).token } catch { $null }
  Check '1. Register user A' ($ra.Status -eq 200 -and $tokenA) "status=$($ra.Status)"

  Check '2. GET /api/devices no token -> 401' ((Invoke-Api GET '/api/devices').Status -eq 401) '401'
  Check '3. GET /api/devices with token -> 200' ((Invoke-Api GET '/api/devices' @{ Authorization="Bearer $tokenA" }).Status -eq 200) '200'

  $r4 = Invoke-Api POST '/api/devices/register' @{ Authorization="Bearer $tokenA" } (@{ deviceId=$dev; name='Smoke Device' } | ConvertTo-Json -Compress)
  $secret = try { ($r4.Body | ConvertFrom-Json).deviceSecret } catch { $null }
  Check '4. Register device + secret' (($r4.Status -eq 201 -or $r4.Status -eq 200) -and $secret) "status=$($r4.Status)"

  $r5 = Invoke-Api GET "/api/devices/$dev" @{ Authorization="Bearer $tokenA" }
  Check '5. No deviceSecretHash leak' ($r5.Status -eq 200 -and ($r5.Body -notmatch 'deviceSecretHash')) '200'
  Check '6. Upload sensor (correct secret) -> 200' ((Invoke-Api POST '/api/sensordata/upload' @{ 'X-Device-Id'=$dev; 'X-Device-Secret'=$secret } (@{ deviceId=$dev; soilMoisture=20 } | ConvertTo-Json -Compress)).Status -eq 200) '200'
  Check '7. Upload (wrong secret) -> 401' ((Invoke-Api POST '/api/sensordata/upload' @{ 'X-Device-Id'=$dev; 'X-Device-Secret'='wrong' } (@{ deviceId=$dev } | ConvertTo-Json -Compress)).Status -eq 401) '401'

  $rb = Invoke-Api POST '/api/auth/register' @{} (@{ email=$emailB; password='pass1234'; displayName='B' } | ConvertTo-Json -Compress)
  $tokenB = try { ($rb.Body | ConvertFrom-Json).token } catch { $null }
  Check '8. User B cannot see A device -> 404' ((Invoke-Api GET "/api/devices/$dev" @{ Authorization="Bearer $tokenB" }).Status -eq 404) '404'
  Check '9. User B cannot command A device -> 404' ((Invoke-Api POST '/api/control/commands' @{ Authorization="Bearer $tokenB" } (@{ deviceId=$dev; command='WATER_ON' } | ConvertTo-Json -Compress)).Status -eq 404) '404'
  Check '10. Login wrong password -> 401' ((Invoke-Api POST '/api/auth/login' @{} (@{ email=$emailA; password='wrong' } | ConvertTo-Json -Compress)).Status -eq 401) '401'
}
finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}
$pass = ($results | Where-Object { $_.Pass }).Count
Write-Host ("`n===== {0}/{1} PASS =====" -f $pass, $results.Count)
