# 只截 Game 视图：png 与帧号旁注（<NNN>-<label>.frame.json）用下一个空编号，默认不覆盖。
# 用法：.\shot.ps1 -Dir <证据目录> -Label "panel open" -Project <Unity 工程根> [-EditorPid N] [-Seq N] [-Force]
param(
    [Parameter(Mandatory = $true)] [string] $Dir,
    [Parameter(Mandatory = $true)] [string] $Label,
    [Parameter(Mandatory = $true)] [string] $Project,
    [int] $EditorPid = 0,
    [int] $Seq = 0,
    [switch] $Force,
    [double] $Timeout = 40
)
$py = @((Join-Path $PSScriptRoot 'evidence.py'), 'shot', '--dir', $Dir, '--label', $Label, '--project', $Project,
        '--timeout', $Timeout)
if ($EditorPid -gt 0) { $py += @('--pid', $EditorPid) }
if ($Seq -gt 0) { $py += @('--seq', $Seq) }
if ($Force) { $py += '--force' }
& python @py
exit $LASTEXITCODE