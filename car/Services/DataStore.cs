using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using car.Models;

namespace car.Services
{
    /// <summary>存檔內容：加油紀錄 + 保養紀錄。</summary>
    public class CarData
    {
        public List<FuelRecord> Fuel { get; set; } = new();
        public List<MaintenanceRecord> Maintenance { get; set; } = new();
    }

    /// <summary>
    /// 行車資料存成 JSON（%AppData%\CarLog\car.json），不加密，可直接備份或用記事本查看。
    /// 寫入時先寫暫存檔再換名，存到一半當機也不會把舊資料弄壞。換電腦時請用「匯出 Excel → 匯入」搬資料。
    /// </summary>
    public static class DataStore
    {
        // 環境變數 CARLOG_DATA_DIR 可指定其他資料夾（測試用）；平常不設定，存在 %AppData%\CarLog
        public static readonly string DataDirectory =
            Environment.GetEnvironmentVariable("CARLOG_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CarLog");

        public const string FileName = "car.json";

        private static string FilePath => Path.Combine(DataDirectory, FileName);

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文直接存成中文，用記事本打開也看得懂
        };

        public static CarData Load() => Load(FilePath);

        public static void Save(CarData data) => Save(FilePath, data);

        internal static CarData Load(string path)
        {
            if (!File.Exists(path)) return new();
            try
            {
                var data = JsonSerializer.Deserialize<CarData>(File.ReadAllText(path), Options) ?? new();
                data.Fuel ??= new();
                data.Maintenance ??= new();
                return data;
            }
            catch (JsonException)
            {
                // 檔案損毀（例如被手動改壞）：備份後以空資料開始，不覆蓋原檔
                File.Move(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                return new();
            }
        }

        internal static void Save(string path, CarData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
