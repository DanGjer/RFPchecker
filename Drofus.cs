using System.Text.Json;
using System.Text.Json.Serialization;

namespace RFPchecker;

public class DrofusRoom
{
    public string DrofusRoomId { get; set; } = string.Empty;
    public string DrofusRoomName { get; set; } = string.Empty;
    public string DrofusRoomFuncNo { get; set; } = string.Empty;
    public string DrofusOutletsNormal { get; set; } = string.Empty;
    public string DrofusOutletsEmergency { get; set; } = string.Empty;
    public string DrofusOutletsUps { get; set; } = string.Empty;
    public string DrofusOutletsData { get; set; } = string.Empty;

}

public class DrofusRoomResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("room_func_no")]
    public object? RoomFuncNo { get; set; }

    [JsonPropertyName("room_data_20101610")]
    public object? RoomData20101610 { get; set; }

    [JsonPropertyName("room_data_20102210")]
    public object? RoomData20102210 { get; set; }

    [JsonPropertyName("room_data_20102310")]
    public object? RoomData20102310 { get; set; }

    [JsonPropertyName("room_data_21101010")]
    public object? RoomData21101010 { get; set; }

    public static string AsText(object? value)
    {
        if (value is null)
            return string.Empty;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Undefined => string.Empty,
                JsonValueKind.String => element.GetString() ?? string.Empty,
                _ => element.ToString()
            };
        }

        return Convert.ToString(value) ?? string.Empty;
    }
}