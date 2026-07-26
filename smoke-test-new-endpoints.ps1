# Smoke test cho các endpoint MỚI: /api/auth/dev-token, /api/control/{id}/water|light|auto,
# /api/assistant/v1/chat/completions. Kiểm tra routing + auth + validation + xử lý lỗi.
#
# Yêu cầu: đã `dotnet build`, MongoDB chạy ở localhost:27017.
# KHÔNG cần MQTT broker (để trống -> /water|/light|/auto trả 503 sau khi qua auth+ownership).
# KHÔNG cần AI server (để tắt -> /assistant/* trả 503 = xử lý lỗi đúng). Muốn test happy-path
# của trợ lý: chạy AI server (tree-grow-helper) ở http://localhost:8787 rồi gọi tay.
#
# Chạy:  powershell -ExecutionPolicy Bypass -File .\smoke-test-new-endpoints.ps1
$ErrorActionPreference = 'Stop'
$exe  = Join-Path $PSScriptRoot 'PlantTreeIoTServer\bin\Debug\net10.0\PlantTreeIoTServer.exe'
$log  = Join-Path $env:TEMP 'pt-smoke-new.out.log'
$elog = Join-Path $env:TEMP 'pt-smoke-new.err.log'
$base = 'http://localhost:8000'
$rand = Get-Random -Maximum 999999

if (-not (Test-Path $exe)) { Write-Host "EXE not found (chay 'dotnet build' truoc): $exe"; exit 1 }

$env:ASPNETCORE_ENVIRONMENT = 'Development'   # bat dev-token + dev JWT fallback
Remove-Item Env:MQTT_BROKER -ErrorAction SilentlyContinue   # tat MQTT
Remove-Item Env:JWT_SECRET  -ErrorAction SilentlyContinue
Remove-Item Env:AI_SERVER_URL -ErrorAction SilentlyContinue # AI server khong chay -> 503
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

  $dev = "smoke-new-$rand"; $other = "smoke-other-$rand"

  # --- C. dev-token (Development) ---
  $rt = Invoke-Api POST '/api/auth/dev-token' @{} $null
  $token = try { ($rt.Body | ConvertFrom-Json).token } catch { $null }
  Check '1. dev-token -> 200 + token' ($rt.Status -eq 200 -and $token) "status=$($rt.Status)"

  Check '2. GET /api/devices with dev token -> 200' ((Invoke-Api GET '/api/devices' @{ Authorization="Bearer $token" }).Status -eq 200) '200'

  # Device thuoc so huu dev user
  $rr = Invoke-Api POST '/api/devices/register' @{ Authorization="Bearer $token" } (@{ deviceId=$dev; name='Smoke New' } | ConvertTo-Json -Compress)
  Check '3. Register device (owned by dev)' ($rr.Status -eq 201 -or $rr.Status -eq 200) "status=$($rr.Status)"

  # --- A. Control chuyen dung ---
  Check '4. /water no token -> 401' ((Invoke-Api POST "/api/control/$dev/water" @{} (@{ on=$true } | ConvertTo-Json -Compress)).Status -eq 401) '401'
  Check '5. /water non-owned device -> 404' ((Invoke-Api POST "/api/control/$other/water" @{ Authorization="Bearer $token" } (@{ on=$true } | ConvertTo-Json -Compress)).Status -eq 404) '404'

  # /light thieu ca on lan pwm -> 400 (validation, chua toi publish nen khong can MQTT)
  Check '6. /light empty body -> 400' ((Invoke-Api POST "/api/control/$dev/light" @{ Authorization="Bearer $token" } '{}').Status -eq 400) '400 (thieu on/pwm)'

  # Owned device + MQTT off -> 503 (da qua auth+ownership, toi buoc publish). Neu MQTT bat -> 200.
  $w = Invoke-Api POST "/api/control/$dev/water" @{ Authorization="Bearer $token" } (@{ on=$true } | ConvertTo-Json -Compress)
  Check '7. /water owned -> 503 (MQTT off) hoac 200' ($w.Status -eq 503 -or $w.Status -eq 200) "status=$($w.Status)"
  $l = Invoke-Api POST "/api/control/$dev/light" @{ Authorization="Bearer $token" } (@{ pwm=180 } | ConvertTo-Json -Compress)
  Check '8. /light pwm owned -> 503/200' ($l.Status -eq 503 -or $l.Status -eq 200) "status=$($l.Status)"
  $a = Invoke-Api POST "/api/control/$dev/auto" @{ Authorization="Bearer $token" } $null
  Check '9. /auto owned -> 503/200' ($a.Status -eq 503 -or $a.Status -eq 200) "status=$($a.Status)"

  # --- B. Assistant proxy (OpenAI-compatible /v1/chat/completions) ---
  $cc = '/api/assistant/v1/chat/completions'
  $msg = @{ messages = @(@{ role='user'; content='Do am dat bao nhieu?' }) } | ConvertTo-Json -Depth 5 -Compress
  Check '10. /chat/completions no token -> 401' ((Invoke-Api POST $cc @{} $msg).Status -eq 401) '401'
  Check '11. /chat/completions empty messages -> 400' ((Invoke-Api POST $cc @{ Authorization="Bearer $token" } (@{ messages=@() } | ConvertTo-Json -Compress)).Status -eq 400) '400'
  # AI server khong chay -> 503 (AiServerUnavailableException)
  $ch = Invoke-Api POST $cc @{ Authorization="Bearer $token" } $msg
  Check '12. /chat/completions AI down -> 503' ($ch.Status -eq 503) "status=$($ch.Status)"
}
finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}
$pass = ($results | Where-Object { $_.Pass }).Count
Write-Host ("`n===== {0}/{1} PASS =====" -f $pass, $results.Count)
if ($pass -ne $results.Count) { exit 1 }
