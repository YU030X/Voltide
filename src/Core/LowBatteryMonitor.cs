using System;
using System.Collections.Generic;

namespace Voltide.Core
{
    public class LowBatteryAlert
    {
        public string DeviceId;
        public string DeviceName;
        public int Percent;
    }

    /// <summary>
    /// Tracks which devices have already triggered a low-battery alert so the user
    /// is notified once per discharge below the configured threshold, and resets
    /// cleanly when the device charges or rises back above threshold + hysteresis.
    /// </summary>
    public class LowBatteryMonitor
    {
        private const int Hysteresis = 5;

        private readonly HashSet<string> _alerted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _lastThreshold = -1;

        public void Reset()
        {
            _alerted.Clear();
        }

        public List<LowBatteryAlert> Evaluate(List<DeviceSnapshot> snapshots, int threshold)
        {
            List<LowBatteryAlert> triggered = new List<LowBatteryAlert>();

            if (threshold != _lastThreshold)
            {
                _lastThreshold = threshold;
                _alerted.Clear();
            }

            if (threshold <= 0 || snapshots == null) return triggered;

            foreach (DeviceSnapshot snap in snapshots)
            {
                if (snap == null || snap.Info == null || snap.Reading == null) continue;
                if (!snap.Reading.Online || !snap.Reading.Percent.HasValue) continue;

                string id = snap.Info.Id;
                if (string.IsNullOrEmpty(id)) continue;

                int percent = snap.Reading.Percent.Value;

                if (snap.Reading.Charging)
                {
                    _alerted.Remove(id);
                    continue;
                }

                if (percent >= threshold + Hysteresis)
                {
                    _alerted.Remove(id);
                    continue;
                }

                if (percent < threshold && _alerted.Add(id))
                {
                    LowBatteryAlert alert = new LowBatteryAlert();
                    alert.DeviceId = id;
                    alert.DeviceName = FormatName(snap.Info);
                    alert.Percent = percent;
                    triggered.Add(alert);
                }
            }

            return triggered;
        }

        private static string FormatName(DeviceInfo info)
        {
            string brand = info.Brand != null ? info.Brand.Trim() : "";
            string model = info.Model != null ? info.Model.Trim() : "";

            if (!string.IsNullOrEmpty(brand) && !string.IsNullOrEmpty(model))
            {
                if (model.StartsWith(brand, StringComparison.OrdinalIgnoreCase))
                    return model;
                return brand + " " + model;
            }

            if (!string.IsNullOrEmpty(info.Name)) return info.Name.Trim();
            if (!string.IsNullOrEmpty(model)) return model;
            if (!string.IsNullOrEmpty(brand)) return brand;
            return "未知设备";
        }
    }
}
