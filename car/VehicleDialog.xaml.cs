using System.Windows;
using System.Windows.Controls;
using car.Services;

namespace car
{
    /// <summary>新增 / 編輯車輛的小對話框。</summary>
    public partial class VehicleDialog : Window
    {
        private readonly Func<string, string?> _validate;

        private VehicleDialog(string title, string name, string plate, Func<string, string?> validate)
        {
            InitializeComponent();
            Title = title;
            NameBox.Text = name;
            PlateBox.Text = plate;
            _validate = validate;
            Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
            SourceInitialized += (_, _) => WindowTheme.ApplyTitleBar(this, "CardBrush");
        }

        /// <summary>顯示對話框；按確定回傳（名稱, 車牌），取消回傳 null。validate 回傳錯誤訊息或 null。</summary>
        public static (string Name, string Plate)? Ask(Window owner, string title, string name, string plate,
                                                      Func<string, string?> validate)
        {
            var dlg = new VehicleDialog(title, name, plate, validate) { Owner = owner };
            return dlg.ShowDialog() == true ? (dlg.NameBox.Text.Trim(), dlg.PlateBox.Text.Trim()) : null;
        }

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => ErrorText.Visibility = Visibility.Collapsed;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var error = _validate(NameBox.Text);
            if (error != null)
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
                NameBox.Focus();
                return;
            }
            DialogResult = true;
        }
    }
}
