using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using car.Models;
using car.Services;
using static car.Services.TextFormat;

namespace car
{
    public partial class MainWindow : Window
    {
        private const string AppTitle = "車輛紀錄";

        private readonly CarData _data;
        private readonly UpdateService _updater = new();

        // 目前在表單裡編輯的紀錄；null 表示表單是「新增」
        private FuelRecord? _selectedFuel;
        private MaintenanceRecord? _selectedMaint;

        // 重建清單時會觸發 SelectionChanged，這段期間不要把表單清掉
        private bool _refreshing;

        public MainWindow()
        {
            InitializeComponent();
            // 小螢幕（例如筆電）上不要讓視窗超出可用範圍
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 20);
            Width = Math.Min(Width, SystemParameters.WorkArea.Width - 20);
            _data = DataStore.Load();
            ClearFuelForm();
            ClearMaintForm();
            Refresh();
            StatusText.Text = $"版本 {_updater.CurrentVersion}";
            ThemeService.ThemeChanged += OnThemeChanged; // 切換主題（或系統深淺色改變）時更新標題列與按鈕
            UpdateThemeButton();
        }

        // ---------- 分頁 ----------

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (FuelPanel == null || MaintenancePanel == null) return; // InitializeComponent 期間
            bool fuel = FuelTab.IsChecked == true;
            FuelPanel.Visibility = fuel ? Visibility.Visible : Visibility.Collapsed;
            MaintenancePanel.Visibility = fuel ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------- 主題：跟隨系統 / 淺色 / 深色 ----------

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Cycle();
            StatusText.Text = $"主題：{ThemeName(ThemeService.Mode)}";
        }

        private static string ThemeName(AppTheme mode) => mode switch
        {
            AppTheme.Light => "淺色",
            AppTheme.Dark => "深色",
            _ => "跟隨系統",
        };

        private void OnThemeChanged()
        {
            UpdateThemeButton();
            ApplyTitleBar();
        }

        private void UpdateThemeButton()
        {
            ThemeIcon.Text = ThemeService.Mode switch
            {
                AppTheme.Light => "", // 太陽
                AppTheme.Dark => "",  // 月亮
                _ => "",              // 電腦（跟隨系統）
            };
            ThemeButton.ToolTip = $"主題：{ThemeName(ThemeService.Mode)}（按一下切換）";
        }

        // ---------- Windows 11：標題列底色與視窗背景同色，看起來是一整片 ----------

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyTitleBar();
        }

        private void ApplyTitleBar() => WindowTheme.ApplyTitleBar(this);

        // ---------- 啟動時檢查更新（詢問使用者） ----------

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CleanupService.RunInBackground(); // 清掉更新後遺留的舊檔
            DesktopShortcutService.TidyUp(_updater.IsInstalled);     // 更新後：重複捷徑只留最新的、工作列釘選改指向新版
            DesktopShortcutService.EnsureOnce(_updater.IsInstalled); // 安裝版第一次開啟時補上桌面捷徑

            await CheckForUpdateAsync(manual: false);
        }

        private void Shortcut_Click(object sender, RoutedEventArgs e)
        {
            const string title = "桌面捷徑";
            try
            {
                if (DesktopShortcutService.Create(_updater.IsInstalled) == ShortcutResult.SourceNotFound)
                {
                    MessageBox.Show("找不到安裝版的開始功能表捷徑，請重新執行「安裝.cmd」後再試。", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                StatusText.Text = "已在桌面建立捷徑";
                MessageBox.Show("已在桌面建立捷徑", title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or COMException)
            {
                MessageBox.Show($"建立桌面捷徑失敗：{ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdateAsync(manual: true);
        }

        private async Task CheckForUpdateAsync(bool manual)
        {
            const string title = "檢查更新";
            if (!_updater.IsInstalled)
            {
                if (manual)
                    MessageBox.Show("目前是開發版（非安裝版），無法線上更新。", title);
                return;
            }

            try
            {
                var info = await _updater.CheckAsync();
                if (info == null)
                {
                    if (manual) MessageBox.Show($"目前已是最新版本（{_updater.CurrentVersion}）。", title);
                    return;
                }

                var answer = MessageBox.Show(
                    $"發現新版本 {info.Version}（目前 {_updater.CurrentVersion}）。\n\n是否現在更新？更新完成後程式會自動重新啟動。",
                    "有新版本", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                StatusText.Text = "下載更新中…";
                await _updater.DownloadAndLaunchAsync(info, p => Dispatcher.Invoke(() => StatusText.Text = $"下載更新中… {p}%"));
                // 安裝程式已啟動，結束本程式讓它能覆蓋檔案；安裝完成後會自動重新開啟
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"版本 {_updater.CurrentVersion}";
                // 啟動時的自動檢查失敗（例如沒網路）不打擾使用者
                if (manual) MessageBox.Show($"檢查更新失敗：{ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---------- 重新整理畫面 ----------

        /// <summary>重算油耗與統計、重建兩個清單（保留原本的選取），並更新標題列。</summary>
        private void Refresh()
        {
            _refreshing = true;
            try
            {
                var stats = FuelCalculator.Calculate(_data.Fuel);
                var fuelRows = FuelCalculator.Ordered(_data.Fuel)
                    .Select(f => BuildFuelRow(f, stats[f]))
                    .Reverse() // 最新（里程最高）的放最上面
                    .ToList();
                FuelList.ItemsSource = fuelRows;
                FuelList.SelectedItem = fuelRows.FirstOrDefault(r => r.Record == _selectedFuel);

                var maintRows = MaintenanceCalculator.Ordered(_data.Maintenance)
                    .Select(BuildMaintenanceRow)
                    .Reverse()
                    .ToList();
                MaintenanceList.ItemsSource = maintRows;
                MaintenanceList.SelectedItem = maintRows.FirstOrDefault(r => r.Record == _selectedMaint);

                var current = OdometerCheck.Current(_data.Fuel, _data.Maintenance);
                UpdateFuelSummary();
                UpdateMaintenanceSummary(current);
                UpdateFuelInfo(stats);

                Title = _updater.IsInstalled ? $"{AppTitle} v{_updater.CurrentVersion}" : $"{AppTitle}（開發版）";
                CountText.Text = current == null
                    ? "還沒有任何紀錄"
                    : $"目前里程 {Km(current.Value)} 公里 · 加油 {_data.Fuel.Count} 筆 · 保養 {_data.Maintenance.Count} 筆";
            }
            finally { _refreshing = false; }
        }

        private static FuelRow BuildFuelRow(FuelRecord f, FuelStats st)
        {
            var sub = $"里程 {Km(f.Odometer)} km";
            if (f.UnitPrice is { } price) sub += $" · {Rate(price)} 元/公升";
            if (!f.IsFull) sub += " · 未加滿";
            var note = FirstLine(f.Note);
            if (note.Length > 0) sub += " · " + note;

            string economy = st.KmPerLiter is { } k ? $"{Rate(k)} km/L"
                : !f.IsFull ? "未加滿"
                : "等下次加滿";

            return new FuelRow
            {
                Record = f,
                Day = f.Date.ToString("MM/dd", CultureInfo.InvariantCulture),
                Year = f.Date.Year.ToString(CultureInfo.InvariantCulture),
                Title = $"加油 {Liters(f.Liters)} 公升 · {Money(f.Amount)} 元",
                Subtitle = sub,
                Economy = economy,
                HasEconomy = st.KmPerLiter != null,
                Distance = st.DistanceToNext is { } d ? $"開了 {Km(d)} km" : "最新一筆",
            };
        }

        private static MaintenanceRow BuildMaintenanceRow(MaintenanceRecord m)
        {
            var sub = $"里程 {Km(m.Odometer)} km";
            if (m.Shop.Trim().Length > 0) sub += " · " + m.Shop.Trim();
            if (m.NextOdometer is { } next) sub += $" · 下次 {Km(next)} km";
            var note = FirstLine(m.Note);
            if (note.Length > 0) sub += " · " + note;

            return new MaintenanceRow
            {
                Record = m,
                Day = m.Date.ToString("MM/dd", CultureInfo.InvariantCulture),
                Year = m.Date.Year.ToString(CultureInfo.InvariantCulture),
                Title = OneLine(m.Items),
                Subtitle = sub,
                AmountText = $"{Money(m.Amount)} 元",
            };
        }

        private static string FirstLine(string text) =>
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

        private static string OneLine(string text) =>
            string.Join("、", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        private void UpdateFuelSummary()
        {
            var s = FuelCalculator.Summarize(_data.Fuel);

            AvgEconomyText.Text = s.AverageKmPerLiter is { } avg ? $"{Rate(avg)} km/L" : "—";
            AvgEconomyHint.Text = s.AverageKmPerLiter != null
                ? $"依 {Km(s.TrackedDistance)} 公里計算"
                : "至少兩次加滿才算得出來";

            FuelTotalText.Text = $"{Money(s.TotalAmount)} 元";
            FuelTotalHint.Text = $"共 {s.Count} 次加油";

            LitersTotalText.Text = $"{Liters(s.TotalLiters)} 公升";
            LitersTotalHint.Text = s.TotalLiters > 0
                ? $"平均 {Rate(s.TotalAmount / (decimal)s.TotalLiters)} 元/公升"
                : "—";

            CostPerKmText.Text = s.CostPerKm is { } c ? $"{Rate(c)} 元" : "—";
            CostPerKmHint.Text = "每開 1 公里的油錢";
        }

        private void UpdateMaintenanceSummary(double? current)
        {
            var s = MaintenanceCalculator.Summarize(_data.Maintenance, current);

            MaintTotalText.Text = $"{Money(s.TotalAmount)} 元";
            MaintTotalHint.Text = $"共 {s.Count} 次保養";

            if (s.Latest is { } last)
            {
                LastMaintText.Text = $"{Km(last.Odometer)} km";
                LastMaintHint.Text = Date(last.Date);
                SinceMaintText.Text = $"{Km(Math.Max(0, (current ?? last.Odometer) - last.Odometer))} km";
                SinceMaintHint.Text = current is { } cur ? $"目前里程 {Km(cur)} km" : "";
            }
            else
            {
                LastMaintText.Text = "—";
                LastMaintHint.Text = "還沒有保養紀錄";
                SinceMaintText.Text = "—";
                SinceMaintHint.Text = current is { } cur ? $"目前里程 {Km(cur)} km" : "";
            }

            if (s.RemainingKm is { } remaining)
            {
                bool overdue = remaining < 0;
                NextMaintText.Text = overdue ? $"超過 {Km(-remaining)} km" : $"還有 {Km(remaining)} km";
                NextMaintText.SetResourceReference(TextBlock.ForegroundProperty, overdue ? "DangerBrush" : "TextBrush");
                NextMaintHint.Text = overdue ? $"預定 {Km(s.NextDueOdometer!.Value)} km，該保養了" : $"預定 {Km(s.NextDueOdometer!.Value)} km 保養";
            }
            else
            {
                NextMaintText.Text = "未設定";
                NextMaintText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                NextMaintHint.Text = "保養時可填下次保養里程";
            }
        }

        // ---------- 加油：表單 ----------

        private void FuelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing || FuelList.SelectedItem is not FuelRow row) return;
            _selectedFuel = row.Record;
            FillFuelForm(row.Record);
        }

        private void FillFuelForm(FuelRecord f)
        {
            FuelDateBox.Text = Date(f.Date);
            FuelOdoBox.Text = Plain(f.Odometer);
            FuelLitersBox.Text = Plain(f.Liters);
            FuelAmountBox.Text = Plain(f.Amount);
            FuelFullBox.IsChecked = f.IsFull;
            FuelNoteBox.Text = f.Note;
            FuelFormTitle.Text = "編輯加油紀錄";
            UpdateFuelInfo(FuelCalculator.Calculate(_data.Fuel));
        }

        private void ClearFuelForm()
        {
            _selectedFuel = null;
            _refreshing = true;
            FuelList.SelectedItem = null;
            _refreshing = false;
            FuelDateBox.Text = Date(DateTime.Today);
            FuelOdoBox.Clear();
            FuelLitersBox.Clear();
            FuelAmountBox.Clear();
            FuelFullBox.IsChecked = true;
            FuelNoteBox.Clear();
            FuelFormTitle.Text = "新增加油";
            FuelInfo.Visibility = Visibility.Collapsed;
        }

        private void FuelClear_Click(object sender, RoutedEventArgs e)
        {
            ClearFuelForm();
            FuelOdoBox.Focus();
        }

        private void FuelToday_Click(object sender, RoutedEventArgs e) => FuelDateBox.Text = Date(DateTime.Today);

        /// <summary>打加油量或金額時，即時算出每公升單價。</summary>
        private void FuelPrice_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (UnitPriceText == null) return; // InitializeComponent 期間
            UnitPriceText.Text =
                TryParseDouble(FuelLitersBox.Text, out var l) && l > 0 && TryParseDecimal(FuelAmountBox.Text, out var a) && a > 0
                    ? $"單價：每公升 {Rate(a / (decimal)l)} 元"
                    : "填好加油量與金額會自動算出單價";
        }

        /// <summary>選取的這一筆：開了幾公里、這段的油耗。</summary>
        private void UpdateFuelInfo(Dictionary<FuelRecord, FuelStats> stats)
        {
            if (_selectedFuel == null || !stats.TryGetValue(_selectedFuel, out var st))
            {
                FuelInfo.Visibility = Visibility.Collapsed;
                return;
            }

            var lines = new List<string>();
            lines.Add(st.DistanceToNext is { } d
                ? $"這次加油到下次加油開了 {Km(d)} 公里"
                : "這是最新一筆，下次加油後會算出開了幾公里");
            if (st.KmPerLiter is { } k)
                lines.Add($"油耗 {Rate(k)} 公里/公升（{Km(st.SegmentDistance!.Value)} 公里用了 {Liters(st.FuelUsed!.Value)} 公升）");
            else if (!_selectedFuel.IsFull)
                lines.Add("這次沒加滿，油量會併到前一次加滿那筆的油耗一起算");
            else
                lines.Add("下次加滿後會算出這段的油耗");

            FuelInfoText.Text = string.Join("\n", lines);
            FuelInfo.Visibility = Visibility.Visible;
        }

        private FuelRecord? ReadFuelForm()
        {
            const string title = "加油紀錄";
            if (!TryParseDate(FuelDateBox.Text, out var date))
                return Invalid<FuelRecord>(FuelDateBox, "日期格式不對，請用 2026/10/06 這種寫法。", title);
            if (!TryParseDouble(FuelOdoBox.Text, out var odo) || odo < 0)
                return Invalid<FuelRecord>(FuelOdoBox, "請輸入里程表上的公里數。", title);
            if (!TryParseDouble(FuelLitersBox.Text, out var liters) || liters <= 0)
                return Invalid<FuelRecord>(FuelLitersBox, "請輸入加了幾公升（要大於 0）。", title);
            if (!TryParseDecimal(FuelAmountBox.Text, out var amount) || amount < 0)
                return Invalid<FuelRecord>(FuelAmountBox, "請輸入這次加油花了多少錢。", title);

            return new FuelRecord
            {
                Date = date,
                Odometer = odo,
                Liters = liters,
                Amount = amount,
                IsFull = FuelFullBox.IsChecked == true,
                Note = FuelNoteBox.Text.Trim(),
            };
        }

        private void FuelAdd_Click(object sender, RoutedEventArgs e)
        {
            var f = ReadFuelForm();
            if (f == null || !ConfirmOdometer(f.Date, f.Odometer, except: null)) return;
            _data.Fuel.Add(f);
            _selectedFuel = f;
            Persist($"已新增 {Date(f.Date)} 的加油紀錄");
            FillFuelForm(f);
        }

        private void FuelSave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFuel is not { } target)
            {
                MessageBox.Show("請先在左邊選一筆要修改的加油紀錄，或按「新增」。", "提示");
                return;
            }
            var f = ReadFuelForm();
            if (f == null || !ConfirmOdometer(f.Date, f.Odometer, except: target)) return;
            target.Date = f.Date;
            target.Odometer = f.Odometer;
            target.Liters = f.Liters;
            target.Amount = f.Amount;
            target.IsFull = f.IsFull;
            target.Note = f.Note;
            Persist("已儲存修改");
            FillFuelForm(target);
        }

        private void FuelDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFuel is not { } target) return;
            var ok = MessageBox.Show($"確定刪除 {Date(target.Date)}（里程 {Km(target.Odometer)} 公里）這筆加油紀錄？", "刪除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (ok != MessageBoxResult.Yes) return;
            _data.Fuel.Remove(target);
            ClearFuelForm();
            Persist("已刪除加油紀錄");
        }

        // ---------- 保養：表單 ----------

        private void MaintenanceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing || MaintenanceList.SelectedItem is not MaintenanceRow row) return;
            _selectedMaint = row.Record;
            FillMaintForm(row.Record);
        }

        private void FillMaintForm(MaintenanceRecord m)
        {
            MaintDateBox.Text = Date(m.Date);
            MaintOdoBox.Text = Plain(m.Odometer);
            MaintItemsBox.Text = m.Items;
            MaintAmountBox.Text = Plain(m.Amount);
            MaintShopBox.Text = m.Shop;
            MaintNextBox.Text = m.NextOdometer is { } n ? Plain(n) : "";
            MaintNoteBox.Text = m.Note;
            MaintFormTitle.Text = "編輯保養紀錄";
        }

        private void ClearMaintForm()
        {
            _selectedMaint = null;
            _refreshing = true;
            MaintenanceList.SelectedItem = null;
            _refreshing = false;
            MaintDateBox.Text = Date(DateTime.Today);
            MaintOdoBox.Clear();
            MaintItemsBox.Clear();
            MaintAmountBox.Clear();
            MaintShopBox.Clear();
            MaintNextBox.Clear();
            MaintNoteBox.Clear();
            MaintFormTitle.Text = "新增保養";
        }

        private void MaintClear_Click(object sender, RoutedEventArgs e)
        {
            ClearMaintForm();
            MaintOdoBox.Focus();
        }

        private void MaintToday_Click(object sender, RoutedEventArgs e) => MaintDateBox.Text = Date(DateTime.Today);

        /// <summary>「+5,000」「+10,000」：下次保養里程 = 這次里程 + N。</summary>
        private void NextQuick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string tag } || !double.TryParse(tag, out var add)) return;
            if (!TryParseDouble(MaintOdoBox.Text, out var odo))
            {
                MessageBox.Show("請先填這次保養的里程。", "保養紀錄");
                MaintOdoBox.Focus();
                return;
            }
            MaintNextBox.Text = Plain(odo + add);
        }

        private MaintenanceRecord? ReadMaintForm()
        {
            const string title = "保養紀錄";
            if (!TryParseDate(MaintDateBox.Text, out var date))
                return Invalid<MaintenanceRecord>(MaintDateBox, "日期格式不對，請用 2026/10/06 這種寫法。", title);
            if (!TryParseDouble(MaintOdoBox.Text, out var odo) || odo < 0)
                return Invalid<MaintenanceRecord>(MaintOdoBox, "請輸入保養時的里程（公里）。", title);
            if (MaintItemsBox.Text.Trim().Length == 0)
                return Invalid<MaintenanceRecord>(MaintItemsBox, "請輸入這次保養做了哪些項目。", title);
            if (!TryParseDecimal(MaintAmountBox.Text, out var amount) || amount < 0)
                return Invalid<MaintenanceRecord>(MaintAmountBox, "請輸入這次保養花了多少錢。", title);

            double? next = null;
            if (MaintNextBox.Text.Trim().Length > 0)
            {
                if (!TryParseDouble(MaintNextBox.Text, out var n) || n <= odo)
                    return Invalid<MaintenanceRecord>(MaintNextBox, "下次保養里程要比這次的里程大；不需要可以留白。", title);
                next = n;
            }

            return new MaintenanceRecord
            {
                Date = date,
                Odometer = odo,
                Items = MaintItemsBox.Text.Trim(),
                Amount = amount,
                Shop = MaintShopBox.Text.Trim(),
                NextOdometer = next,
                Note = MaintNoteBox.Text.Trim(),
            };
        }

        private void MaintAdd_Click(object sender, RoutedEventArgs e)
        {
            var m = ReadMaintForm();
            if (m == null || !ConfirmOdometer(m.Date, m.Odometer, except: null)) return;
            _data.Maintenance.Add(m);
            _selectedMaint = m;
            Persist($"已新增 {Date(m.Date)} 的保養紀錄");
            FillMaintForm(m);
        }

        private void MaintSave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMaint is not { } target)
            {
                MessageBox.Show("請先在左邊選一筆要修改的保養紀錄，或按「新增」。", "提示");
                return;
            }
            var m = ReadMaintForm();
            if (m == null || !ConfirmOdometer(m.Date, m.Odometer, except: target)) return;
            target.Date = m.Date;
            target.Odometer = m.Odometer;
            target.Items = m.Items;
            target.Amount = m.Amount;
            target.Shop = m.Shop;
            target.NextOdometer = m.NextOdometer;
            target.Note = m.Note;
            Persist("已儲存修改");
            FillMaintForm(target);
        }

        private void MaintDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMaint is not { } target) return;
            var ok = MessageBox.Show($"確定刪除 {Date(target.Date)}「{OneLine(target.Items)}」這筆保養紀錄？", "刪除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (ok != MessageBoxResult.Yes) return;
            _data.Maintenance.Remove(target);
            ClearMaintForm();
            Persist("已刪除保養紀錄");
        }

        // ---------- 共用 ----------

        private T? Invalid<T>(Control field, string message, string title) where T : class
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            field.Focus();
            if (field is TextBox tb) tb.SelectAll();
            return null;
        }

        /// <summary>
        /// 日期與里程前後矛盾（例如比較早的日期卻有比較高的里程）時提醒，通常是打錯字。
        /// 回傳 true 表示可以繼續儲存。except：正在修改的那筆，不跟自己比。
        /// </summary>
        private bool ConfirmOdometer(DateTime date, double odometer, object? except)
        {
            var others = _data.Fuel.Where(f => f != except).Select(f => (f.Date, f.Odometer))
                .Concat(_data.Maintenance.Where(m => m != except).Select(m => (m.Date, m.Odometer)));
            if (OdometerCheck.FindConflict(date, odometer, others) is not { } c) return true;

            var answer = MessageBox.Show(
                $"這筆是 {Date(date)}、里程 {Km(odometer)} 公里，\n但 {Date(c.Date)} 那筆紀錄的里程是 {Km(c.Odometer)} 公里。\n\n" +
                "日期或里程可能打錯了。仍要儲存嗎？",
                "請確認里程", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            return answer == MessageBoxResult.Yes;
        }

        private void Persist(string status)
        {
            try
            {
                DataStore.Save(_data);
                StatusText.Text = status;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存失敗，資料尚未寫入硬碟：{ex.Message}", "儲存", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Refresh();
        }

        // ---------- Excel 匯出 / 匯入 ----------

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            const string title = "匯出 Excel";
            var dlg = new SaveFileDialog
            {
                Filter = "Excel 檔案 (*.xlsx)|*.xlsx",
                FileName = $"車輛紀錄_{DateTime.Now:yyyyMMdd}.xlsx",
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ExcelService.Export(_data, dlg.FileName);
                StatusText.Text = "匯出完成";
                MessageBox.Show($"匯出完成：加油 {_data.Fuel.Count} 筆、保養 {_data.Maintenance.Count} 筆。", title);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出失敗：{ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            const string title = "匯入 Excel";
            var dlg = new OpenFileDialog { Filter = "Excel 檔案 (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;

            CarData imported;
            int skipped;
            try
            {
                (imported, skipped) = ExcelService.Import(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯入失敗：{ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (imported.Fuel.Count == 0 && imported.Maintenance.Count == 0)
            {
                MessageBox.Show($"這個檔案裡沒有可匯入的資料。\n\n需要名為「{ExcelService.FuelSheet}」或「{ExcelService.MaintenanceSheet}」的工作表，" +
                                "第一列是標題（可先用「匯出 Excel」產生一份範本）。", title);
                return;
            }

            var skippedText = skipped > 0 ? $"\n（另有 {skipped} 列日期或里程讀不懂，會略過）" : "";
            var mode = MessageBox.Show(
                $"讀到加油 {imported.Fuel.Count} 筆、保養 {imported.Maintenance.Count} 筆。{skippedText}\n\n" +
                "是 = 合併到現有資料（日期與里程都相同的略過）\n否 = 清除現有資料，完全以 Excel 為準\n取消 = 不匯入",
                title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (mode == MessageBoxResult.Cancel) return;

            if (mode == MessageBoxResult.No)
            {
                _data.Fuel.Clear();
                _data.Maintenance.Clear();
            }

            int added = 0;
            foreach (var f in imported.Fuel)
            {
                if (_data.Fuel.Any(x => x.Date.Date == f.Date.Date && x.Odometer == f.Odometer)) continue;
                _data.Fuel.Add(f);
                added++;
            }
            foreach (var m in imported.Maintenance)
            {
                if (_data.Maintenance.Any(x => x.Date.Date == m.Date.Date && x.Odometer == m.Odometer)) continue;
                _data.Maintenance.Add(m);
                added++;
            }

            ClearFuelForm();
            ClearMaintForm();
            Persist($"匯入完成，新增 {added} 筆");
            MessageBox.Show($"匯入完成，新增 {added} 筆。", title);
        }
    }
}
