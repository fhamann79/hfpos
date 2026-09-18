namespace Pos.Backend.Api.Core.Models;

public sealed class MasterDataLifecycleException(string errorCode) : Exception(errorCode);
