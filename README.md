# Dantherm2mqtt

This program connects to a Dantherm UVC Controller (Currently only tested with a HCV400) over the Modbus TCP/IP Port and publishes it current state to MQTT.

## Features

### Publish current state to MQTT

Example value posted to the state topic (By default `dantherm/status/<device-serial>`):

```json
{
  "kind": "DanthermUvc",
  "spec": {
    "address": "192.168.0.21",
    "port": 502,
    "slaveAddress": 1,
    "pollingIntervalMS": 10000
  },
  "status": {
    "macAddress": "FF:FF:FF:FF:FF:FF",
    "serialNum": 1234567890,
    "systemName": "Ventilation unit",
    "fwVersion": {
      "major": 2,
      "minor": 95
    },
    "systemId": {
      "fP1": false,
      "week": false,
      "bypass": false,
      "lrSwitch": false,
      "internalPreheater": false,
      "rhSensor": false,
      "vocSensor": false,
      "extOverride": false,
      "haC1": true,
      "hrC2": false,
      "pcTool": false,
      "apps": true,
      "zigBee": false,
      "dI1Override": false,
      "dI2Override": false,
      "unitType": 195
    },
    "halLeft": true,
    "halRight": false,
    "dateTime": "2023-01-27T17:44:40Z",
    "workTimeHours": 7215,
    "startExploitation": "2022-04-01T10:31:39Z",
    "currentBLState": "WeekProgram",
    "outdoorTemperatureC": -1.2696124,
    "supplyTemperatureC": 15.075243,
    "extractTemperatureC": 21.727291,
    "exhaustTemperatureC": 7.226437,
    "filterRemaningTimeDays": 60,
    "lastActiveAlarm": "None",
    "halFan1Rpm": 2169.6309,
    "halFan2Rpm": 2147.4563
  }
}
```

### Home Assistant MQTT Discovery

Makes the current status available automatically through the Home Assistant user interface:

![Home Assistant View](./docs/img/example-home-assistant-view.png)

### Prometheus Metric Endpoint

It has a prometheus metrics endpoint at `/metrics` with the following metrics related to Dantherm:

| Metric Name                          | Labels                       | Description                                                                                                                               |
| ------------------------------------ | ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `danthermtomqtt_last_active_alarm`   | `device_serial`              | The last active alarm, zero = none, see [Dantherm documentation](docs/Dantherm%20UVC%20Controller%20-%20Modbus%20TCP%20IP.pdf) if not `0` |
| `danthermtomqtt_last_data_pull_time` | `succeeded`, `device_serial` | The last time data either failed or pulled successfully                                                                                   |

## How to deploy

The application is distributed using a docker image available at [jonasmh/dantherm2mqtt](https://hub.docker.com/r/jonasmh/dantherm2mqtt)

### Docker Compose

Example docker-compose:

```yaml
version: "3.4"

services:
  dantherm2mqtt:
    image: jonasmh/dantherm2mqtt:latest
    environment:
      - DanthermUvcSpec__Address=192.168.0.42
      - MqttConnectionOptions__Server=192.168.0.30
```

### Configuration

| Json Key                            | Environment Varible                   | Description                         | Example        | Default       |
| ----------------------------------- | ------------------------------------- | ----------------------------------- | -------------- | ------------- |
| `DanthermUvcSpec.Address`           | `DanthermUvcSpec__Address`,           | IP of the UVC Controller            | `192.168.1.42` | `null`        |
| `DanthermUvcSpec.Port`              | `DanthermUvcSpec__Port`,              | Modbus port on the UVC Controller   | `502`          | `502`         |
| `DanthermUvcSpec.SlaveAddress`      | `DanthermUvcSpec__SlaveAddress`,      | Slave address of the UVC Controller | `1`            | `1`           |
| `DanthermUvcSpec.PollingIntervalMS` | `DanthermUvcSpec__PollingIntervalMS`, | Polling interval in ms              | `10000`        | `10000` (10s) |

It uses the library [JonasMH/ToMqttNet](https://github.com/JonasMH/ToMqttNet) to MQTT, Home Assistant Discovery and more, so some MQTT related options are inherited from there:

| Json Key                         | Environment Varible                | Description                             | Example          | Default          |
| -------------------------------- | ---------------------------------- | --------------------------------------- | ---------------- | ---------------- |
| `MqttConnectionOptions.ClientId` | `MqttConnectionOptions__ClientId`, | MQTT Client ID                          | `danthermtomqtt` | `danthermtomqtt` |
| `MqttConnectionOptions.NodeId`   | `MqttConnectionOptions__NodeId`,   | Node id, used as prefix for topics      | `dantherm`       | `dantherm`       |
| `MqttConnectionOptions.Server`   | `MqttConnectionOptions__Server`,   | Server address of the MQTT Server       | `192.168.1.42`   | `mosquitto`      |
| `MqttConnectionOptions.Port`     | `MqttConnectionOptions__Port`,     | Port to connect to the MQTT Server with | `1883`           | `1883` (10s)     |
