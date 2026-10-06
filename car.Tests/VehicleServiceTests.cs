using car.Models;
using car.Services;

namespace car.Tests
{
    /// <summary>多台車：補齊資料、名稱檢查、刪除、匯入合併的自動測試。</summary>
    public class VehicleServiceTests
    {
        private static (CarData Data, Vehicle A, Vehicle B) TwoCars()
        {
            var a = new Vehicle { Name = "Altis" };
            var b = new Vehicle { Name = "機車" };
            var data = new CarData { Vehicles = { a, b }, SelectedVehicleId = a.Id };
            data.Fuel.Add(new FuelRecord { VehicleId = a.Id, Date = new DateTime(2026, 1, 1), Odometer = 1000, Liters = 40, Amount = 1200 });
            data.Fuel.Add(new FuelRecord { VehicleId = b.Id, Date = new DateTime(2026, 1, 1), Odometer = 300, Liters = 4, Amount = 120 });
            data.Maintenance.Add(new MaintenanceRecord { VehicleId = b.Id, Date = new DateTime(2026, 1, 5), Odometer = 320, Items = "機油" });
            return (data, a, b);
        }

        // ---------- Normalize ----------

        [Fact]
        public void Normalize_EmptyData_CreatesDefaultVehicle()
        {
            var data = new CarData();

            VehicleService.Normalize(data);

            Assert.Equal(VehicleService.DefaultName, Assert.Single(data.Vehicles).Name);
            Assert.Equal(data.Vehicles[0].Id, data.SelectedVehicleId);
        }

        [Fact]
        public void Normalize_OrphanRecordsGoToFirstVehicle_AndKeepsValidOnes()
        {
            var (data, a, b) = TwoCars();
            var orphan = new FuelRecord { VehicleId = "deleted-car", Odometer = 5 };
            data.Fuel.Add(orphan);

            VehicleService.Normalize(data);

            Assert.Equal(a.Id, orphan.VehicleId);
            Assert.Single(VehicleService.FuelOf(data, b.Id));
        }

        [Fact]
        public void Normalize_FixesDuplicateIdsBlankNamesAndBadSelection()
        {
            var data = new CarData
            {
                Vehicles = { new Vehicle { Id = "x", Name = " Altis " }, new Vehicle { Id = "x", Name = "" } },
                SelectedVehicleId = "gone",
            };

            VehicleService.Normalize(data);

            Assert.NotEqual(data.Vehicles[0].Id, data.Vehicles[1].Id);
            Assert.Equal("Altis", data.Vehicles[0].Name);
            Assert.Equal(VehicleService.DefaultName, data.Vehicles[1].Name);
            Assert.Equal("x", data.SelectedVehicleId);
        }

        [Fact]
        public void UniqueName_AddsNumber()
        {
            var vehicles = new[] { new Vehicle { Name = "我的車" }, new Vehicle { Name = "我的車 2" } };

            Assert.Equal("我的車 3", VehicleService.UniqueName(vehicles, "我的車"));
            Assert.Equal("新車", VehicleService.UniqueName(vehicles, "新車"));
        }

        // ---------- 名稱 ----------

        [Fact]
        public void Validate_Name()
        {
            var (data, a, _) = TwoCars();

            Assert.Equal(VehicleNameError.Empty, VehicleService.Validate("  ", data.Vehicles));
            Assert.Equal(VehicleNameError.TooLong, VehicleService.Validate(new string('車', 21), data.Vehicles));
            Assert.Equal(VehicleNameError.Duplicate, VehicleService.Validate("altis", data.Vehicles));
            Assert.Equal(VehicleNameError.None, VehicleService.Validate("ALTIS", data.Vehicles, editing: a)); // 改自己的大小寫
            Assert.Equal(VehicleNameError.None, VehicleService.Validate("新車", data.Vehicles));
        }

        // ---------- 刪除 ----------

        [Fact]
        public void Delete_RemovesVehicleAndItsRecords_AndMovesSelection()
        {
            var (data, a, b) = TwoCars();
            data.SelectedVehicleId = b.Id;

            Assert.True(VehicleService.Delete(data, b));

            Assert.Equal(new[] { a }, data.Vehicles);
            Assert.All(data.Fuel, f => Assert.Equal(a.Id, f.VehicleId));
            Assert.Empty(data.Maintenance);
            Assert.Equal(a.Id, data.SelectedVehicleId);
        }

        [Fact]
        public void Delete_LastVehicle_IsRefused()
        {
            var data = new CarData();
            VehicleService.Normalize(data);

            Assert.False(VehicleService.Delete(data, data.Vehicles[0]));
            Assert.Single(data.Vehicles);
        }

        // ---------- 匯入合併 ----------

        [Fact]
        public void Merge_MatchesVehiclesByName_AddsNewOnes_AndSkipsDuplicates()
        {
            var (data, a, b) = TwoCars();
            var importedA = new Vehicle { Name = "ALTIS", Plate = "AAA-111" };
            var importedC = new Vehicle { Name = "貨車" };
            var imported = new CarData { Vehicles = { importedA, importedC } };
            imported.Fuel.Add(new FuelRecord { VehicleId = importedA.Id, Date = new DateTime(2026, 1, 1), Odometer = 1000 }); // 重複
            imported.Fuel.Add(new FuelRecord { VehicleId = importedA.Id, Date = new DateTime(2026, 2, 1), Odometer = 1500 });
            imported.Fuel.Add(new FuelRecord { VehicleId = importedC.Id, Date = new DateTime(2026, 2, 1), Odometer = 90000 });
            imported.Maintenance.Add(new MaintenanceRecord { VehicleId = "", Date = new DateTime(2026, 3, 1), Odometer = 400 }); // 沒寫車輛

            int added = VehicleService.Merge(data, imported, defaultVehicleId: b.Id, replace: false);

            Assert.Equal(3, added);
            Assert.Equal(3, data.Vehicles.Count);
            Assert.Equal("AAA-111", a.Plate); // 原本沒車牌，補上
            Assert.Equal(2, VehicleService.FuelOf(data, a.Id).Count);
            var truck = data.Vehicles.Single(v => v.Name == "貨車");
            Assert.Single(VehicleService.FuelOf(data, truck.Id));
            Assert.Equal(2, VehicleService.MaintenanceOf(data, b.Id).Count); // 沒寫車輛的給目前選的車
        }

        [Fact]
        public void Merge_Replace_ClearsEverythingButCurrentVehicle()
        {
            var (data, a, b) = TwoCars();
            var imported = new CarData();
            imported.Fuel.Add(new FuelRecord { VehicleId = "", Date = new DateTime(2026, 5, 1), Odometer = 2000 });

            int added = VehicleService.Merge(data, imported, defaultVehicleId: b.Id, replace: true);

            Assert.Equal(1, added);
            Assert.Equal(new[] { b }, data.Vehicles);
            Assert.Equal(2000, data.Fuel.Single().Odometer);
            Assert.Equal(b.Id, data.Fuel.Single().VehicleId);
            Assert.Empty(data.Maintenance);
            Assert.Equal(b.Id, data.SelectedVehicleId);
        }
    }
}
