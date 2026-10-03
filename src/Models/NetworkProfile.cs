namespace IPChanger.Models;

public record NetworkProfile
{
    public string InterfaceName { get; init; } = string.Empty;
    public string InterfaceDescription { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
    public List<IpAddressSetting> Presets { get; init; } = new();
}
