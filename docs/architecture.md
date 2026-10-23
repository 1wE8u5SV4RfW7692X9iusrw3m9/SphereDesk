# 架构决策

## 分层

依赖方向固定为：

- `SphereDesk.App` 组装应用服务和 Windows 实现，包含 WPF 视图与 ViewModel。
- `SphereDesk.Application` 依赖 Core 和抽象接口，负责扫描协调、虚拟分组、快照合并和 JSON 配置。
- `SphereDesk.Windows` 实现已知文件夹、系统图标、Shell 打开、目录监听、托盘、热键和单实例。
- `SphereDesk.Core` 不引用 WPF、COM 或 Windows API。几何、交互状态机、搜索、布局稳定性和配置校验都在这里。

ViewModel 不直接调用 P/Invoke。系统调用留在 Windows 项目。

## 首版取舍

- 窗口使用 `WindowChrome` 的不透明无边框样式，不做透明窗口，也不始终置顶。
- 只读取用户桌面和公共桌面的顶层项目。不递归子目录，不枚举回收站、此电脑等 Shell 虚拟图标。
- 图标通过 `IShellItemImageFactory` 在专用 STA 线程获取，并带内存与磁盘缓存。取消请求后丢弃过时结果，不中断正在执行的 COM 调用。
- 打开使用 `ShellExecuteEx` 的默认 verb，不把路径拼进命令行。
- 配置是带 `schemaVersion` 的 JSON。保存时先写临时文件，再原子替换，并保留上一份备份。
- 球面位置用 Fibonacci 分布初始化。已有项目在少量增删后保持原位置，只有“重新均匀排列”会重算。
- 姿态用归一化四元数。屏幕坐标 X 向右、Y 向下、Z 朝向观察者。摄像机距离是球半径的 3 倍。单次动画时间步上限为 0.05 秒。
- `--synthetic` 使用内存样例和独立配置目录，不能打开真实文件。

## 明确不做

实际移动、重命名、回收站、缩略图、完整 Shell 菜单，以及贴在壁纸层的桌面挂载，都不在首版。界面不提供这些无效菜单。
