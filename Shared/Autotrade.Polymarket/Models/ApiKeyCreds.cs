namespace Autotrade.Polymarket.Models;

/// <summary>
/// CLOB API Key 凭证（L2）。
/// </summary>
public sealed record ApiKeyCreds(
    string Key,
    string Secret,
    string Passphrase);

