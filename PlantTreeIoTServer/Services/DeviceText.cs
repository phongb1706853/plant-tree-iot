using System.Globalization;
using System.Text;

namespace PlantTreeIoTServer.Services;

/// <summary>
/// Chuẩn hoá chữ gửi lên màn TFT của thiết bị — HÀM THUẦN. Font màn hình chỉ có ASCII, firmware lưu tối đa 95 ký
/// tự và cắt im lặng phần dư (backend-api.md mục 5.9). Nên server tự: bỏ dấu tiếng Việt (NFD + bỏ dấu kết hợp,
/// đ → d), bỏ emoji / ký tự ngoài ASCII, gộp khoảng trắng, cắt còn ≤ 90 ký tự.
/// </summary>
public static class DeviceText
{
    public const int MaxScreenLength = 90;

    /// <summary>"Chào bạn 🌱 đã tưới!" -> "Chao ban da tuoi!". null -> "".</summary>
    public static string ToScreenText(string? text, int maxLength = MaxScreenLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // đ/Đ không tách được bằng NFD (là chữ riêng, không phải d + dấu).
        var decomposed = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = true; // bỏ khoảng trắng đầu chuỗi
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // dấu thanh / dấu mũ / móc đã tách ra

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else if (ch is >= ' ' and <= '~')
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
            // còn lại (emoji, ký tự điều khiển, chữ ngoài Latin): bỏ.
        }

        var result = sb.ToString().TrimEnd();
        return result.Length <= maxLength ? result : result[..maxLength].TrimEnd();
    }
}
