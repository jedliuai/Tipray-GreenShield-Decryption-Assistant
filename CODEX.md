# Codex 使用说明

这是绿盾一键申请解密工具。普通用户日常使用托盘版，Codex 自动化或排查问题时使用 CLI 版。

## 项目路径

```text
D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply
```

## 程序入口

托盘版主程序：

```text
D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply\LdDecryptHotkey.exe
```

CLI 程序：

```text
D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply\LdDecryptHotkeyCli.exe
```

## 给 Codex 的常用命令

查看状态：

```powershell
& "D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply\LdDecryptHotkeyCli.exe" --status
```

执行一次申请流程：

```powershell
& "D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply\LdDecryptHotkeyCli.exe" --once
```

设置开机自启：

```powershell
& "D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply\LdDecryptHotkeyCli.exe" --install-startup
```

取消开机自启：

```powershell
& "D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply\LdDecryptHotkeyCli.exe" --uninstall-startup
```

查看帮助：

```powershell
& "D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply\LdDecryptHotkeyCli.exe" --help
```

## 使用前提

执行 `--once` 前，需要用户先在 Windows 资源管理器里选中文件。

如果没有选中文件，或者绿盾右键菜单没有出现，命令可能会失败。

## 日志位置

```text
D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply\LdDecryptHotkey.log
```

排查问题时优先读取这个日志。

## 版本发布

当前 GitHub 仓库：

```text
https://github.com/jedliuai/Lvdun-Auto-Decryption
```

Release 下载页：

```text
https://github.com/jedliuai/Lvdun-Auto-Decryption/releases
```

## 注意事项

- 不要要求用户安装 AutoHotkey；本工具不依赖 AutoHotkey。
- 不要要求用户永久修改 Win11 右键菜单；程序内置兼容逻辑。
- 不要覆盖旧 Release；有新功能时递增版本号。
- 普通用户使用 `LdDecryptHotkey.exe`。
- Codex 使用 `LdDecryptHotkeyCli.exe`。
