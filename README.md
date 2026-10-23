# SphereDesk

SphereDesk 把用户桌面和公共桌面上的文件、文件夹、快捷方式排到壁纸上的球面。其他窗口盖住球面。程序不移动、不重命名、不删除真实文件。

## 环境

- 64 位 Windows，版本不低于 10.0.19041。
- 发布包自带 .NET 运行时，运行机器不必另装 SDK。
- 从源码编译时，使用仓库里 `global.json` 指定的 SDK 10.0.204。`rollForward` 为 `latestPatch`。

## 编译

在仓库根目录执行：

```powershell
dotnet build SphereDesk.sln -c Release
```

发布自包含压缩包和安装程序的步骤见 [构建说明](docs/build.md)。安装包未签名。

## 使用

- 启动后，球面铺在主显示器的桌面区域上，并隐藏原来的桌面图标。
- 默认热键 Ctrl+Alt+D 打开搜索、分组和设置。再按一次收起面板。
- 面板上的关闭只收起面板。托盘菜单里的「退出」才会结束程序，并恢复原来的桌面图标。
- 默认开机启动。可以在设置里关闭。
- `SphereDesk.exe --synthetic` 使用样例数据打开普通窗口，不挂到桌面，也不改桌面图标。

更细的操作见 [操作说明](docs/usage.md)。

## 文档

- [构建](docs/build.md)
- [操作](docs/usage.md)
- [架构](docs/architecture.md)
- [验收记录](docs/acceptance.md)
- [已知限制](docs/limitations.md)
- [第三方许可](docs/third-party.md)

程序未签名。不要为了运行它去关闭 SmartScreen 或其他系统防护。
