using System.Text.Json;

namespace InvestOps.Contracts.Events;

public static class EventJson
{
    // camelCase no fio + leitura case-insensitive (formato canônico dos eventos)
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
