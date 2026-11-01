using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Roles;

/// <summary>The stored role as the API answers with it. The internal key is not part of it.</summary>
public static class RoleMapper
{
    public static RoleDto ToDto(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return new RoleDto(
            role.PublicId,
            role.Name,
            role.Builtin,
            role.Description,
            role.EntryCommand,
            role.ProfileDefaults,
            role.ExecutionProfile,
            WorkItems.WorkItemMapper.Utc(role.CreatedAt),
            WorkItems.WorkItemMapper.Utc(role.UpdatedAt));
    }
}
