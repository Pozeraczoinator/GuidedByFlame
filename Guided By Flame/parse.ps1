$json = Get-Content lhm_test.json -Raw -Encoding Unicode
$matches = [regex]::Matches($json, '"Text":"([^"]+)".*?"Value":"([^"]+)"')
foreach ($m in $matches) {
    Write-Output ($m.Groups[1].Value + ": " + $m.Groups[2].Value)
}
