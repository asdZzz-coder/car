using System.IO;
using ClosedXML.Excel;
using car.Models;
using car.Services;

namespace car.Tests
{
    /// <summary>存檔（JSON）與 Excel 匯出 / 匯入的自動測試，全部在暫存資料夾裡進行。</summary>
    public sealed class StorageTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "CarLog-Tests-" + Guid.NewGuid().ToString("N"));

        public StorageTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static CarData Sample() => new()
        {
            Fuel =
            {
                new FuelRecord { Date = new DateTime(2026, 9, 1), Odometer = 12000, Liters = 38.5, Amount = 1201.2m, Note = "中油" },
                new FuelRecord { Date = new DateTime(2026, 9, 15), Odometer = 12480, Liters = 20, Amount = 620m, IsFull = false },
                new FuelRecord { Date = new DateTime(2026, 9, 28), Odometer = 12950.5, Liters = 22.3, Amount = 691.3m, Note = "'單引號開頭" },
            },
            Maintenance =
            {
                new MaintenanceRecord
                {
                    Date = new DateTime(2026, 8, 20), Odometer = 11800, Items = "機油、機油芯\n空氣濾網", Amount = 2350m,
                    Shop = "原廠", NextOdometer = 16800, Note = "下次換煞車油",
                },
                new MaintenanceRecord { Date = new DateTime(2026, 9, 30), Odometer = 13000, Items = "輪胎對調", Amount = 400m },
            },
        };

        // ---------- JSON ----------

        [Fact]
        public void DataStore_RoundTrips()
        {
            var path = Path.Combine(_root, "car.json");
            var data = Sample();

            DataStore.Save(path, data);
            var loaded = DataStore.Load(path);

            Assert.Equal(3, loaded.Fuel.Count);
            Assert.Equal(2, loaded.Maintenance.Count);
            Assert.Equal(12950.5, loaded.Fuel[2].Odometer);
            Assert.False(loaded.Fuel[1].IsFull);
            Assert.Equal(1201.2m, loaded.Fuel[0].Amount);
            Assert.Equal(16800, loaded.Maintenance[0].NextOdometer);
            Assert.Null(loaded.Maintenance[1].NextOdometer);
            Assert.Contains("機油、機油芯", File.ReadAllText(path)); // 中文直接存成中文
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void DataStore_MissingFile_IsEmpty()
        {
            var data = DataStore.Load(Path.Combine(_root, "nope.json"));

            Assert.Empty(data.Fuel);
            Assert.Empty(data.Maintenance);
        }

        [Fact]
        public void DataStore_CorruptFile_IsBackedUpNotOverwritten()
        {
            var path = Path.Combine(_root, "car.json");
            File.WriteAllText(path, "{ 這不是 JSON");

            var data = DataStore.Load(path);

            Assert.Empty(data.Fuel);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(_root, "car.json.corrupt-*"));
        }

        [Fact]
        public void DataStore_OldFileWithoutIsFull_DefaultsToFull()
        {
            var path = Path.Combine(_root, "car.json");
            File.WriteAllText(path, """{ "Fuel": [ { "Date": "2026-01-01T00:00:00", "Odometer": 100, "Liters": 10, "Amount": 300 } ] }""");

            var data = DataStore.Load(path);

            Assert.True(data.Fuel.Single().IsFull);
            Assert.Empty(data.Maintenance);
        }

        // ---------- Excel ----------

        [Fact]
        public void Excel_RoundTrips()
        {
            var path = Path.Combine(_root, "export.xlsx");
            var data = Sample();

            ExcelService.Export(data, path);
            var (back, skipped) = ExcelService.Import(path);

            Assert.Equal(0, skipped);
            Assert.Equal(3, back.Fuel.Count);
            Assert.Equal(2, back.Maintenance.Count);

            var f = back.Fuel.OrderBy(x => x.Odometer).ToList();
            Assert.Equal(new DateTime(2026, 9, 1), f[0].Date);
            Assert.Equal(38.5, f[0].Liters);
            Assert.Equal(1201.2m, f[0].Amount);
            Assert.Equal("中油", f[0].Note);
            Assert.False(f[1].IsFull);
            Assert.Equal(12950.5, f[2].Odometer);
            Assert.Equal("'單引號開頭", f[2].Note);

            var m = back.Maintenance.OrderBy(x => x.Odometer).ToList();
            Assert.Equal("機油、機油芯\n空氣濾網", m[0].Items);
            Assert.Equal("原廠", m[0].Shop);
            Assert.Equal(16800, m[0].NextOdometer);
            Assert.Null(m[1].NextOdometer);
        }

        [Fact]
        public void Excel_ExportIncludesCalculatedColumns()
        {
            var path = Path.Combine(_root, "export.xlsx");
            ExcelService.Export(Sample(), path);

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(ExcelService.FuelSheet);
            // 第一筆：12000 加滿 → 沒加滿 20 L → 12950.5 加滿 22.3 L：950.5 km / 42.3 L
            Assert.Equal(480, ws.Cell(2, 7).GetDouble());                           // 到下次加油開了
            Assert.Equal(Math.Round(950.5 / 42.3, 2), ws.Cell(2, 8).GetDouble());  // 油耗
            Assert.Equal("否", ws.Cell(3, 6).GetString());
        }

        [Fact]
        public void Excel_ImportFindsColumnsByHeader_AndAcceptsTextValues()
        {
            var path = Path.Combine(_root, "manual.xlsx");
            using (var wb = new XLWorkbook())
            {
                // 使用者自己做的表：欄位順序不同、數字與日期是文字、有空白列、有一列日期打錯
                var ws = wb.Worksheets.Add(ExcelService.FuelSheet);
                ws.Cell(1, 1).Value = "金額 (元)";
                ws.Cell(1, 2).Value = "日期";
                ws.Cell(1, 3).Value = "里程 (km)";
                ws.Cell(1, 4).Value = "加油量 (公升)";
                ws.Cell(1, 5).Value = "加滿";
                ws.Cell(2, 1).SetValue("1,200");
                ws.Cell(2, 2).SetValue("2026/10/1");
                ws.Cell(2, 3).SetValue("12,000");
                ws.Cell(2, 4).SetValue("40");
                ws.Cell(2, 5).SetValue("否");
                ws.Cell(4, 1).SetValue("900");
                ws.Cell(4, 2).SetValue("不知道");
                ws.Cell(4, 3).SetValue("12500");
                wb.SaveAs(path);
            }

            var (data, skipped) = ExcelService.Import(path);

            Assert.Equal(1, skipped);
            var f = Assert.Single(data.Fuel);
            Assert.Equal(new DateTime(2026, 10, 1), f.Date);
            Assert.Equal(12000, f.Odometer);
            Assert.Equal(40, f.Liters);
            Assert.Equal(1200m, f.Amount);
            Assert.False(f.IsFull);
            Assert.Empty(data.Maintenance);
        }

        [Theory]
        [InlineData("是", true)]
        [InlineData("", true)]
        [InlineData("否", false)]
        [InlineData("N", false)]
        [InlineData("false", false)]
        [InlineData("未加滿", false)]
        public void ParseFull(string text, bool expected) => Assert.Equal(expected, ExcelService.ParseFull(text));
    }
}
