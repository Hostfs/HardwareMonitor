using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace HardwareMonitorApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 관리자 권한인지 체크
            if (!IsAdministrator())
            {
                // 관리자 권한으로 재시작 시도
                RestartAsAdmin();
                return;
            }

            base.OnStartup(e);
        }

        /// <summary>
        /// 현재 프로세스가 관리자 권한으로 실행 중인지 확인합니다.
        /// </summary>
        private bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// 프로그램을 관리자 권한으로 다시 실행합니다.
        /// </summary>
        private void RestartAsAdmin()
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule?.FileName,
                UseShellExecute = true,
                Verb = "runas" // 관리자 권한 상승 트리거
            };

            try
            {
                Process.Start(processInfo);
            }
            catch
            {
                // 사용자가 UAC 창에서 '아니오'를 눌렀을 때
                MessageBox.Show("하드웨어 센서 데이터에 접근하려면 관리자 권한이 반드시 필요합니다.", "권한 거부", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 현재 일반 권한 프로세스 종료
            Application.Current.Shutdown();
        }
    }
}
