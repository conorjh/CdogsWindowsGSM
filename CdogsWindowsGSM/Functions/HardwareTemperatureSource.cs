using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using LibreHardwareMonitor.Hardware;

namespace WindowsGSM.Functions
{
    public sealed class TemperatureReading
    {
        public string DeviceName { get; private set; }
        public string SensorName { get; private set; }
        public double Celsius { get; private set; }

        public TemperatureReading(string deviceName, string sensorName, double celsius)
        {
            DeviceName = deviceName;
            SensorName = sensorName;
            Celsius = celsius;
        }
    }

    public sealed class HardwareTemperatureSample
    {
        public TemperatureReading Cpu { get; private set; }
        public IReadOnlyList<TemperatureReading> Gpus { get; private set; }
        public DateTime? SampledAt { get; private set; }
        public string Error { get; private set; }

        public HardwareTemperatureSample(
            TemperatureReading cpu,
            IReadOnlyList<TemperatureReading> gpus,
            DateTime? sampledAt,
            string error)
        {
            Cpu = cpu;
            Gpus = gpus ?? throw new ArgumentNullException(nameof(gpus));
            SampledAt = sampledAt;
            Error = error;
        }
    }

    public interface IHardwareTemperatureSource : IDisposable
    {
        HardwareTemperatureSample Sample();
    }

    public sealed class LibreHardwareTemperatureSource : IHardwareTemperatureSource
    {
        private readonly object _sync = new object();
        private readonly Computer _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true
        };
        private bool _opened;

        public HardwareTemperatureSample Sample()
        {
            lock (_sync)
            {
                try
                {
                    if (!_opened)
                    {
                        _computer.Open();
                        _opened = true;
                    }

                    var hardware = new List<IHardware>();
                    foreach (IHardware device in _computer.Hardware)
                    {
                        UpdateHardware(device, hardware);
                    }

                    TemperatureReading cpu = hardware
                        .Where(device => device.HardwareType == HardwareType.Cpu)
                        .SelectMany(device => device.Sensors
                            .Where(IsUsableTemperature)
                            .Select(sensor => new TemperatureReading(device.Name, sensor.Name, sensor.Value.Value)))
                        .OrderByDescending(reading => IsPackageTemperature(reading.SensorName))
                        .ThenByDescending(reading => reading.Celsius)
                        .FirstOrDefault();

                    List<TemperatureReading> gpus = hardware
                        .Where(device => device.HardwareType.ToString().StartsWith("Gpu", StringComparison.OrdinalIgnoreCase))
                        .Select(device => new
                        {
                            Device = device,
                            Sensor = device.Sensors
                                .Where(IsUsableTemperature)
                                .OrderBy(sensor => IsGpuHotspot(sensor.Name) ? 1 : 0)
                                .ThenBy(sensor => IsGpuCore(sensor.Name) ? 0 : 1)
                                .FirstOrDefault()
                        })
                        .Where(item => item.Sensor != null)
                        .Select(item => new TemperatureReading(item.Device.Name, item.Sensor.Name, item.Sensor.Value.Value))
                        .ToList();

                    return new HardwareTemperatureSample(cpu, gpus, DateTime.Now, null);
                }
                catch (Exception exception) when (IsExpectedSensorException(exception))
                {
                    Trace.TraceWarning("Hardware temperature sampling failed: {0}", exception);
                    return new HardwareTemperatureSample(null, new List<TemperatureReading>(), null, exception.Message);
                }
            }
        }

        private static void UpdateHardware(IHardware hardware, ICollection<IHardware> devices)
        {
            hardware.Update();
            devices.Add(hardware);
            foreach (IHardware child in hardware.SubHardware)
            {
                UpdateHardware(child, devices);
            }
        }

        private static bool IsUsableTemperature(ISensor sensor)
        {
            return sensor.SensorType == SensorType.Temperature &&
                sensor.Value.HasValue &&
                !float.IsNaN(sensor.Value.Value) &&
                !float.IsInfinity(sensor.Value.Value);
        }

        private static bool IsPackageTemperature(string sensorName)
        {
            return sensorName.IndexOf("package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sensorName.IndexOf("tctl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sensorName.IndexOf("tdie", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsGpuCore(string sensorName)
        {
            return sensorName.IndexOf("core", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsGpuHotspot(string sensorName)
        {
            return sensorName.IndexOf("hot spot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sensorName.IndexOf("hotspot", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsExpectedSensorException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidOperationException ||
                exception is NotSupportedException ||
                exception is Win32Exception ||
                exception is SecurityException ||
                exception is DllNotFoundException ||
                exception is BadImageFormatException ||
                exception is TypeInitializationException;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (!_opened) { return; }

                try
                {
                    _computer.Close();
                    _opened = false;
                }
                catch (Exception exception) when (IsExpectedSensorException(exception))
                {
                    Trace.TraceWarning("Hardware temperature provider cleanup failed: {0}", exception);
                }
            }
        }
    }
}
