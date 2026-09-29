# ProtoTest.Devices.Mqtt.Testcontainers

> Preview: the surface can change before 1.2.

A Mosquitto container that can be owned by a ProtoTest run, for suites that talk to devices over MQTT.

```bash
dotnet add package ProtoTest.Devices.Mqtt.Testcontainers
```

`MosquittoBroker.Container()` creates the resource. Register it as the `UseContainer(...)` provider of
the target that declares `MqttDeviceOptions.BrokerSetting`, so the MQTT clients and anything else that
reads the key resolve the same broker; see `ProtoTest.Testcontainers` for the shared container contract:

```csharp
builder.AddInfrastructure("Mqtt", chain => chain
    .UseConfigured()
    .UseContainer(MosquittoBroker.Container()), MqttDeviceOptions.BrokerSetting);
```

The container starts before individual test skip conditions are evaluated. Check container availability
before registration when the suite needs a fallback.

## Learn more

- [Devices](https://prototest.dev/docs/integrations/devices)
- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
