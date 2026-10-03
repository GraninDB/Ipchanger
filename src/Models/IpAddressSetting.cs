namespace IPChanger.Models;

public record IpAddressSetting
{
    public string Name { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string SubnetMask { get; init; } = string.Empty;
    public string DefaultGateway { get; init; } = string.Empty;
    public string PrimaryDns { get; init; } = string.Empty;
    public string SecondaryDns { get; init; } = string.Empty;

    public override string ToString()
    {
        var dns = string.Empty;
        if (!string.IsNullOrEmpty(PrimaryDns) || !string.IsNullOrEmpty(SecondaryDns))
            dns = $", dns: {PrimaryDns}/{SecondaryDns}";
        return $"{Name} ({IpAddress}/{SubnetMask}, gw: {DefaultGateway}{dns})";
    }
}
