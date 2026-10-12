using Microsoft.Extensions.Logging;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;
using MqttRoutingSample.Models;

namespace MqttRoutingSample.Controllers;

[MqttController]
public sealed class TelemetryController(ILogger<TelemetryController> logger) : MqttBaseController
{
    public static int TemperatureCount { get; private set; }
    public static int MotionCount { get; private set; }

    [MqttRoute("demo/telemetry/{deviceId:int}")]
    public async Task OnTemperature(int deviceId, [FromPayload] TemperatureReading reading,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Device {DeviceId}: {Celsius} °C", deviceId, reading.Celsius);
        await Task.Delay(10, cancellationToken);
        TemperatureCount++;
    }

    [MqttRoute("demo/motion/{deviceId}")]
    public void OnMotion(string deviceId)
    {
        logger.LogInformation("Motion at {DeviceId}", deviceId);
        MotionCount++;
    }
}
