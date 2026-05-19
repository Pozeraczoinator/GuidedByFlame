using System;
using UnityEngine;
using System.Threading.Tasks;
using System.Threading;

namespace Pathfinding.Benchmark
{
    /// <summary>
    /// Opcjonalny moduł monitorowania zasobów sprzętowych.
    /// Próbuje odczytać temperaturę CPU przez WMI (Windows Management Instrumentation).
    /// 
    /// UWAGA: Wymaga System.Management.dll — działa TYLKO na Windows Standalone.
    /// Na innych platformach gracefully zwraca -1.
    /// 
    /// W Unity: Edytor powinien mieć dostęp do System.Management automatycznie.
    /// Jeśli nie — dodaj referencję do System.Management.dll w Assets/Plugins.
    /// 
    /// Nie wymaga zewnętrznych bibliotek (NuGet) — System.Management jest częścią .NET Framework.
    /// </summary>
    public static class HardwareMonitor
    {
        private static bool _isAvailable = true;
        private static bool _checkedAvailability = false;

        private static float _cachedTemperature = -1f;
        private static bool _isAsyncMonitoring = false;
        private static CancellationTokenSource _monitoringCts;

        /// <summary>
        /// Uruchamia asynchroniczne śledzenie temperatury w tle (aktualizacja co 5 sekund).
        /// Zapobiega blokowaniu wątku głównego Unity przy częstym sprawdzaniu w benchmarku.
        /// </summary>
        public static void StartTemperatureMonitoring()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_isAsyncMonitoring) return;
            _isAsyncMonitoring = true;
            _monitoringCts = new CancellationTokenSource();

            // Szybki pierwszy odczyt
            _cachedTemperature = GetCPUTemperatureWMI();

            Task.Run(async () =>
            {
                while (!_monitoringCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(5000, _monitoringCts.Token);
                        if (!_monitoringCts.Token.IsCancellationRequested)
                        {
                            _cachedTemperature = GetCPUTemperatureWMI();
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        break; // Normalne zakończenie
                    }
                }
            }, _monitoringCts.Token);
#endif
        }

        /// <summary>
        /// Zatrzymuje asynchroniczne śledzenie temperatury.
        /// </summary>
        public static void StopTemperatureMonitoring()
        {
            if (!_isAsyncMonitoring) return;
            _isAsyncMonitoring = false;
            if (_monitoringCts != null)
            {
                _monitoringCts.Cancel();
                _monitoringCts.Dispose();
                _monitoringCts = null;
            }
        }

        /// <summary>
        /// Próbuje odczytać temperaturę CPU w stopniach Celsjusza.
        /// Zwraca -1 jeśli odczyt niemożliwy (brak WMI, inna platforma, brak uprawnień).
        /// 
        /// UWAGA: WMI query jest KOSZTOWNE (~50-200ms). NIE wywołuj w każdej klatce.
        /// Wywołuj raz na początku/końcu serii benchmarkowej.
        /// 
        /// Złożoność: O(1) jeśli cache, O(n) przy pierwszym wywołaniu WMI
        /// gdzie n = liczba sensorów temperatury w systemie.
        /// </summary>
        /// <returns>Temperatura CPU w °C lub -1 jeśli niedostępna</returns>
        public static float GetCPUTemperature()
        {
            if (!_isAvailable)
                return -1f;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                if (_isAsyncMonitoring)
                {
                    return _cachedTemperature;
                }
                return GetCPUTemperatureWMI();
            }
            catch (Exception ex)
            {
                if (!_checkedAvailability)
                {
                    Debug.LogWarning($"[HardwareMonitor] Nie można odczytać temperatury CPU: {ex.Message}. " +
                                     "Dalsze próby zostaną pominięte. Temperatura będzie raportowana jako -1.");
                    _isAvailable = false;
                    _checkedAvailability = true;
                }
                return -1f;
            }
