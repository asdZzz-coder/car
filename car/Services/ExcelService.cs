using System.IO;
using ClosedXML.Excel;
using car.Models;

namespace car.Services
{
    /// <summary>
    /// Excel 匯出 / 匯入：一個檔案三張工作表「加油紀錄」「保養紀錄」「車輛」。
    /// 加油與保養的第一欄是車輛名稱，匯入時依名稱對應到車（沒寫車輛的歸給目前選的車）。
    /// 匯入時依第一列的標題找欄位（欄位順序可以調換），算出來的欄位（單價、開了幾公里、油耗）只匯出、不匯入。
    /// </summary>
    public static class ExcelService
    {
        public const string FuelSheet = "加油紀錄";
        public const string MaintenanceSheet = "保養紀錄";
        public const string VehicleSheet = "車輛";

        private const string HVehicle = "車輛";
        private const string HPlate = "車牌";
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

        internal static readonly string[] FuelHeaders =
            { HVehicle, HDate, HOdometer, HLiters, HAmount, HUnitPrice, HFull, HDistance, HEconomy, HNote };

        internal static readonly string[] MaintenanceHeaders =
            { HVehicle, HDate, HOdometer, HItems, HAmount, HShop, HNext, HNote };

        private static readonly string[] VehicleHeaders = { HVehicle, HPlate };

        /// <summary>標題在第幾欄（從 1 開始）。</summary>
        internal static int Col(string[] headers, string header) => Array.IndexOf(headers, header) + 1;

        // ---------- 匯出 ----------

        /// <summary>匯出所有車輛的資料；同一台車的紀錄放在一起，依里程排序。</summary>
        public static void Export(CarData data, string path)
        {
            using var wb = new XLWorkbook();

            var fuel = wb.Worksheets.Add(FuelSheet);
            WriteHeaders(fuel, FuelHeaders);
            int r = 2;
            foreach (var v in data.Vehicles)
            {
                var records = VehicleService.FuelOf(data, v.Id);
                var stats = FuelCalculator.Calculate(records);
                foreach (var f in FuelCalculator.Ordered(records))
                {
                    var st = stats[f];
                    int C(string h) => Col(FuelHeaders, h);
                    fuel.Cell(r, C(HVehicle)).SetValue(Escape(v.Name));
                    SetDate(fuel.Cell(r, C(HDate)), f.Date);
                    fuel.Cell(r, C(HOdometer)).SetValue(f.Odometer);
                    fuel.Cell(r, C(HLiters)).SetValue(f.Liters);
                    fuel.Cell(r, C(HAmount)).SetValue(f.Amount);
                    if (f.UnitPrice is { } price) fuel.Cell(r, C(HUnitPrice)).SetValue(Math.Round(price, 2));
                    fuel.Cell(r, C(HFull)).SetValue(f.IsFull ? "是" : "否");
                    if (st.DistanceToNext is { } d) fuel.Cell(r, C(HDistance)).SetValue(d);
                    if (st.KmPerLiter is { } k) fuel.Cell(r, C(HEconomy)).SetValue(Math.Round(k, 2));
                    fuel.Cell(r, C(HNote)).SetValue(Escape(f.Note));
                    r++;
                }
            }
            fuel.Column(Col(FuelHeaders, HUnitPrice)).Style.NumberFormat.Format = "0.00";
            fuel.Column(Col(FuelHeaders, HEconomy)).Style.NumberFormat.Format = "0.00";
            Finish(fuel);

            var maint = wb.Worksheets.Add(MaintenanceSheet);
            WriteHeaders(maint, MaintenanceHeaders);
            r = 2;
            foreach (var v in data.Vehicles)
            {
                foreach (var m in MaintenanceCalculator.Ordered(VehicleService.MaintenanceOf(data, v.Id)))
                {
                    int C(string h) => Col(MaintenanceHeaders, h);
                    maint.Cell(r, C(HVehicle)).SetValue(Escape(v.Name));
                    SetDate(maint.Cell(r, C(HDate)), m.Date);
                    maint.Cell(r, C(HOdometer)).SetValue(m.Odometer);
                    maint.Cell(r, C(HItems)).SetValue(Escape(m.Items));
                    maint.Cell(r, C(HAmount)).SetValue(m.Amount);
                    maint.Cell(r, C(HShop)).SetValue(Escape(m.Shop));
                    if (m.NextOdometer is { } next) maint.Cell(r, C(HNext)).SetValue(next);
                    maint.Cell(r, C(HNote)).SetValue(Escape(m.Note));
                    r++;
                }
            }
            Finish(maint);

            var vehicles = wb.Worksheets.Add(VehicleSheet);
            WriteHeaders(vehicles, VehicleHeaders);
            r = 2;
            foreach (var v in data.Vehicles)
            {
                vehicles.Cell(r, 1).SetValue(Escape(v.Name));
                vehicles.Cell(r, 2).SetValue(Escape(v.Plate));
                r++;
            }
            Finish(vehicles);

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

        /// <summary>
        /// 讀取 Excel；找不到工作表或沒有資料時回傳空清單。日期或里程讀不懂的列會略過並計入 Skipped。
        /// 回傳的 Vehicles 是檔案裡出現的車（新的 Id，由 VehicleService.Merge 依名稱對應到現有的車）；
        /// 沒寫車輛的紀錄 VehicleId 為空字串。
        /// </summary>
        public static (CarData Data, int Skipped) Import(string path)
        {
            // 允許讀取「正在被 Excel 開啟」的檔案，否則會因檔案被鎖定而失敗
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);
            var data = new CarData();
            int skipped = 0;

            // 車輛名稱 → 匯入資料裡的 Vehicle（同名只建一台）
            string VehicleIdFor(string name, string plate = "")
            {
                name = name.Trim();
                if (name.Length == 0) return "";
                var v = data.Vehicles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (v == null) data.Vehicles.Add(v = new Vehicle { Name = name });
                if (v.Plate.Length == 0) v.Plate = plate.Trim();
                return v.Id;
            }

            if (wb.Worksheets.TryGetWorksheet(VehicleSheet, out var vehicles))
            {
                var col = HeaderColumns(vehicles);
                foreach (var row in vehicles.RowsUsed().Skip(1))
                    VehicleIdFor(Text(row, col, HVehicle), Text(row, col, HPlate));
            }

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
                        VehicleId = VehicleIdFor(Text(row, col, HVehicle)),
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
                        VehicleId = VehicleIdFor(Text(row, col, HVehicle)),
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
