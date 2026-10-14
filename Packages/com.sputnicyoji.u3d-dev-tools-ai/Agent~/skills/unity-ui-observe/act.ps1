# act + 截图取证：结果 json、Game 视图 png 与帧号旁注用同一个编号，默认不覆盖已有证据。
# 用法：.\act.ps1 -Dir <证据目录> -Label "cell 498 503" -Project <Unity 工程根> [-EditorPid N] [-Seq N] [-Force] -World "--cell 498 503"
# 实现在 evidence.py（跨平台）；Mac 上直接 python3 evidence.py act ...
param(
    [Parameter(Mandatory = $true)] [string] $Dir,
    [Parameter(Mandatory = $true)] [string] $Label,
    [Parameter(Mandatory = $true)] [string] $Project,
    [int] $EditorPid = 0,
    [int] $Seq = 0,
    [switch] $Force,
    [double] $Settle = 2,
    [double] $Timeout = 40,
    # client.py world 的参数，整串传入（PowerShell 会把 --cell 当成本脚本的参数名）
    [Parameter(Mandatory = $true)] [string] $World
)
$py = @((Join-Path $PSScriptRoot 'evidence.py'), 'act', '--dir', $Dir, '--label', $Label, '--project', $Project,
        '--settle', $Settle, '--timeout', $Timeout)
if ($EditorPid -gt 0) { $py += @('--pid', $EditorPid) }
if ($Seq -gt 0) { $py += @('--seq', $Seq) }
if ($Force) { $py += '--force' }
$py += '--'
$py += ($World.Trim() -split '\s+')
& python @py
exit $LASTEXITCODE