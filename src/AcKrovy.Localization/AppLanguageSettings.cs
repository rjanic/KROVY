using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AcKrovy.Localization;

[DataContract]
public sealed class AppLanguageSettings
{
    [DataMember(Name = "languageCode", EmitDefaultValue = false)]
    public string LanguageCode { get; set; } = AppLanguageService.DefaultLanguageCode;

    public AppLanguageSettings Normalize() => new()
    {
        LanguageCode = AppLanguageService.NormalizeLanguageCode(LanguageCode),
    };
}

public static class AppLanguageSettingsSerializer
{
    public static AppLanguageSettings Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AppLanguageSettings();
        }

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var serializer = new DataContractJsonSerializer(typeof(AppLanguageSettings));
            return (serializer.ReadObject(stream) as AppLanguageSettings ?? new AppLanguageSettings()).Normalize();
        }
        catch (SerializationException)
        {
            return new AppLanguageSettings();
        }
        catch (FormatException)
        {
            return new AppLanguageSettings();
        }
    }

    public static string Serialize(AppLanguageSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(typeof(AppLanguageSettings));
        serializer.WriteObject(stream, settings.Normalize());
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
