## test-runner-mcp

Unity Editor 在线时跑 EditMode 测试的 HTTP 服务（端口 21890）。流程：/ping 确认 Idle
-> /recompile 等编译 -> POST /run-tests 拿 jobId -> 轮询 /test-status。client.py 全局
flag 放子命令前。阶段 1 仅 EditMode（PlayMode 返 400）。空 testNames = 跑全套件。
