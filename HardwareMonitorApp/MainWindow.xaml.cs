using System.Collections.ObjectModel;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitorApp
{
    public class StorageInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "Disk"; 
        public float? Temperature { get; set; }
        public string TemperatureDisplay => Temperature.HasValue ? $"{Temperature:F0} °C" : "N/A";
        public string PowerOnHours { get; set; } = "조회 중...";
    }

    public partial class MainWindow : Window
    {
        private Computer? _computer;
        private DispatcherTimer? _timer;
        public ObservableCollection<StorageInfo> StorageDevices { get; set; } = new ObservableCollection<StorageInfo>();

        [DllImport("winmm.dll", EntryPoint = "mciSendStringA", CharSet = CharSet.Ansi)]
        protected static extern int mciSendString(string lpstrCommand, string lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

        public MainWindow()
        {
            InitializeComponent();
            ListStorage.ItemsSource = StorageDevices;
            SetupMonitor();
            StartTimer();
        }

        private void SetupMonitor()
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsStorageEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true
                };
                _computer.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"초기화 오류: {ex.Message}");
            }
        }

        private void StartTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (s, e) => UpdateStats();
            _timer.Start();
            UpdateStats();
        }

        private void UpdateStats()
        {
            if (_computer == null) return;

            try
            {
                var currentStorages = new List<StorageInfo>();

                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();

                    // CPU/GPU 로직 (기존과 동일)
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        TxtCpuName.Text = hardware.Name;
                        var temp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && (s.Name.Contains("Package") || s.Name.Contains("Core") || s.Name.Contains("Average"))) ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                        if (temp != null) { TxtCpuTemp.Text = $"{temp.Value:F0} °C"; PbCpuTemp.Value = (double)(temp.Value ?? 0); }
                    }

                    if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        TxtGpuName.Text = hardware.Name;
                        var temp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && (s.Name.Contains("Core") || s.Name.Contains("GPU")));
                        if (temp != null) { TxtGpuTemp.Text = $"{temp.Value:F0} °C"; PbGpuTemp.Value = (double)(temp.Value ?? 0); }
                    }

                    // 저장장치 로직 개선
                    if (hardware.HardwareType == HardwareType.Storage)
                    {
                        var tempSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                        
                        // 1. LibreHardwareMonitor에서 제공하는 가동 시간 센서가 있는지 확인 (NVMe 등에 유리)
                        var hoursSensor = hardware.Sensors.FirstOrDefault(s => s.Name.ToLower().Contains("power-on hours") || s.Name.Contains("가동 시간"));
                        string hoursText = "조회 불가";

                        if (hoursSensor != null && hoursSensor.Value.HasValue)
                        {
                            hoursText = $"{hoursSensor.Value:N0} 시간";
                        }
                        else
                        {
                            // 2. 센서가 없다면 WMI를 통해 정밀 조회
                            hoursText = GetPowerOnHoursDetailed(hardware.Name);
                        }

                        currentStorages.Add(new StorageInfo 
                        { 
                            Name = hardware.Name, 
                            Type = GetStorageTypeDetailed(hardware.Name),
                            Temperature = tempSensor?.Value,
                            PowerOnHours = hoursText
                        });
                    }
                }

                // UI 업데이트
                StorageDevices.Clear();
                foreach (var s in currentStorages) StorageDevices.Add(s);
            }
            catch { }
        }

        private string GetStorageTypeDetailed(string modelName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"\\.\root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");
                foreach (ManagementObject drive in searcher.Get())
                {
                    string? friendlyName = drive["FriendlyName"]?.ToString();
                    if (friendlyName != null && friendlyName.IndexOf(modelName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        UInt16 mediaType = (UInt16)drive["MediaType"];
                        if (mediaType == 4) return "SSD";
                        if (mediaType == 3) return "HDD";
                    }
                }
            }
            catch { }
            return "Disk";
        }

        private string GetPowerOnHoursDetailed(string modelName)
        {
            try
            {
                // Win32_DiskDrive에서 모델명으로 장치 PNP ID를 찾습니다.
                string deviceId = "";
                using (var driveSearcher = new ManagementObjectSearcher("SELECT PNPDeviceID, Model FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject drive in driveSearcher.Get())
                    {
                        string driveModel = drive["Model"]?.ToString() ?? "";
                        if (driveModel.IndexOf(modelName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            deviceId = drive["PNPDeviceID"]?.ToString() ?? "";
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(deviceId)) return "N/A";

                // WMI S.M.A.R.T 데이터에서 해당 장치의 인스턴스를 찾습니다.
                using var smartSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSStorageDriver_FailurePredictData");
                foreach (ManagementObject queryObj in smartSearcher.Get())
                {
                    string instanceName = queryObj["InstanceName"]?.ToString() ?? "";
                    // 인스턴스 이름에 장치 ID가 포함되어 있는지 확인 (특수문자 치환 대응)
                    if (instanceName.ToUpper().Contains(deviceId.Replace("\\", "_").ToUpper()))
                    {
                        byte[] vendorSpecific = (byte[])queryObj["VendorSpecific"];
                        // S.M.A.R.T 속성 테이블 순회 (Index 2부터 12바이트씩)
                        for (int i = 2; i <= vendorSpecific.Length - 12; i += 12)
                        {
                            if (vendorSpecific[i] == 0x09) // Power-On Hours Attribute ID
                            {
                                // 4바이트 정수 값 추출 (Little Endian)
                                uint hours = BitConverter.ToUInt32(vendorSpecific, i + 5);
                                return $"{hours:N0} 시간";
                            }
                        }
                    }
                }
            }
            catch { }
            return "조회 불가";
        }

        private void EjectCD_Click(object sender, RoutedEventArgs e)
        {
            if (!DriveInfo.GetDrives().Any(d => d.DriveType == DriveType.CDRom))
            {
                MessageBox.Show("CD-ROM 드라이브를 찾을 수 없습니다.");
                return;
            }
            mciSendString("set cdaudio door open", null, 0, IntPtr.Zero);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _computer?.Close();
            Application.Current.Shutdown();
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) this.DragMove();
        }
    }
}
