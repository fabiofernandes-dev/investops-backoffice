namespace InvestOps.Application.Operations.GetOperationAudit;

public sealed record AuditLogDto(
    string Action,
    string? OldValue,
    string? NewValue,
    string? UserId,
    DateTime CreatedAt);
