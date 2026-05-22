using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Collections.Generic;

class Test
{
    private struct TemperatureCandidate
    {
        public float Value;
        public int Priority;
    }

    static void Main()
    {
        float tempWeb = GetCPUTemperatureFromLibreHardwareMonitorWeb();
        Console.WriteLine("TempWeb: " + tempWeb);
    }

    private static float GetCPUTemperatureFromLibreHardwareMonitorWeb()
    {
        int[] ports = { 8085, 8086, 8080 };
        foreach (int port in ports)
        {
            float temperature = TryReadLibreHardwareMonitorWebUrl(string.Format("http://localhost:{0}/data.json", port));
            if (temperature >= 0f) return temperature;

            temperature = TryReadLibreHardwareMonitorWebUrl(string.Format("http://127.0.0.1:{0}/data.json", port));
            if (temperature >= 0f) return temperature;
        }
        return -1f;
    }

    private static float TryReadLibreHardwareMonitorWebUrl(string url)
    {
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 2000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                return ParseTemperatureFromLibreHardwareMonitorJson(reader.ReadToEnd());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error Web: " + ex.Message);
            return -1f;
        }
    }

    private static float ParseTemperatureFromLibreHardwareMonitorJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return -1f;

        var candidates = new List<TemperatureCandidate>();
        MatchCollection matches = Regex.Matches(
            json,
            "\"Text\"\\s*:\\s*\"(?<name>[^\"]+)\"(?:(?!\"Text\"\\s*:).)*?\"Value\"\\s*:\\s*\"(?<value>-?\\d+(?:[\\.,]\\d+)?)[^\"]*\"",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        Console.WriteLine("Matches found: " + matches.Count);
        foreach (Match match in matches)
        {
            string name = match.Groups["name"].Value;
            string rawValue = match.Groups["value"].Value;
            
            bool contains = ContainsInvariant(name, "cpu") || ContainsInvariant(name, "package") || ContainsInvariant(name, "tctl") || ContainsInvariant(name, "tdie") || ContainsInvariant(name, "core");
            if (!contains) continue;

            float value;
            if (!float.TryParse(rawValue.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value))
                continue;

            Console.WriteLine(string.Format("Candidate: {0} = {1}", name, value));

            if (value <= 0f || value >= 130f) continue;

            candidates.Add(new TemperatureCandidate { Value = value, Priority = GetTemperaturePriority(name) });
        }

        return SelectBestTemperature(candidates);
    }

    private static float SelectBestTemperature(List<TemperatureCandidate> candidates)
    {
        if (candidates.Count == 0) return -1f;

        TemperatureCandidate best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            if (candidates[i].Priority < best.Priority || (candidates[i].Priority == best.Priority && candidates[i].Value > best.Value))
            {
                best = candidates[i];
            }
        }
        return best.Value;
    }

    private static int GetTemperaturePriority(string sensorName)
    {
        if (ContainsInvariant(sensorName, "package") || ContainsInvariant(sensorName, "tctl") || ContainsInvariant(sensorName, "tdie") || ContainsInvariant(sensorName, "cpu die")) return 0;
        if (ContainsInvariant(sensorName, "core max") || ContainsInvariant(sensorName, "core")) return 1;
        return 2;
    }

    private static bool ContainsInvariant(string source, string value)
    {
        return !string.IsNullOrEmpty(source) && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
