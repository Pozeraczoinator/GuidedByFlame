using System;
using UnityEngine;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

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
        private static DateTime _lastTemperatureReadUtc = DateTime.MinValue;
        private const int TemperatureRefreshIntervalMs = 1000;
        private static bool _isAsyncMonitoring = false;
        private static CancellationTokenSource _monitoringCts;
        private static bool _pluginAssemblyResolverRegistered = false;

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
            RefreshCachedTemperature();
            WarnTemperatureUnavailableOnce(_cachedTemperature);

            Task.Run(async () =>
            {
                while (!_monitoringCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TemperatureRefreshIntervalMs, _monitoringCts.Token);
                        if (!_monitoringCts.Token.IsCancellationRequested)
                        {
                            RefreshCachedTemperature();
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
                    RefreshCachedTemperatureIfStale();
                    WarnTemperatureUnavailableOnce(_cachedTemperature);
                    return _cachedTemperature;
                }
                float temperature = GetCPUTemperatureFromProviders();
                WarnTemperatureUnavailableOnce(temperature);
                return temperature;
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

        private static void WarnTemperatureUnavailableOnce(float temperature)
        {
            if (temperature >= 0f || _checkedAvailability)
                return;

            Debug.LogWarning("[HardwareMonitor] Nie znaleziono temperatury CPU przez LibreHardwareMonitorLib, Libre/OpenHardwareMonitor WMI ani ACPI. " +
                             "Aby uzyc biblioteki bezposrednio, dodaj LibreHardwareMonitorLib.dll do Assets/Plugins. " +
                             "Dla WMI uruchom LibreHardwareMonitor jako administrator i sprawdz, czy sensory sa widoczne w WMI.");
            _checkedAvailability = true;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static void RefreshCachedTemperatureIfStale()
        {
            if ((DateTime.UtcNow - _lastTemperatureReadUtc).TotalMilliseconds < TemperatureRefreshIntervalMs)
                return;

            RefreshCachedTemperature();
        }

        private static void RefreshCachedTemperature()
        {
            _cachedTemperature = GetCPUTemperatureFromProviders();
            _lastTemperatureReadUtc = DateTime.UtcNow;
        }

        private struct TemperatureCandidate
        {
            public float Value;
            public int Priority;
        }

        private static float GetCPUTemperatureFromProviders()
        {
            float temperature = GetCPUTemperatureFromLibreHardwareMonitorLib();
            if (temperature >= 0f)
                return temperature;

            temperature = GetCPUTemperatureFromLibreHardwareMonitorWeb();
            if (temperature >= 0f)
                return temperature;

            return GetCPUTemperatureWMI();
        }

        private static float GetCPUTemperatureFromLibreHardwareMonitorLib()
        {
            try
            {
                Assembly assembly = FindLibreHardwareMonitorAssembly();
                if (assembly == null)
                    return -1f;

                Type computerType = assembly.GetType("LibreHardwareMonitor.Hardware.Computer");
                if (computerType == null)
                    return -1f;

                object computer = Activator.CreateInstance(computerType);
                SetBoolProperty(computer, "IsCpuEnabled", true);
                SetBoolProperty(computer, "IsMotherboardEnabled", true);

                MethodInfo openMethod = computerType.GetMethod("Open");
                MethodInfo closeMethod = computerType.GetMethod("Close");
                PropertyInfo hardwareProperty = computerType.GetProperty("Hardware");

                openMethod?.Invoke(computer, null);

                try
                {
                    var candidates = new List<TemperatureCandidate>();
                    if (hardwareProperty?.GetValue(computer, null) is Array hardwareItems)
                    {
                        foreach (object hardware in hardwareItems)
                            CollectCpuTemperatureCandidates(hardware, candidates);
                    }

                    return SelectBestTemperature(candidates);
                }
                finally
                {
                    closeMethod?.Invoke(computer, null);
                }
            }
            catch (Exception)
            {
                return -1f;
            }
        }

        private static float GetCPUTemperatureFromLibreHardwareMonitorWeb()
        {
            int[] ports = { 8085, 8086, 8080 };
            foreach (int port in ports)
            {
                float temperature = TryReadLibreHardwareMonitorWebUrl($"http://localhost:{port}/data.json");
                if (temperature >= 0f)
                    return temperature;

                temperature = TryReadLibreHardwareMonitorWebUrl($"http://127.0.0.1:{port}/data.json");
                if (temperature >= 0f)
                    return temperature;
            }

            return -1f;
        }

        private static float TryReadLibreHardwareMonitorWebUrl(string url)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 500;
                request.ReadWriteTimeout = 500;
                request.Proxy = null;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    return ParseTemperatureFromLibreHardwareMonitorJson(reader.ReadToEnd());
                }
            }
            catch (Exception)
            {
                return -1f;
            }
        }

        private static float ParseTemperatureFromLibreHardwareMonitorJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return -1f;

            var candidates = new List<TemperatureCandidate>();
            MatchCollection matches = Regex.Matches(
                json,
                "\"Text\"\\s*:\\s*\"(?<name>[^\"]+)\"(?:(?!\"Text\"\\s*:).)*?\"Value\"\\s*:\\s*\"(?<value>-?\\d+(?:[\\.,]\\d+)?)\\s*(?:°C|C)?\"",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                string name = match.Groups["name"].Value;
                if (!ContainsInvariant(name, "cpu") &&
                    !ContainsInvariant(name, "package") &&
                    !ContainsInvariant(name, "tctl") &&
                    !ContainsInvariant(name, "tdie") &&
                    !ContainsInvariant(name, "core"))
                    continue;

                if (!float.TryParse(
                        match.Groups["value"].Value.Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float value))
                    continue;

                if (value <= 0f || value >= 130f)
                    continue;

                candidates.Add(new TemperatureCandidate
                {
                    Value = value,
                    Priority = GetTemperaturePriority(name)
                });
            }

            return SelectBestTemperature(candidates);
        }

        private static Assembly FindLibreHardwareMonitorAssembly()
        {
            RegisterPluginAssemblyResolver();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "LibreHardwareMonitorLib")
                    return assembly;
            }

            string pluginPath = Path.Combine(Application.dataPath, "Plugins");
            if (!Directory.Exists(pluginPath))
                return null;

            string[] matches = Directory.GetFiles(pluginPath, "LibreHardwareMonitorLib.dll", SearchOption.AllDirectories);
            if (matches.Length == 0)
                return null;

            return Assembly.LoadFrom(matches[0]);
        }

        private static void RegisterPluginAssemblyResolver()
        {
            if (_pluginAssemblyResolverRegistered)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembly;
            _pluginAssemblyResolverRegistered = true;
        }

        private static Assembly ResolvePluginAssembly(object sender, ResolveEventArgs args)
        {
            string pluginPath = Path.Combine(Application.dataPath, "Plugins");
            if (!Directory.Exists(pluginPath))
                return null;

            string assemblyName = new AssemblyName(args.Name).Name + ".dll";
            string[] matches = Directory.GetFiles(pluginPath, assemblyName, SearchOption.AllDirectories);
            if (matches.Length == 0)
                return null;

            return Assembly.LoadFrom(matches[0]);
        }

        private static void SetBoolProperty(object target, string propertyName, bool value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName);
            if (property != null && property.CanWrite)
                property.SetValue(target, value, null);
        }

        private static void CollectCpuTemperatureCandidates(object hardware, List<TemperatureCandidate> candidates)
        {
            if (hardware == null)
                return;

            Type hardwareType = hardware.GetType();
            hardwareType.GetMethod("Update")?.Invoke(hardware, null);

            string hardwareKind = GetPropertyString(hardware, "HardwareType");
            string hardwareName = GetPropertyString(hardware, "Name");
            bool isCpuHardware = ContainsInvariant(hardwareKind, "cpu") || ContainsInvariant(hardwareName, "cpu");

            if (hardwareType.GetProperty("Sensors")?.GetValue(hardware, null) is Array sensors)
            {
                foreach (object sensor in sensors)
                {
                    string sensorType = GetPropertyString(sensor, "SensorType");
                    if (!string.Equals(sensorType, "Temperature", StringComparison.OrdinalIgnoreCase))
                        continue;

                    float value = GetNullableFloatProperty(sensor, "Value");
                    if (value <= 0f || value >= 130f)
                        continue;

                    string sensorName = GetPropertyString(sensor, "Name");
                    string identifier = GetPropertyString(sensor, "Identifier");
                    bool looksLikeCpu = isCpuHardware ||
                                        ContainsInvariant(sensorName, "cpu") ||
                                        ContainsInvariant(identifier, "/cpu") ||
                                        ContainsInvariant(identifier, "intelcpu") ||
                                        ContainsInvariant(identifier, "amdcpu");

                    if (!looksLikeCpu)
                        continue;

                    candidates.Add(new TemperatureCandidate
                    {
                        Value = value,
                        Priority = GetTemperaturePriority(sensorName)
                    });
                }
            }

            if (hardwareType.GetProperty("SubHardware")?.GetValue(hardware, null) is Array subHardware)
            {
                foreach (object child in subHardware)
                    CollectCpuTemperatureCandidates(child, candidates);
            }
        }

        private static float SelectBestTemperature(List<TemperatureCandidate> candidates)
        {
            if (candidates.Count == 0)
                return -1f;

            TemperatureCandidate best = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                TemperatureCandidate candidate = candidates[i];
                if (candidate.Priority < best.Priority ||
                    (candidate.Priority == best.Priority && candidate.Value > best.Value))
                {
                    best = candidate;
                }
            }

            return best.Value;
        }

        private static int GetTemperaturePriority(string sensorName)
        {
            if (ContainsInvariant(sensorName, "package") ||
                ContainsInvariant(sensorName, "tctl") ||
                ContainsInvariant(sensorName, "tdie") ||
                ContainsInvariant(sensorName, "cpu die"))
                return 0;

            if (ContainsInvariant(sensorName, "core max") || ContainsInvariant(sensorName, "core"))
                return 1;

            return 2;
        }

        private static string GetPropertyString(object target, string propertyName)
        {
            object value = target.GetType().GetProperty(propertyName)?.GetValue(target, null);
            return value?.ToString() ?? string.Empty;
        }

        private static float GetNullableFloatProperty(object target, string propertyName)
        {
            object value = target.GetType().GetProperty(propertyName)?.GetValue(target, null);
            if (value == null)
                return -1f;

            try
            {
                return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return -1f;
            }
        }

        private static bool ContainsInvariant(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

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
                    if (!process.WaitForExit(3000))
                    {
                        try { process.Kill(); } catch (Exception) { }
                        return -1f;
                    }

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
                string command = @"
$namespaces = @('root\LibreHardwareMonitor', 'root\OpenHardwareMonitor')
$preferredNamePattern = 'package|tctl|tdie|cpu die|core \(max\)|cpu'
$preferredIdentifierPattern = '/cpu|intelcpu|amdcpu'

foreach ($ns in $namespaces) {
    $sensors = Get-WmiObject -Namespace $ns -Class Sensor -ErrorAction SilentlyContinue |
        Where-Object {
            $_.SensorType -eq 'Temperature' -and
            $null -ne $_.Value -and
            [double]$_.Value -gt 0 -and
            [double]$_.Value -lt 130
        }

    if ($sensors) {
        $sensor = $sensors |
            Where-Object {
                $_.Name -match $preferredNamePattern -or
                $_.Identifier -match $preferredIdentifierPattern
            } |
            Sort-Object `
                @{ Expression = {
                    if ($_.Name -match 'package|tctl|tdie|cpu die') { 0 }
                    elseif ($_.Name -match 'core') { 1 }
                    else { 2 }
                } },
                @{ Expression = { [double]$_.Value }; Descending = $true } |
            Select-Object -First 1

        if (-not $sensor) {
            $sensor = $sensors | Select-Object -First 1
        }

        if ($sensor) {
            [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:F1}', [double]$sensor.Value)
            exit 0
        }
    }
}

$acpi = Get-WmiObject -Namespace root/wmi -Class MSAcpi_ThermalZoneTemperature -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty CurrentTemperature

if ($acpi) {
    [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:F1}', (([double]$acpi / 10.0) - 273.15))
    exit 0
}

exit 1
";
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    // Pobiera temperaturę rdzeni procesora (CPU Core) za pomocą instancji udostępnianych przez external tool
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    if (!process.WaitForExit(3000))
                    {
                        try { process.Kill(); } catch (Exception) { }
                        return -1f;
                    }

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