#else
            if (!_checkedAvailability)
            {
                Debug.LogWarning("[HardwareMonitor] Monitoring temperatury CPU dostępny tylko na Windows.");
                _checkedAvailability = true;
                _isAvailable = false;
            }
            return -1f;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>
        /// Wewnętrzna metoda do odczytu temperatury przez WMI.
        /// Korzysta z namespace MSAcpi_ThermalZoneTemperature.
        /// Wartość WMI jest w dziesiątkach Kelvinów — konwertujemy na Celsjusze.
        /// </summary>
        private static float GetCPUTemperatureWMI()
        {
            // System.Management musi być dostępne w projekcie
            // Poniższy kod wymaga: using System.Management;
            // Jeśli brak referencji — ten blok nie skompiluje się.
            // Unity Editor na Windows zazwyczaj ma System.Management.dll z .NET Framework.

            // ------------------------------------------------------------------------------------------
            // PROBLEM: Laptopy (szczególnie HP Pavilion i inne modele firmowe) blokują 
            // bezpośredni dostęp do sensorów ACPI (MSAcpi_ThermalZoneTemperature) z poziomu 
            // systemu Windows, co zwraca błąd WMI lub po prostu pusty wynik, nawet z uprawnieniami Admina.
            // 
            // ZAKOMENTOWANA PRÓBA (ORYGINAŁ):
            /*
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-WmiObject MSAcpi_ThermalZoneTemperature -Namespace root/wmi -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty CurrentTemperature\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    process.WaitForExit(3000);
                    string output = process.StandardOutput.ReadToEnd().Trim();

                    if (uint.TryParse(output, out uint tempKelvinTenths))
                    {
                        return (tempKelvinTenths / 10f) - 273.15f;
                    }
                }
            }
            catch (Exception) { }
            */
            // ------------------------------------------------------------------------------------------

            // NOWE ROZWIĄZANIE: Odczyt za pomocą OpenHardwareMonitor / LibreHardwareMonitor
            // Aplikacje te udostępniają swoje dane w ogólnodostępnym kanale WMI, 
            // do którego Unity ma dostęp bez problemu.
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    // Pobiera temperaturę rdzeni procesora (CPU Core) za pomocą instancji udostępnianych przez external tool
                    Arguments = "-NoProfile -Command \"Get-WmiObject -Namespace root\\OpenHardwareMonitor -Class Sensor -ErrorAction SilentlyContinue | Where-Object { $_.SensorType -eq 'Temperature' -and $_.Name -like '*CPU*' } | Select-Object -First 1 -ExpandProperty Value\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    process.WaitForExit(2000); 
                    string output = process.StandardOutput.ReadToEnd().Trim();

                    // Parsowanie float z kropką lub przecinkiem, zależnie od lokalizacji
                    if (!string.IsNullOrEmpty(output) && float.TryParse(output.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float tempCelsius))
                    {
                        return tempCelsius;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HardwareMonitor] OHM/LHM WMI query failed: {ex.Message}");
            }

            return -1f;
        }
#endif

        /// <summary>
        /// Zbiera snapshot informacji o pamięci zarządzanej (GC).
        /// Bezpieczne na wszystkich platformach.
        /// 
        /// Złożoność: O(1).
        /// </summary>
        /// <returns>Całkowita ilość pamięci zarządzanej w bajtach</returns>
        public static long GetManagedMemoryBytes()
        {
            return GC.GetTotalMemory(false);
        }

        /// <summary>
        /// Wymusza pełne zbieranie śmieci i zwraca ilość pamięci po cleanup.
        /// UWAGA: GC.Collect() jest KOSZTOWNE. Używaj wyłącznie między seriami testów,
        /// nigdy w trakcie pomiarów.
        /// 
        /// Złożoność: O(n) gdzie n = ilość obiektów na stercie zarządzanej.
        /// </summary>
        public static long ForceGCAndGetMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(true);
        }
    }
}
