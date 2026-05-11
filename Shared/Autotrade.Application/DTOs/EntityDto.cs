namespace Autotrade.Application.DTOs;

/// <summary>
/// Base DTO for entities identified by a Guid.
/// </summary>
public abstract class EntityDto
{
    public Guid Id { get; set; }
}

/// <summary>
/// Base DTO for entities with creation and update timestamps.
/// </summary>
public abstract class AuditedEntityDto : EntityDto
{
    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>
/// Base DTO for entities with complete audit metadata.
/// </summary>
public abstract class FullAuditedEntityDto : AuditedEntityDto
{
    public string? CreatedBy { get; set; }

    public string? UpdatedBy { get; set; }
}
