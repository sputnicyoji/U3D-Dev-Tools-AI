param(
    [switch]$StayOpen,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ArgsList
)

$ErrorActionPreference = 'Stop'
# 必须用 -InputObject 传整段参数数组；管道会把单元素序列化成 JSON 字符串，导致 Python 侧 sys.argv 拼接失败。
$argArray = @(foreach ($a in $ArgsList) { $a })

if ($StayOpen) {
    # 新开与当前宿主同款的 PowerShell 窗口，带 -NoExit：子进程不再传 -StayOpen，避免递归；执行完脚本后窗口保留，便于查看 JSON 输出。
    $hostExe = (Get-Process -Id $PID).Path
    $childArgs = [System.Collections.Generic.List[string]]::new()
    $childArgs.Add('-NoExit')
    $childArgs.Add('-ExecutionPolicy')
    $childArgs.Add('Bypass')
    $childArgs.Add('-File')
    $childArgs.Add($PSCommandPath)
    foreach ($a in $argArray) {
        $childArgs.Add($a)
    }
    Start-Process -FilePath $hostExe -ArgumentList $childArgs -WorkingDirectory $PSScriptRoot | Out-Null
    exit 0
}

$env:PYTHONIOENCODING = 'utf-8'
$env:FEVAL_SCRIPT_DIR = $PSScriptRoot
$env:FEVAL_RUNTIME_DEBUG_ARGS = if ($argArray.Count -eq 0) { '[]' } else { ConvertTo-Json -Compress -InputObject $argArray }

$pythonBootstrap = @'
import importlib.util
import json
import os
import sys
from pathlib import Path

script_dir = Path(os.environ.get("FEVAL_SCRIPT_DIR", "."))
script_path = (script_dir / "feval_runtime_debug.py").resolve()
spec = importlib.util.spec_from_file_location("feval_runtime_debug", script_path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
sys.argv = [str(script_path)] + json.loads(os.environ["FEVAL_RUNTIME_DEBUG_ARGS"])
raise SystemExit(module.main())
'@

$pythonBootstrap | python -
