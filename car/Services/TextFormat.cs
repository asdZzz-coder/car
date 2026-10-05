using System.Globalization;
using System.Text;

namespace car.Services
{
    /// <summary>
    /// 數字與日期的輸入解析、畫面顯示格式。
    /// 輸入時容許千分位逗號、前後空白，以及中文輸入法打出來的全形數字（１２３．５）。
    /// </summary>
    public static class TextFormat
    {
        public const string DateFormat = "yyyy/MM/dd";

        private static readonly string[] DateFormats =
        {
            "yyyy/M/d", "yyyy-M-d", "yyyy.M.d", "yyyyMMdd", "yyyy/M/d H:mm", "yyyy/M/d H:mm:ss",
        };

        /// <summary>全形轉半形、去掉千分位與空白。</summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (var c in text.Trim())
            {
                var h = c is >= '！' and <= '～' ? (char)(c - 0xFEE0) : c; // 全形 ASCII → 半形
                if (h is ',' or ' ' or '　') continue;
                if (h == '。') h = '.';
                sb.Append(h);
            }
            return sb.ToString();
        }

        public static bool TryParseDouble(string? text, out double value) =>
            double.TryParse(Normalize(text), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);

        public static bool TryParseDecimal(string? text, out decimal value) =>
            decimal.TryParse(Normalize(text), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

        public static bool TryParseDate(string? text, out DateTime value)
        {
            var t = Normalize(text);
            return DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        public static string Date(DateTime d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);

        /// <summary>公里：12,345 或 12,345.6（整數不顯示小數點）。</summary>
        public static string Km(double v) => v.ToString("#,0.#", CultureInfo.InvariantCulture);

        /// <summary>公升：38.52（最多兩位小數）。</summary>
        public static string Liters(double v) => v.ToString("#,0.##", CultureInfo.InvariantCulture);

        /// <summary>金額：1,234 或 1,234.5。</summary>
        public static string Money(decimal v) => v.ToString("#,0.##", CultureInfo.InvariantCulture);

        /// <summary>油耗、單價等：固定兩位小數。</summary>
        public static string Rate(double v) => v.ToString("#,0.00", CultureInfo.InvariantCulture);

        public static string Rate(decimal v) => v.ToString("#,0.00", CultureInfo.InvariantCulture);

        /// <summary>輸入框顯示用：不加千分位，方便直接修改。</summary>
        public static string Plain(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static string Plain(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
