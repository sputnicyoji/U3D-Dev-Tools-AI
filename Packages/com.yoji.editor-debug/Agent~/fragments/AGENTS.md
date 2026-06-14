## unity-editor-debug-mcp

Unity Editor 在线时通过 HTTP+JSON 反射调用任意 Unity API 的调试服务（端口 21891，
被占回退 21892/21893）。Agent 用 client.py 发请求。核心端点：/invoke 反射调用、
/describe 列成员、/console 读真实日志条目、/ping 暴露编辑器态（isPlaying/isCompiling）、
/batch 合并多次 invoke。object 引用用 EntityId 十进制字符串，兼容 Unity 6.4+。
