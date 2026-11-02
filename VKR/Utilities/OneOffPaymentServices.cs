#nullable enable
using System.Collections.Generic;

namespace VKR.Utilities;

public static class OneOffPaymentServices
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Разовое занятие",
        "Консультация",
        "Пробное занятие",
        "Мастер-класс",
        "Прочее"
    ];
}
