using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitorApp
{
    /// <summary>
    /// 하드웨어 모니터링 메인 로직
    /// </summary>
    public partial class MainWindow : Window
    {
        private Computer _computer;
        private DispatcherTimer _timer;

        // CD-ROM 트레이 제어를 위한 Win32 API 임포트
        [DllImport("winmm.dll", EntryPoint = "mciSendStringA", CharSet = CharSet.Ansi)]
        protected static extern int mciSendString(string lpstrCommand, string lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

        public MainWindow()
        {
            InitializeComponent();
            SetupMonitor(); // 센서 설정
            StartTimer();   // 갱신 타이머 시작
        }

        /// <summary>
        /// 하드웨어 모니터링 객체 초기화
        /// </summary>
        private void SetupMonitor()
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsStorageEnabled = true
                };
                _computer.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"하드웨어 정보를 읽어오는데 실패했습니다. 관리자 권한으로 실행 중인지 확인해 주세요.\n\n오류 내용: {ex.Message}", "권한 또는 시스템 오류");
            }
        }

        /// <summary>
        /// 2초마다 데이터를 갱신하는 타이머 설정
        /// </summary>
        private void StartTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += (s, e) => UpdateStats();
            _timer.Start();
            UpdateStats(); // 초기 1회 즉시 실행
        }

        /// <summary>
        /// 실시간 하드웨어 상태 업데이트 로직
        /// </summary>
        private void UpdateStats()
        {
            if (_computer == null) return;

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();

                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            // CPU 온도 업데이트
                            if (hardware.HardwareType == HardwareType.Cpu && (sensor.Name.Contains("Package") || sensor.Name.Contains("Core (Average)")))
                            {
                                TxtCpuTemp.Text = $"{sensor.Value:F0} °C";
                                PbCpuTemp.Value = (double)(sensor.Value ?? 0);
                            }
                            // GPU 온도 업데이트
                            else if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                            {
                                TxtGpuTemp.Text = $"{sensor.Value:F0} °C";
                                PbGpuTemp.Value = (double)(sensor.Value ?? 0);
                            }
                            // 저장장치(HDD/SSD) 온도 업데이트
                            else if (hardware.HardwareType == HardwareType.Storage)
                            {
                                TxtHddTemp.Text = $"{sensor.Value:F0} °C";
                                PbHddTemp.Value = (double)(sensor.Value ?? 0);
                            }
                        }
                    }
                }

                UpdateHddHours(); // HDD 가동 시간 조회
            }
            catch
            {
                // 센서 데이터 유실 시 조용히 넘어감
            }
        }

        /// <summary>
        /// WMI를 사용하여 하드디스크의 총 가동 시간을 조회합니다.
        /// </summary>
        private void UpdateHddHours()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSStorageDriver_FailurePredictData");
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    byte[] vendorSpecific = (byte[])queryObj["VendorSpecific"];
                    // S.M.A.R.T Attribute ID 0x09 (Power-on Hours) 데이터를 파싱합니다.
                    int hours = vendorSpecific[101] + (vendorSpecific[102] << 8); 
                    TxtHddHours.Text = $"{hours:N0} 시간";
                }
            }
            catch
            {
                TxtHddHours.Text = "조회 불가";
            }
        }

        /// <summary>
        /// CD-ROM 트레이 열기 버튼 클릭 이벤트
        /// </summary>
        private void EjectCD_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                mciSendString("set cdaudio door open", null, 0, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"CD-ROM을 여는 도중 오류가 발생했습니다: {ex.Message}");
            }
        }

        /// <summary>
        /// 창 닫기 버튼 클릭 이벤트
        /// </summary>
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _computer?.Close();
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 창 드래그 이동을 위한 이벤트 처리
        /// </summary>
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                this.DragMove();
        }
    }
}
