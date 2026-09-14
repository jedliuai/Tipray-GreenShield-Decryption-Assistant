# Codex 使用说明

这是天锐绿盾 F8 快速申请解密工具。普通用户使用托盘版；自动化、诊断和人工核对使用 CLI 版。

## 项目路径

```text
D:\Documents\GitHub\绿盾解密\GreenShieldQuickApply
```

## 程序入口

- 托盘版：`LdDecryptHotkey.exe`
- 命令行版：`LdDecryptHotkeyCli.exe`
- 构建脚本：`build-hotkey-tool.bat`
- 日志：`LdDecryptHotkey.log`

## 常用命令

```powershell
& ".\LdDecryptHotkeyCli.exe" --status
& ".\LdDecryptHotkeyCli.exe" --list-selected
& ".\LdDecryptHotkeyCli.exe" --probe-direct
& ".\LdDecryptHotkeyCli.exe" --prepare-once
& ".\LdDecryptHotkeyCli.exe" --once
```

- `--list-selected` 只检查当前选中的路径。
- `--probe-direct` 只检查本地插件和策略，不会发申请。
- `--prepare-once` 会打开官方申请窗口，但不会发送。
- `--once` 会执行完整申请并自动发送，运行前必须获得用户明确授权。

## 当前实现

程序直接调用本机绿盾组件：

```text
C:\Inetpub\ftproot\Tipray\LdTerm\LdMenuPlug.dll
```

当前源码不再保留右键菜单模拟点击流程。旧实现仍可从 Git 提交 `47d4f68` 查看或恢复，不要重写历史。

资源管理器与桌面均支持文件、文件夹以及二者混合多选。无法确定选中项时必须停止，不能复用其他窗口中的旧选择。会发送申请的 `--once` 不允许自动回退到其他资源管理器窗口；自动化调用时应显式传入 HWND。

## 维护注意事项

- 不要要求用户安装 AutoHotkey。
- 不要要求用户修改 Windows 11 右键菜单。
- 不要以透明复制作为默认方案：受控扩展名写回后会被立即重新加密。
- 排查前先运行 `--probe-direct` 和 `--list-selected`，再查看日志中的 `source:`、`target:`、`direct signal sent`。
- 不要通过 CLI 测试 `--once`，除非用户确实希望发送申请；优先用 `--prepare-once`。
- 绿盾升级后要重新验证 DLL 路径、导出函数、菜单类型和命令结构。
- 发布新功能时递增版本，不覆盖已有 Release。

## GitHub

- 仓库：https://github.com/jedliuai/Lvdun-Auto-Decryption
- Releases：https://github.com/jedliuai/Lvdun-Auto-Decryption/releases
