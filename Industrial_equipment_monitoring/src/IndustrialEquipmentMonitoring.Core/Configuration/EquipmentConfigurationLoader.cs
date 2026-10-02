using System.Globalization;
using System.Xml.Linq;

namespace IndustrialEquipmentMonitoring.Core.Configuration;

public static class EquipmentConfigurationLoader
{
    public static EquipmentConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Equipment configuration was not found: {path}",
                path);
        }

        var document = XDocument.Load(path);
        var root = document.Root
            ?? throw new InvalidDataException("Equipment XML is missing a root element.");

        if (root.Name.LocalName != "Equipment")
        {
            throw new InvalidDataException("Equipment XML root element must be <Equipment>.");
        }

        var name = Required(root, "Name").Trim();
        var address = Required(root, "Address").Trim();
        var port = ParseInt(root, "Port");
        var temperatureLimit = ParseDouble(root, "TemperatureLimit");
        var pollIntervalMs = ParseInt(root, "PollIntervalMs");

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException("Equipment name is required.");
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidDataException("Equipment address is required.");
        }

        if (port is < 1 or > 65535)
        {
            throw new InvalidDataException("Port must be between 1 and 65535.");
        }

        if (temperatureLimit <= 0)
        {
            throw new InvalidDataException("TemperatureLimit must be greater than zero.");
        }

        if (pollIntervalMs < 100)
        {
            throw new InvalidDataException("PollIntervalMs must be at least 100.");
        }

        return new EquipmentConfiguration
        {
            Name = name,
            Address = address,
            Port = port,
            TemperatureLimit = temperatureLimit,
            PollIntervalMs = pollIntervalMs
        };
    }

    private static string Required(XElement root, string name)
    {
        return root.Element(name)?.Value
            ?? throw new InvalidDataException($"Equipment XML is missing <{name}>.");
    }

    private static int ParseInt(XElement root, string name)
    {
        var text = Required(root, name).Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"<{name}> must be an integer.");
        }

        return value;
    }

    private static double ParseDouble(XElement root, string name)
    {
        var text = Required(root, name).Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"<{name}> must be a number.");
        }

        return value;
    }
}
