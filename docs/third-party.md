# 第三方许可

应用程序项目没有引用第三方 NuGet 包。发布包里的运行时来自 .NET SDK 10.0.204 的自包含发布。

.NET 运行时由 .NET Foundation 发布，使用 MIT 许可证。许可文本随上游仓库 `dotnet/runtime` 提供，本文件不复制全文。

测试项目使用 xUnit 2.9.3、Microsoft.NET.Test.Sdk 17.14.1 和 coverlet.collector 6.0.4。这些包只用于开发测试，不进入 `SphereDesk.exe` 发布目录。
