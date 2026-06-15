## test-runner-mcp

Unity Editor 在线时跑 EditMode/PlayMode 测试的 HTTP 服务（端口 21890）。流程：/ping 确认 Idle
-> /recompile 等编译 -> POST /run-tests 拿 jobId -> 轮询 /test-status。client.py 全局
flag 放子命令前。PlayMode 通过 DisableDomainReload 执行，结束后还原用户设置；已在 Play 中或有脏场景时返 409。空 testNames = 跑全套件。
