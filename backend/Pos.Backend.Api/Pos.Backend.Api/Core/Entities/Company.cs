namespace Pos.Backend.Api.Core.Entities;

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Ruc { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public ICollection<Establishment> Establishments { get; set; } = new List<Establishment>();
}