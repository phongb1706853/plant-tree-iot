# Smoke test end-to-end cho auth (JWT + ownership + chia sẻ device).
# ESP32 dùng MQTT (HiveMQ) -> KHÔNG còn device secret; endpoint HTTP dùng JWT (owner/member).
# Yêu cầu: đã `dotnet build`, MongoDB chạy ở localhost:27017.
# Chạy:  powershell -ExecutionPolicy Bypass -File .\smoke-test-auth.ps1
$ErrorActionPreference = 'Stop'
$exe  = Join-Path $PSScriptRoot 'PlantTreeIoTServer\bin\Debug\net10.0\PlantTreeIoTServer.exe'
$log  = Join-Path $env:TEMP 'pt-smoke.out.log'
$elog = Join-Path $env:TEMP 'pt-smoke.err.log'
$base = 'http://localhost:8000'
$rand = Get-Random -Maximum 999999

if (-not (Test-Path $exe)) { Write-Host "EXE not found (chay 'dotnet build' truoc): $exe"; exit 1 }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
Remove-Item Env:MQTT_BROKER -ErrorAction SilentlyContinue
Remove-Item Env:JWT_SECRET  -ErrorAction SilentlyContinue
if (Test-Path $log)  { Remove-Item $log  -Force }
if (Test-Path $elog) { Remove-Item $elog -Force }

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

  $rb = Invoke-Api POST '/api/auth/register' @{} (@{ email=$emailB; password='pass1234'; displayName='B' } | ConvertTo-Json -Compress)
  $tokenB = try { ($rb.Body | ConvertFrom-Json).token } catch { $null }
  Check '2. Register user B' ($rb.Status -eq 200 -and $tokenB) "status=$($rb.Status)"

  Check '3. GET /api/devices no token -> 401' ((Invoke-Api GET '/api/devices').Status -eq 401) '401'

  # Owner A đăng ký device — response KHÔNG còn deviceSecret
  $reg = Invoke-Api POST '/api/devices/register' @{ Authorization="Bearer $tokenA" } (@{ deviceId=$dev; name='X' } | ConvertTo-Json -Compress)
  Check '4. Register device (no deviceSecret in response)' (($reg.Status -eq 201 -or $reg.Status -eq 200) -and ($reg.Body -notmatch 'deviceSecret')) "status=$($reg.Status)"

  $get = Invoke-Api GET "/api/devices/$dev" @{ Authorization="Bearer $tokenA" }
  Check '5. Owner GET device -> 200, no secret hash' ($get.Status -eq 200 -and ($get.Body -notmatch 'deviceSecretHash')) "status=$($get.Status)"

  Check '6. Owner upload sensor (JWT) -> 200' ((Invoke-Api POST '/api/sensordata/upload' @{ Authorization="Bearer $tokenA" } (@{ deviceId=$dev; soilMoisture=20 } | ConvertTo-Json -Compress)).Status -eq 200) 'owner control'

  # Trước khi chia sẻ: B không truy cập được device của A
  Check '7. B GET A device (chua share) -> 404' ((Invoke-Api GET "/api/devices/$dev" @{ Authorization="Bearer $tokenB" }).Status -eq 404) '404'
  Check '8. B upload to A device (chua share) -> 404' ((Invoke-Api POST '/api/sensordata/upload' @{ Authorization="Bearer $tokenB" } (@{ deviceId=$dev; soilMoisture=30 } | ConvertTo-Json -Compress)).Status -eq 404) '404'

  # A chia sẻ device cho B
  $share = Invoke-Api POST "/api/devices/$dev/share" @{ Authorization="Bearer $tokenA" } (@{ email=$emailB } | ConvertTo-Json -Compress)
  Check '9. Owner share device cho B -> 200' ($share.Status -eq 200) "status=$($share.Status)"

  # Sau khi chia sẻ: B xem + điều khiển được
  Check '10. B GET shared device -> 200 (member)' ((Invoke-Api GET "/api/devices/$dev" @{ Authorization="Bearer $tokenB" }).Status -eq 200) 'member view'
  Check '11. B upload shared device -> 200 (member)' ((Invoke-Api POST '/api/sensordata/upload' @{ Authorization="Bearer $tokenB" } (@{ deviceId=$dev; soilMoisture=40 } | ConvertTo-Json -Compress)).Status -eq 200) 'member control'
  Check '12. B send command shared device -> 200 (member)' ((Invoke-Api POST '/api/control/commands' @{ Authorization="Bearer $tokenB" } (@{ deviceId=$dev; command='WATER_ON' } | ConvertTo-Json -Compress)).Status -eq 200) 'member control'
  Check '13. B thay B trong shared device -> KHONG xoa duoc (owner-only)' ((Invoke-Api DELETE "/api/devices/$dev" @{ Authorization="Bearer $tokenB" }).Status -eq 404) 'owner-only delete'

  # members list -> lấy id của B để thu hồi
  $mem = Invoke-Api GET "/api/devices/$dev/members" @{ Authorization="Bearer $tokenA" }
  $bId = try { (($mem.Body | ConvertFrom-Json).members)[0].id } catch { $null }
  Check '14. GET members -> co 1 member' ($mem.Status -eq 200 -and $bId) "memberId=$bId"

  # Thu hồi chia sẻ
  Check '15. Owner unshare B -> 200' ((Invoke-Api DELETE "/api/devices/$dev/share/$bId" @{ Authorization="Bearer $tokenA" }).Status -eq 200) 'revoke'
  Check '16. B GET device sau thu hoi -> 404' ((Invoke-Api GET "/api/devices/$dev" @{ Authorization="Bearer $tokenB" }).Status -eq 404) 'revoked'

  Check '17. Login sai password -> 401' ((Invoke-Api POST '/api/auth/login' @{} (@{ email=$emailA; password='wrong' } | ConvertTo-Json -Compress)).Status -eq 401) '401'
}
finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}
$pass = ($results | Where-Object { $_.Pass }).Count
Write-Host ("`n===== {0}/{1} PASS =====" -f $pass, $results.Count)
if ($pass -ne $results.Count -and (Test-Path $elog)) { Get-Content $elog -Tail 15 }
