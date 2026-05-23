#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-observatory-rg}"
NAMESPACE="${SERVICEBUS_NAMESPACE:-hvoobs}"
TOPIC="${SERVICEBUS_INGEST_TOPIC:-hvo-ingest}"
SEND_POLICY="${SERVICEBUS_RABBITMQ_SEND_POLICY:-rabbitmq-shovel-send}"

subscriptions=(
  "weather-raw-v1:hvo.weather.raw.v1"
  "weather-sensor-v1:hvo.weather.sensor.v1"
  "weather-alarm-v1:hvo.weather.alarm.v1"
  "environment-reading-v1:hvo.environment.reading.v1"
  "power-reading-v1:hvo.power.reading.v1"
  "bms-reading-v1:hvo.bms.reading.v1"
)

echo "Configuring Service Bus topic ${NAMESPACE}/${TOPIC} in ${RESOURCE_GROUP}"

az servicebus topic show \
  --resource-group "${RESOURCE_GROUP}" \
  --namespace-name "${NAMESPACE}" \
  --name "${TOPIC}" \
  --output none 2>/dev/null || \
az servicebus topic create \
  --resource-group "${RESOURCE_GROUP}" \
  --namespace-name "${NAMESPACE}" \
  --name "${TOPIC}" \
  --default-message-time-to-live P14D \
  --max-size 1024 \
  --output none

for entry in "${subscriptions[@]}"; do
  subscription="${entry%%:*}"
  schema="${entry#*:}"

  az servicebus topic subscription show \
    --resource-group "${RESOURCE_GROUP}" \
    --namespace-name "${NAMESPACE}" \
    --topic-name "${TOPIC}" \
    --name "${subscription}" \
    --output none 2>/dev/null || \
  az servicebus topic subscription create \
    --resource-group "${RESOURCE_GROUP}" \
    --namespace-name "${NAMESPACE}" \
    --topic-name "${TOPIC}" \
    --name "${subscription}" \
    --max-delivery-count 10 \
    --dead-letter-on-filter-exceptions true \
    --output none

  az servicebus topic subscription rule delete \
    --resource-group "${RESOURCE_GROUP}" \
    --namespace-name "${NAMESPACE}" \
    --topic-name "${TOPIC}" \
    --subscription-name "${subscription}" \
    --name '$Default' \
    --output none 2>/dev/null || true

  az servicebus topic subscription rule delete \
    --resource-group "${RESOURCE_GROUP}" \
    --namespace-name "${NAMESPACE}" \
    --topic-name "${TOPIC}" \
    --subscription-name "${subscription}" \
    --name schema-filter \
    --output none 2>/dev/null || true

  az servicebus topic subscription rule create \
    --resource-group "${RESOURCE_GROUP}" \
    --namespace-name "${NAMESPACE}" \
    --topic-name "${TOPIC}" \
    --subscription-name "${subscription}" \
    --name schema-filter \
    --filter-sql-expression "schema = '${schema}'" \
    --output none
done

az servicebus topic authorization-rule show \
  --resource-group "${RESOURCE_GROUP}" \
  --namespace-name "${NAMESPACE}" \
  --topic-name "${TOPIC}" \
  --name "${SEND_POLICY}" \
  --output none 2>/dev/null || \
az servicebus topic authorization-rule create \
  --resource-group "${RESOURCE_GROUP}" \
  --namespace-name "${NAMESPACE}" \
  --topic-name "${TOPIC}" \
  --name "${SEND_POLICY}" \
  --rights Send \
  --output none

echo "Service Bus ingest topology is configured."
echo "Connection strings are secrets; retrieve the AMQP connection string only on the target host or in Key Vault."
