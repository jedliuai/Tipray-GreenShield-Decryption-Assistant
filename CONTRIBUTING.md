# 参与贡献

欢迎提交能够提高兼容性、可验证性或使用体验的 Issue 和 Pull Request。

## 报告问题前

请先运行：

```text
LdDecryptHotkeyCli.exe --probe-direct
LdDecryptHotkeyCli.exe --status
```

提交 Issue 时请说明 Windows 版本、绿盾客户端版本、目标是文件还是文件夹、使用 F8 还是 MCP，以及实际结果和预期结果。日志中可能包含本地路径；公开前请删除组织名、用户名、文件名等敏感信息。

## 代码贡献原则

- 保留绿盾官方权限与审批链，不接受绕过、伪造或重放服务器通信的实现。
- 不把“成功发起申请”当作“文件已经解密”。
- 文件夹扫描必须有数量边界，并跳过重解析点。
- 新功能应同时更新 README、版本号和相关构建或安装说明。
- 提交前至少运行一次 `build-hotkey-tool.bat`，并使用只读诊断命令验证产物。

安全问题请按照 [SECURITY.md](SECURITY.md) 私下报告。
