BrokerWidget

Connects to an external or widget-owned MQTT ItemBroker bus with a generated readonly local client id, exposes remote retained and live item trees under `Attached To UI`, publishes active local `Published Items` definitions to shared flat broker topics, and writes external broker updates back only for active definitions marked `Writable=true`.
