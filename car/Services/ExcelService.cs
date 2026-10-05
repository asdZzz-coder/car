using System.IO;
using ClosedXML.Excel;
using car.Models;

namespace car.Services
{
    /// <summary>
    /// Excel 匯出 / 匯入：一個檔案兩張工作表「加油紀錄」「保養紀錄」。
    /// 匯入時依第一列的標題找欄位（欄位順序可以調換），算出來的欄位（單價、開了幾公里、油耗）只匯出、不匯入。
    /// </summary>
    public static class ExcelService
    {
        public const string FuelSheet = "加油紀錄";
        public const string MaintenanceSheet = "保養紀錄";

        private const string HDate = "日期";
        private const string HOdometer = "里程 (km)";
        private const string HLiters = "加油量 (公升)";
        private const string HAmount = "金額 (元)";
        private const string HUnitPrice = "單價 (元/公升)";
        private const string HFull = "加滿";
        private const string HDistance = "到下次加油開了 (km)";
        private const string HEconomy = "油耗 (km/公升)";
        private const string HItems = "保養項目";
        private const string HShop = "保養廠";
        private const string HNext = "下次保養里程 (km)";
        private const string HNote = "備註";

        private static readonly string[] FuelHeaders =
            { HDate, HOdometer, HLiters, HAmount, HUnitPrice, HFull, HDistance, HEconomy, HNote };

        private static readonly string[] MaintenanceHeaders =
            { HDate, HOdometer, HItems, HAmount, HShop, HNext, HNote };

        // ---------- 匯出 ----------

        public static void Export(CarData data, string path)
        {
            using var wb = new XLWorkbook();

            var fuel = wb.Worksheets.Add(FuelSheet);
            WriteHeaders(fuel, FuelHeaders);
            var stats = FuelCalculator.Calculate(data.Fuel);
            int r = 2;
            foreach (var f in FuelCalculator.Ordered(data.Fuel))
            {
                var st = stats[f];
                SetDate(fuel.Cell(r, 1), f.Date);
                fuel.Cell(r, 2).SetValue(f.Odometer);
                fuel.Cell(r, 3).SetValue(f.Liters);
                fuel.Cell(r, 4).SetValue(f.Amount);
                if (f.UnitPrice is { } price) fuel.Cell(r, 5).SetValue(Math.Round(price, 2));
                fuel.Cell(r, 6).SetValue(f.IsFull ? "是" : "否");
                if (st.DistanceToNext is { } d) fuel.Cell(r, 7).SetValue(d);
                if (st.KmPerLiter is { } k) fuel.Cell(r, 8).SetValue(Math.Round(k, 2));
                fuel.Cell(r, 9).SetValue(Escape(f.Note));
                r++;
            }
            fuel.Column(5).Style.NumberFormat.Format = "0.00";
            fuel.Column(8).Style.NumberFormat.Format = "0.00";
            Finish(fuel);

            var maint = wb.Worksheets.Add(MaintenanceSheet);
            WriteHeaders(maint, MaintenanceHeaders);
            r = 2;
            foreach (var m in MaintenanceCalculator.Ordered(data.Maintenance))
            {
                SetDate(maint.Cell(r, 1), m.Date);
                maint.Cell(r, 2).SetValue(m.Odometer);
                maint.Cell(r, 3).SetValue(Escape(m.Items));
                maint.Cell(r, 4).SetValue(m.Amount);
                maint.Cell(r, 5).SetValue(Escape(m.Shop));
                if (m.NextOdometer is { } next) maint.Cell(r, 6).SetValue(next);
                maint.Cell(r, 7).SetValue(Escape(m.Note));
                r++;
            }
            Finish(maint);

            wb.SaveAs(path);
        }

