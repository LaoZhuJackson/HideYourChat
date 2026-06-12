# 替代 wix heat dir，生成 PublishedComponents.wxs
param(
    [string]$PublishDir  = "publish",
    [string]$OutputFile  = "installer/PublishedComponents.wxs"
)

$publishPath = (Resolve-Path $PublishDir).Path
$files = Get-ChildItem -Path $publishPath -Recurse -File | Sort-Object FullName

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="PublishedComponents" Directory="INSTALLFOLDER">')

foreach ($file in $files) {
    $rel    = $file.FullName.Substring($publishPath.Length).TrimStart('\','/')
    $safeId = "comp_" + ($rel -replace '[^a-zA-Z0-9]', '_')
    $guid   = [guid]::NewGuid().ToString().ToUpper()
    $subdir = [System.IO.Path]::GetDirectoryName($rel)

    if ($subdir) {
        [void]$sb.AppendLine("      <Component Id=""$safeId"" Guid=""$guid""
Subdirectory=""$subdir"">")
    } else {
        [void]$sb.AppendLine("      <Component Id=""$safeId"" Guid=""$guid"">")
    }
    [void]$sb.AppendLine("        <File Source=""$(Join-Path $publishPath $rel)"" />")
    [void]$sb.AppendLine("      </Component>")
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($OutputFile),
    $sb.ToString(),
    [System.Text.UTF8Encoding]::new($true)
)
Write-Host "Generated $($files.Count) components → $OutputFile"