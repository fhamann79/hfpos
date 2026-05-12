using Pos.Backend.Api.Core.Entities;

namespace Pos.Backend.Api.Core.Models;

public class SriXmlDraftRequest
{
    public Sale Sale { get; set; } = null!;

    public Company Company { get; set; } = null!;

    public Establishment Establishment { get; set; } = null!;

    public Customer? Customer { get; set; }

    public IReadOnlyDictionary<int, string> ProductNames { get; set; } = new Dictionary<int, string>();

    public int Environment { get; set; }

    public int EmissionType { get; set; }
}