        private static void WriteHeaders(IXLWorksheet ws, string[] headers)
        {
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];
            ws.Row(1).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);
        }

        private static void SetDate(IXLCell cell, DateTime date)
        {
            cell.SetValue(date.Date);
            cell.Style.DateFormat.Format = "yyyy/mm/dd";
        }

        private static void Finish(IXLWorksheet ws) => ws.Columns().AdjustToContents();

        // 開頭的 ' 會被 ClosedXML 當成 Excel 的「文字前綴」而吞掉，多加一個才能原樣保留
        private static string Escape(string s) => s.StartsWith('\'') ? "'" + s : s;

        // ---------- 匯入 ----------

        /// <summary>讀取 Excel；找不到工作表或沒有資料時回傳空清單。日期或里程讀不懂的列會略過並計入 Skipped。</summary>
        public static (CarData Data, int Skipped) Import(string path)
        {
            // 允許讀取「正在被 Excel 開啟」的檔案，否則會因檔案被鎖定而失敗
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);
            var data = new CarData();
            int skipped = 0;

            if (wb.Worksheets.TryGetWorksheet(FuelSheet, out var fuel))
            {
                var col = HeaderColumns(fuel);
                foreach (var row in fuel.RowsUsed().Skip(1))
                {
                    if (IsBlank(row)) continue;
                    if (!TryDate(row, col, out var date) || !TryNumber(row, col, HOdometer, out var odo)) { skipped++; continue; }
                    TryNumber(row, col, HLiters, out var liters);
                    TryMoney(row, col, HAmount, out var amount);
                    data.Fuel.Add(new FuelRecord
                    {
                        Date = date,
                        Odometer = odo,
                        Liters = liters,
                        Amount = amount,
                        IsFull = ParseFull(Text(row, col, HFull)),
                        Note = Text(row, col, HNote),
                    });
                }
            }

            if (wb.Worksheets.TryGetWorksheet(MaintenanceSheet, out var maint))
            {
                var col = HeaderColumns(maint);
                foreach (var row in maint.RowsUsed().Skip(1))
                {
                    if (IsBlank(row)) continue;
                    if (!TryDate(row, col, out var date) || !TryNumber(row, col, HOdometer, out var odo)) { skipped++; continue; }
                    TryMoney(row, col, HAmount, out var amount);
                    data.Maintenance.Add(new MaintenanceRecord
                    {
                        Date = date,
                        Odometer = odo,
                        Items = Text(row, col, HItems).Trim(),
                        Amount = amount,
                        Shop = Text(row, col, HShop).Trim(),
                        NextOdometer = TryNumber(row, col, HNext, out var next) && next > 0 ? next : null,
                        Note = Text(row, col, HNote),
                    });
                }
            }

            return (data, skipped);
        }

        /// <summary>第一列標題 → 欄號。</summary>
        private static Dictionary<string, int> HeaderColumns(IXLWorksheet ws)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in ws.Row(1).CellsUsed())
                map.TryAdd(cell.GetFormattedString().Trim(), cell.Address.ColumnNumber);
            return map;
        }

        private static IXLCell? Cell(IXLRow row, Dictionary<string, int> col, string header) =>
            col.TryGetValue(header, out var c) ? row.Cell(c) : null;

        private static string Text(IXLRow row, Dictionary<string, int> col, string header) =>
            Cell(row, col, header)?.GetFormattedString() ?? "";

        private static bool IsBlank(IXLRow row) => row.CellsUsed().All(c => c.GetFormattedString().Trim().Length == 0);

        private static bool TryDate(IXLRow row, Dictionary<string, int> col, out DateTime date)
        {
            date = default;
            var cell = Cell(row, col, HDate);
            if (cell == null) return false;
            if (cell.DataType == XLDataType.DateTime) { date = cell.GetDateTime().Date; return true; }
            if (cell.DataType == XLDataType.Number)
            {
                // 沒有設定日期格式的日期儲存格，讀出來是 Excel 的序號
                try { date = DateTime.FromOADate(cell.GetDouble()).Date; return true; }
                catch (ArgumentException) { return false; }
            }
            return TextFormat.TryParseDate(cell.GetFormattedString(), out date);
        }

        private static bool TryNumber(IXLRow row, Dictionary<string, int> col, string header, out double value)
        {
            value = 0;
            var cell = Cell(row, col, header);
            if (cell == null) return false;
            if (cell.DataType == XLDataType.Number) { value = cell.GetDouble(); return true; }
            return TextFormat.TryParseDouble(cell.GetFormattedString(), out value);
        }

        private static bool TryMoney(IXLRow row, Dictionary<string, int> col, string header, out decimal value)
        {
            value = 0;
            var cell = Cell(row, col, header);
            if (cell == null) return false;
            if (cell.DataType == XLDataType.Number) { value = (decimal)cell.GetDouble(); return true; }
            return TextFormat.TryParseDecimal(cell.GetFormattedString(), out value);
        }

        /// <summary>「否 / N / no / false / 0」為沒加滿，其他（含空白）一律當作加滿。</summary>
        internal static bool ParseFull(string text) =>
            text.Trim().ToLowerInvariant() is not ("否" or "n" or "no" or "false" or "0" or "未加滿");
    }
}
