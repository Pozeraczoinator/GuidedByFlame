using System;
using System.Collections.Generic;
using System.Reflection;

class TestDirect
{
    private struct TemperatureCandidate
    {
        public float Value;
        public int Priority;
    }

    static void Main()
    {
        try
        {
            float temp = GetCPUTemperatureFromLibreHardwareMonitorLib();
            Console.WriteLine("TempDirect: " + temp);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception: " + ex);
        }
    }

    private static float GetCPUTemperatureFromLibreHardwareMonitorLib()
    {
        try
        {
            Assembly assembly = Assembly.LoadFrom(@"C:\Users\Windows 10\Desktop\Projekt\GuidedByFlame\Guided By Flame\Assets\Plugins\LibreHardwareMonitorLib.dll");
            if (assembly == null)
            {
                Console.WriteLine("Assembly is null");
                return -1f;
            }

            Type computerType = assembly.GetType("LibreHardwareMonitor.Hardware.Computer");
            if (computerType == null)
            {
                Console.WriteLine("computerType is null");
                return -1f;
            }

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
                var hardwareVal = hardwareProperty?.GetValue(computer, null);
                Console.WriteLine("Hardware property value type: " + (hardwareVal?.GetType().ToString() ?? "null"));
                
                if (hardwareVal is System.Collections.IEnumerable hardwareItems)
                {
                    int count = 0;
                    foreach (object hardware in hardwareItems)
                    {
                        count++;
                        CollectCpuTemperatureCandidates(hardware, candidates);
                    }
                    Console.WriteLine("Hardware items count: " + count);
                }
                else
                {
                    Console.WriteLine("Not IEnumerable: " + hardwareVal);
                }

                Console.WriteLine("Candidates found: " + candidates.Count);
                foreach (var c in candidates) Console.WriteLine("Candidate: " + c.Value);

                return SelectBestTemperature(candidates);
            }
            finally
            {
                closeMethod?.Invoke(computer, null);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Inner Exception: " + ex);
            return -1f;
        }
    }

    private static void SetBoolProperty(object target, string propertyName, bool value)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName);
        if (property != null && property.CanWrite)
            property.SetValue(target, value, null);
    }

    private static void CollectCpuTemperatureCandidates(object hardware, List<TemperatureCandidate> candidates)
    {
        if (hardware == null) return;

        Type hardwareType = hardware.GetType();
        hardwareType.GetMethod("Update")?.Invoke(hardware, null);

        string hardwareKind = GetPropertyString(hardware, "HardwareType");
        string hardwareName = GetPropertyString(hardware, "Name");
        bool isCpuHardware = ContainsInvariant(hardwareKind, "cpu") || ContainsInvariant(hardwareName, "cpu");

        var sensorsVal = hardwareType.GetProperty("Sensors")?.GetValue(hardware, null);
        if (sensorsVal is System.Collections.IEnumerable sensors)
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

                if (!looksLikeCpu) continue;

                candidates.Add(new TemperatureCandidate { Value = value, Priority = GetTemperaturePriority(sensorName) });
            }
        }

        var subHardwareVal = hardwareType.GetProperty("SubHardware")?.GetValue(hardware, null);
        if (subHardwareVal is System.Collections.IEnumerable subHardware)
        {
            foreach (object child in subHardware)
                CollectCpuTemperatureCandidates(child, candidates);
        }
    }

    private static float SelectBestTemperature(List<TemperatureCandidate> candidates)
    {
        if (candidates.Count == 0) return -1f;
        TemperatureCandidate best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            if (candidates[i].Priority < best.Priority || (candidates[i].Priority == best.Priority && candidates[i].Value > best.Value))
                best = candidates[i];
        }
        return best.Value;
    }

    private static int GetTemperaturePriority(string sensorName)
    {
        if (ContainsInvariant(sensorName, "package") || ContainsInvariant(sensorName, "tctl") || ContainsInvariant(sensorName, "tdie") || ContainsInvariant(sensorName, "cpu die")) return 0;
        if (ContainsInvariant(sensorName, "core max") || ContainsInvariant(sensorName, "core")) return 1;
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
        if (value == null) return -1f;
        try { return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture); } catch { return -1f; }
    }

    private static bool ContainsInvariant(string source, string value)
    {
        return !string.IsNullOrEmpty(source) && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
