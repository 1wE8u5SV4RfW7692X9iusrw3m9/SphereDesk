# 构建说明

## 已核实环境

记录日期 2026-10-21。以下来自本机构建机，不是对所有机器的保证。

- SDK：10.0.204。`global.json` 固定该版本，`rollForward` 为 `latestPatch`，`allowPrerelease` 为 false。
- 目标框架：类库 `net10.0`，Windows 与 WPF 项目 `net10.0-windows10.0.19041.0`。
- 本机 RID：`win-x64`。
- 注册表：`ProductName` 为 Windows 10 Pro，`DisplayVersion` 为 26H2，内部版本 26300.9457。
- 发布包包含自带的 .NET 运行时。发布目录中有 `coreclr.dll` 与 `hostfxr.dll`。

## 命令

在仓库根目录执行：

```powershell
dotnet restore SphereDesk.sln
dotnet build SphereDesk.sln -c Release
dotnet test tests/SphereDesk.Core.Tests/SphereDesk.Core.Tests.csproj -c Release
dotnet test tests/SphereDesk.Windows.Tests/SphereDesk.Windows.Tests.csproj -c Release
dotnet publish src/SphereDesk.App/SphereDesk.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/win-x64
Compress-Archive -Path artifacts\win-x64\* -DestinationPath artifacts\SphereDesk-win-x64.zip
```

`scripts/publish.ps1` 会执行 Release 构建、测试、发布和 ZIP。

Windows CI 定义在 `.github/workflows/windows-ci.yml`，包含 restore、Release build、Core tests、Windows integration tests 和 win-x64 自包含发布。

## 安装包

记录日期 2026-10-21。本机通过 winget 安装了 Inno Setup 6.7.3，包标识是 `JRSoftware.InnoSetup`。同一天用 `--version 6.4.3` 安装时，winget 返回找不到该版本；`winget show` 给出的版本是 6.7.3。编译器是 `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`。语言目录里没有 `ChineseSimplified.isl`，安装向导使用 Inno Setup 默认语言。

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package-installer.ps1
```

该脚本先走 `scripts/publish.ps1`，再编译 `scripts/SphereDesk.iss`。输出是 `artifacts\SphereDesk-Setup-1.0.0.exe`。安装范围是当前用户，默认目录是 `%LocalAppData%\Programs\SphereDesk`，开始菜单有 SphereDesk，不创建桌面快捷方式，不请求管理员权限。安装包未签名。

## 运行

- 真实桌面：`artifacts\win-x64\SphereDesk.exe`
- 合成数据：`SphereDesk.exe --synthetic`

合成数据使用 `%LocalAppData%\SphereDesk\synthetic`，不会写入真实桌面配置。真实配置在 `%LocalAppData%\SphereDesk\settings.json`。

启动不请求管理员权限。程序未签名，SmartScreen 可能提示未知发布者。不要为了通过验收去关闭系统防护。
