## unity-lua-device-debug

Unity Lua 运行时诊断的 HTTP+JSON 传输层（端口 21894，固定无回退），覆盖 Editor 与
Android Development Build。Agent 用 client.py：ping/commands/execute、adb-forward/
adb-remove。只暴露项目注册过的 Lua 调试命令，无任意 Lua eval、无 C# 反射 eval、
无 HybridCLR。前置：目标工程须安装本包并注册 ILuaDeviceDebugHost 适配器，否则无命令可用。
