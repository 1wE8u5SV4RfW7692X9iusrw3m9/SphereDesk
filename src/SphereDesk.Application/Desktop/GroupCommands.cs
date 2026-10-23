using SphereDesk.Core.Models;

namespace SphereDesk.Application.Desktop;

public static class GroupCommands
{
    public const int MaxNameLength = 40;

    public static OperationResult Create(UserSettings settings, string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return OperationResult.Fail("分组名称不能为空。");
        }

        if (trimmed.Length > MaxNameLength)
        {
            return OperationResult.Fail("分组名称不能超过 40 个字符。");
        }

        if (settings.Groups.Any(group => group.Name.Equals(trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            return OperationResult.Fail("已经有同名分组。");
        }

        var sortOrder = settings.Groups.Count == 0 ? 0 : settings.Groups.Max(group => group.SortOrder) + 1;
        settings.Groups.Add(new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmed,
            SortOrder = sortOrder,
        });
        return OperationResult.Ok("已创建分组。文件仍留在原来的位置。");
    }

    public static OperationResult Rename(UserSettings settings, string groupId, string name)
    {
        var group = settings.Groups.FirstOrDefault(candidate => candidate.Id == groupId);
        if (group is null)
        {
            return OperationResult.Fail("找不到这个分组。");
        }

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return OperationResult.Fail("分组名称不能为空。");
        }

        if (trimmed.Length > MaxNameLength)
        {
            return OperationResult.Fail("分组名称不能超过 40 个字符。");
        }

        if (settings.Groups.Any(candidate => candidate.Id != groupId && candidate.Name.Equals(trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            return OperationResult.Fail("已经有同名分组。");
        }

        group.Name = trimmed;
        return OperationResult.Ok("已重命名分组。文件没有被移动。");
    }

    public static OperationResult Delete(UserSettings settings, string groupId)
    {
        var removed = settings.Groups.RemoveAll(group => group.Id == groupId);
        if (removed == 0)
        {
            return OperationResult.Fail("找不到这个分组。");
        }

        return OperationResult.Ok("已删除分组。没有删除任何文件。");
    }

    public static OperationResult AddItem(UserSettings settings, string groupId, string itemId)
    {
        var group = settings.Groups.FirstOrDefault(candidate => candidate.Id == groupId);
        if (group is null)
        {
            return OperationResult.Fail("请先选择一个分组，再加入分组。");
        }

        if (string.IsNullOrWhiteSpace(itemId))
        {
            return OperationResult.Fail("请先选择一个项目。");
        }

        if (!group.ItemIds.Contains(itemId, StringComparer.Ordinal))
        {
            group.ItemIds.Add(itemId);
        }

        return OperationResult.Ok("已加入分组。文件仍留在原来的位置。");
    }

    public static OperationResult RemoveItem(UserSettings settings, string groupId, string itemId)
    {
        var group = settings.Groups.FirstOrDefault(candidate => candidate.Id == groupId);
        if (group is null)
        {
            return OperationResult.Fail("请先选择一个分组。");
        }

        group.ItemIds.RemoveAll(id => id == itemId);
        return OperationResult.Ok("已从分组移除。文件没有被删除。");
    }
}
