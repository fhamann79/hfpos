namespace Pos.Backend.Api.Core.DTOs;

public class SupplierDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Identification { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
