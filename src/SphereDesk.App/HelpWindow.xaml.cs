using System.Windows;

namespace SphereDesk.App;

public partial class HelpWindow : Window
{
    private const string HelpText =
        """
        SphereDesk 把桌面上的文件、文件夹和快捷方式排到真实桌面的壁纸上。球面跟着桌面出现，其他窗口照常盖住它。它不移动、不删除真实文件。

        原来的桌面图标
        启动后会关掉系统的“显示桌面图标”，避免和球面叠在一起。用托盘里的“退出”会把这个开关恢复。
        如果程序没有正常退出、图标仍然消失，可以在桌面空白处单击右键，选择“查看”，重新勾选“显示桌面图标”。下次再启动 SphereDesk 时，也会先尝试把上次没恢复的开关设回去。

        虚拟分组
        “加入分组”只改变 SphereDesk 自己的分类，文件仍留在原来的位置。删除分组也不会删除任何文件。左侧选择“全部”，可以看到当前桌面上的所有项目。

        怎么操作
        鼠标移到球面上时，旋转会暂停。按住拖动可以转动球面。单击选中，双击用系统默认方式打开。拖动超过系统判定的距离后，松手不会打开文件。
        搜索框可以按名称和类型过滤。选中结果后会转到球面正面，按 Enter 打开，按 Esc 清空搜索。下方列表可以用键盘选择和打开，屏幕阅读器也使用这个列表。
        背面的图标会变淡，并且不会抢走前面图标的点击。搜索可以把背面的项目转到正面。

        窗口和退出
        平时桌面上只有球面。按 Ctrl+Alt+D，或单击任务栏右下角图标，打开搜索、分组和设置。再按一次热键，或点面板上的关闭，只收起面板，球面还在。
        要退出并恢复原来的桌面图标，用托盘菜单里的“退出”。默认热键是 Ctrl+Alt+D。如果热键被占用，到设置里更换。
        带 --synthetic 启动时仍是普通窗口，使用样例数据，不会改桌面图标。
        """;

    private HelpWindow()
    {
        InitializeComponent();
        DataContext = HelpText;
    }

    public static void Show(Window owner) => new HelpWindow { Owner = owner }.ShowDialog();
}
